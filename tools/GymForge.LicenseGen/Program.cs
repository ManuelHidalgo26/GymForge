using System.Diagnostics;
using System.Text;
using System.Security.Cryptography;
using GymForge.Application.UseCases.Licensing;

// Herramienta del VENDEDOR (no se distribuye con la app): emite claves de
// licencia firmadas con ECDSA P-256. La clave privada vive FUERA del repo, en
// %LOCALAPPDATA%\GymForge\vendor\license-private.key; la pública correspondiente
// va embebida en LicenseService.VendorPublicKey (la app solo verifica).
//
// Sin argumentos arranca el asistente interactivo. Ver scripts\licencia.ps1.

var keyDir = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
    "GymForge", "vendor");
var keyPath = Path.Combine(keyDir, "license-private.key");
var registroPath = Path.Combine(keyDir, "licencias-emitidas.csv");
var mensajesDir = Path.Combine(keyDir, "licencias");

// Presets de venta: evitan tener que recordar los límites de cada plan.
var planes = new[]
{
    new Plan("Basico", 1, 300),
    new Plan("Pro", 3, 1000),
    new Plan("Ilimitado", 99, 100_000),
};

switch (args.FirstOrDefault())
{
    case "init-keys": InitKeys(); break;
    case "public-key": WithPrivateKey(PrintPublicKey); break;
    case "new": GenerateLicense(args.Skip(1).ToArray()); break;
    case "show": Show(args.Skip(1).FirstOrDefault()); break;
    case "list": ListarRegistro(); break;
    case "renew": Renovar(); break;
    case null: Asistente(); break;
    default: PrintHelp(); break;
}

return;

// ---------------------------------------------------------------- claves

void InitKeys()
{
    if (File.Exists(keyPath))
    {
        Console.WriteLine($"Ya existe la clave privada en {keyPath} (no se sobreescribe).");
        WithPrivateKey(PrintPublicKey);
        return;
    }

    Directory.CreateDirectory(keyDir);
    using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    File.WriteAllText(keyPath, Convert.ToBase64String(ecdsa.ExportPkcs8PrivateKey()));

    Console.WriteLine($"Clave privada creada: {keyPath}");
    Console.WriteLine("RESPALDALA: sin ella no se pueden emitir más licencias compatibles.");
    PrintPublicKey(ecdsa);
}

void WithPrivateKey(Action<ECDsa> action)
{
    if (!File.Exists(keyPath))
    {
        Console.WriteLine($"No se encontró la clave privada en: {keyPath}");
        Console.WriteLine("Si es la primera vez en esta PC, creala con:");
        Console.WriteLine("  dotnet run --project tools/GymForge.LicenseGen -- init-keys");
        Console.WriteLine("(Ojo: usá una terminal normal, no elevada como administrador,");
        Console.WriteLine(" porque cambia la carpeta del perfil de usuario.)");
        Environment.Exit(1);
    }

    using var ecdsa = ECDsa.Create();
    ecdsa.ImportPkcs8PrivateKey(Convert.FromBase64String(File.ReadAllText(keyPath).Trim()), out _);

    // La privada tiene que corresponder a la pública embebida en la app distribuida;
    // si no, las claves emitidas serían rechazadas por el exe.
    var derivedPublic = Convert.ToBase64String(ecdsa.ExportSubjectPublicKeyInfo());
    if (derivedPublic != LicenseService.VendorPublicKey)
    {
        Console.WriteLine("⚠ ATENCIÓN: esta clave privada NO corresponde a la clave pública");
        Console.WriteLine("  embebida en la app (LicenseService.VendorPublicKey).");
        Console.WriteLine("  Las licencias que emitas acá van a ser RECHAZADAS por el exe");
        Console.WriteLine("  distribuido. Restaurá el backup de la clave privada original,");
        Console.WriteLine("  o re-embebé esta pública y volvé a publicar la app.");
        Console.WriteLine();
    }

    action(ecdsa);
}

static void PrintPublicKey(ECDsa ecdsa)
{
    Console.WriteLine();
    Console.WriteLine("Clave pública (valor de LicenseService.VendorPublicKey):");
    Console.WriteLine(Convert.ToBase64String(ecdsa.ExportSubjectPublicKeyInfo()));
}

// ---------------------------------------------------------------- emisión

void Asistente()
{
    Console.WriteLine("Nueva licencia de GymForge");
    Console.WriteLine();

    var gym = PreguntarTexto("Nombre del gimnasio", obligatorio: true)!;

    Console.WriteLine("Plan:");
    for (var i = 0; i < planes.Length; i++)
        Console.WriteLine($"  {i + 1}) {planes[i].Tier,-10} {planes[i].Describir()}");
    var plan = planes[PreguntarNumero("Elegí", 2, 1, planes.Length) - 1];

    var cuit = PreguntarTexto("CUIT (enter para omitir)") ?? string.Empty;
    var meses = PreguntarNumero("Meses de vigencia", 12, 1, 120);

    Console.WriteLine();
    Emitir(gym, cuit, plan, meses, DateOnly.FromDateTime(DateTime.Today));
}

// Emite, firma, muestra y deja registro. 'desde' permite encadenar renovaciones
// sin regalar ni perder días respecto del vencimiento anterior.
void Emitir(string gym, string cuit, Plan plan, int meses, DateOnly desde)
{
    var hoy = DateOnly.FromDateTime(DateTime.Today);
    var payload = new LicensePayload(
        Guid.NewGuid(), gym.Trim(), cuit.Trim(), plan.Tier,
        plan.Sedes, plan.Socios, hoy, desde.AddMonths(meses));

    WithPrivateKey(ecdsa =>
    {
        var clave = LicenseCodec.Encode(payload, ecdsa);

        Console.WriteLine($"Licencia {payload.Tier} para {payload.Gym}");
        Console.WriteLine($"  {plan.Describir()} · vence {payload.ExpiresOn:dd/MM/yyyy}");
        Console.WriteLine();
        Console.WriteLine(clave);
        Console.WriteLine();

        CopiarAlPortapapeles(clave);
        GuardarEnRegistro(payload, clave);

        var archivo = GuardarMensaje(payload, plan, clave);
        Console.WriteLine($"Mensaje para el cliente: {archivo}");
    });
}

void GenerateLicense(string[] a)
{
    string? Opt(string name)
    {
        var i = Array.IndexOf(a, "--" + name);
        return i >= 0 && i + 1 < a.Length ? a[i + 1] : null;
    }

    // Sin --gym no hay nada que emitir: en vez de fallar, arranca el asistente.
    var gym = Opt("gym");
    if (string.IsNullOrWhiteSpace(gym)) { Asistente(); return; }

    var tier = Opt("tier") ?? "Pro";
    var basePlan = planes.FirstOrDefault(p => p.Tier.Equals(tier, StringComparison.OrdinalIgnoreCase))
                   ?? new Plan(tier, 3, 1000);
    var plan = new Plan(basePlan.Tier,
        int.Parse(Opt("max-sites") ?? basePlan.Sedes.ToString()),
        int.Parse(Opt("max-members") ?? basePlan.Socios.ToString()));

    Emitir(gym, Opt("cuit") ?? string.Empty, plan,
        int.Parse(Opt("months") ?? "12"), DateOnly.FromDateTime(DateTime.Today));
}

// ---------------------------------------------------------------- registro

void GuardarEnRegistro(LicensePayload p, string clave)
{
    Directory.CreateDirectory(keyDir);
    var nuevo = !File.Exists(registroPath);
    using var w = new StreamWriter(registroPath, append: true, Encoding.UTF8);
    if (nuevo) w.WriteLine("Emitida;LicenseId;Gimnasio;Cuit;Plan;Sedes;Socios;Vence;Clave");
    w.WriteLine(string.Join(';',
        DateTime.Now.ToString("yyyy-MM-dd HH:mm"), p.LicenseId, Csv(p.Gym), Csv(p.Cuit),
        p.Tier, p.MaxSites, p.MaxMembers, p.ExpiresOn.ToString("yyyy-MM-dd"), clave));
}

List<Emitida> LeerRegistro()
{
    if (!File.Exists(registroPath)) return [];

    return File.ReadAllLines(registroPath, Encoding.UTF8)
        .Skip(1)
        .Where(l => !string.IsNullOrWhiteSpace(l))
        .Select(ParseCsv)
        .Where(c => c.Length >= 9)
        .Select(c => new Emitida(
            c[2], c[3], c[4],
            int.TryParse(c[5], out var s) ? s : 0,
            int.TryParse(c[6], out var m) ? m : 0,
            DateOnly.TryParse(c[7], out var v) ? v : default,
            c[8]))
        .ToList();
}

void ListarRegistro()
{
    var todas = LeerRegistro();
    if (todas.Count == 0)
    {
        Console.WriteLine("Todavía no emitiste ninguna licencia.");
        Console.WriteLine($"(el registro se guarda en {registroPath})");
        return;
    }

    var hoy = DateOnly.FromDateTime(DateTime.Today);
    Console.WriteLine($"{"Gimnasio",-28} {"Plan",-11} {"Vence",-12} Estado");
    foreach (var e in todas)
        Console.WriteLine($"{Recortar(e.Gym, 28),-28} {e.Tier,-11} {e.Vence:dd/MM/yyyy}   {EstadoDe(e.Vence, hoy)}");

    Console.WriteLine();
    Console.WriteLine($"{todas.Count} licencia(s) · {registroPath}");
}

void Renovar()
{
    // Un cliente puede tener varias licencias emitidas; interesa la última de cada uno.
    var ultimas = LeerRegistro()
        .GroupBy(e => e.Gym, StringComparer.OrdinalIgnoreCase)
        .Select(g => g.Last())
        .OrderBy(e => e.Vence)
        .ToList();

    if (ultimas.Count == 0)
    {
        Console.WriteLine("No hay clientes en el registro todavía. Emití una licencia primero.");
        return;
    }

    var hoy = DateOnly.FromDateTime(DateTime.Today);
    Console.WriteLine("Clientes:");
    for (var i = 0; i < ultimas.Count; i++)
        Console.WriteLine($"  {i + 1}) {Recortar(ultimas[i].Gym, 28),-28} {ultimas[i].Tier,-11} vence {ultimas[i].Vence:dd/MM/yyyy}  {EstadoDe(ultimas[i].Vence, hoy)}");

    var elegido = ultimas[PreguntarNumero("Elegí", 1, 1, ultimas.Count) - 1];
    var meses = PreguntarNumero("Meses a renovar", 12, 1, 120);

    // Si todavía no venció, la renovación arranca donde terminaba la anterior.
    var desde = elegido.Vence > hoy ? elegido.Vence : hoy;
    if (desde != hoy)
        Console.WriteLine($"La licencia vigente vence el {elegido.Vence:dd/MM/yyyy}: se encadena desde esa fecha.");

    Console.WriteLine();
    Emitir(elegido.Gym, elegido.Cuit, new Plan(elegido.Tier, elegido.Sedes, elegido.Socios), meses, desde);
}

// ---------------------------------------------------------------- inspección

void Show(string? clave)
{
    clave ??= PreguntarTexto("Pegá la clave", obligatorio: true);

    // Se resuelve con el mismo servicio que usa la app: lo que se ve acá es
    // exactamente lo que va a ver el cliente en su PC.
    var hoy = DateOnly.FromDateTime(DateTime.Today);
    var estado = new LicenseService().Resolve(clave, hoy);

    Console.WriteLine();
    if (estado.Status == LicenseStatus.Free)
    {
        Console.WriteLine("Clave INVÁLIDA: no se puede verificar la firma.");
        Console.WriteLine("Puede estar cortada al copiarla, o emitida con otra clave privada.");
        return;
    }

    var detalle = estado.Status switch
    {
        LicenseStatus.Active => "vigente",
        LicenseStatus.Grace => $"VENCIDA, en período de gracia ({LicenseService.GraceDays} días). Sigue operativa, hay que renovar.",
        _ => "VENCIDA fuera de gracia: la app quedó con los límites del plan gratuito.",
    };

    Console.WriteLine($"Gimnasio : {estado.GymName}");
    Console.WriteLine($"Plan     : {estado.Tier} · {estado.MaxSites} sede(s) · {estado.MaxMembers} socios");
    Console.WriteLine($"Vence    : {estado.ExpiresOn:dd/MM/yyyy}");
    Console.WriteLine($"Estado   : {detalle}");
}

// ---------------------------------------------------------------- salida

void CopiarAlPortapapeles(string texto)
{
    try
    {
        var psi = new ProcessStartInfo("clip")
        {
            RedirectStandardInput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        using var proc = Process.Start(psi);
        if (proc is null) return;
        proc.StandardInput.Write(texto);
        proc.StandardInput.Close();
        proc.WaitForExit(3000);
        Console.WriteLine("(copiada al portapapeles)");
    }
    catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or PlatformNotSupportedException)
    {
        // Sin clip.exe (fuera de Windows) no es crítico: la clave ya está impresa.
    }
}

string GuardarMensaje(LicensePayload p, Plan plan, string clave)
{
    Directory.CreateDirectory(mensajesDir);
    var archivo = Path.Combine(mensajesDir, $"{Slug(p.Gym)}-{p.ExpiresOn:yyyy-MM-dd}.txt");
    File.WriteAllText(archivo, $"""
        ¡Hola! Te paso la licencia de GymForge para {p.Gym}.

        Plan {p.Tier}: {plan.Describir()}.
        Vigente hasta el {p.ExpiresOn:dd/MM/yyyy}.

        Clave de activación:

        {clave}

        Para activarla: abrí GymForge → Configuración → Licencia, pegá la clave y tocá
        Activar. Queda guardada en la PC, no hay que repetirlo.
        """, Encoding.UTF8);
    return archivo;
}

// ---------------------------------------------------------------- helpers

string? PreguntarTexto(string etiqueta, bool obligatorio = false)
{
    while (true)
    {
        Console.Write($"{etiqueta}: ");
        var valor = Console.ReadLine();
        if (valor is null)
        {
            Console.WriteLine("No hay entrada interactiva disponible.");
            Environment.Exit(1);
        }

        valor = Sanear(valor);
        if (valor.Length > 0) return valor;
        if (!obligatorio) return null;
        Console.WriteLine("  (es obligatorio)");
    }
}

int PreguntarNumero(string etiqueta, int porDefecto, int min, int max)
{
    while (true)
    {
        Console.Write($"{etiqueta} [{porDefecto}]: ");
        var valor = Console.ReadLine();
        if (valor is null)
        {
            Console.WriteLine("No hay entrada interactiva disponible.");
            Environment.Exit(1);
        }

        valor = Sanear(valor);
        if (valor.Length == 0) return porDefecto;
        if (int.TryParse(valor, out var n) && n >= min && n <= max) return n;
        Console.WriteLine($"  (un número entre {min} y {max})");
    }
}

// Al pegar desde Excel/WhatsApp vienen BOM (U+FEFF) y caracteres de control
// invisibles; sin limpiarlos quedarían firmados dentro de la licencia, o harían
// fallar el parseo de un número que en pantalla se ve perfecto.
static string Sanear(string valor) =>
    new string([.. valor.Where(c => c != (char)0xFEFF && !char.IsControl(c))]).Trim();

static string EstadoDe(DateOnly vence, DateOnly hoy)
{
    if (vence >= hoy)
    {
        var dias = vence.DayNumber - hoy.DayNumber;
        return dias <= 30 ? $"vence en {dias} día(s)" : "vigente";
    }

    var vencidaHace = hoy.DayNumber - vence.DayNumber;
    return vencidaHace <= LicenseService.GraceDays ? "EN GRACIA" : "VENCIDA";
}

static string Recortar(string texto, int largo) =>
    texto.Length <= largo ? texto : texto[..(largo - 1)] + "…";

static string Csv(string valor) =>
    valor.Contains(';') || valor.Contains('"')
        ? '"' + valor.Replace("\"", "\"\"") + '"'
        : valor;

static string[] ParseCsv(string linea)
{
    var campos = new List<string>();
    var sb = new StringBuilder();
    var enComillas = false;

    for (var i = 0; i < linea.Length; i++)
    {
        var c = linea[i];
        if (enComillas)
        {
            if (c == '"' && i + 1 < linea.Length && linea[i + 1] == '"') { sb.Append('"'); i++; }
            else if (c == '"') enComillas = false;
            else sb.Append(c);
        }
        else if (c == '"') enComillas = true;
        else if (c == ';') { campos.Add(sb.ToString()); sb.Clear(); }
        else sb.Append(c);
    }

    campos.Add(sb.ToString());
    return [.. campos];
}

static string Slug(string texto)
{
    var limpio = new string([.. texto.Select(c => char.IsLetterOrDigit(c) ? char.ToLowerInvariant(c) : '-')]);
    while (limpio.Contains("--")) limpio = limpio.Replace("--", "-");
    return limpio.Trim('-') is { Length: > 0 } s ? s : "licencia";
}

static void PrintHelp()
{
    Console.WriteLine("""
        GymForge.LicenseGen — emisión de claves de licencia (uso interno del vendedor)

        Sin argumentos arranca el asistente interactivo (lo más cómodo):
          .\scripts\licencia.ps1

        Comandos:
          (ninguno)     Asistente: pregunta gimnasio, plan, CUIT y meses.
          list          Licencias emitidas, con vencimiento y estado.
          renew         Renueva a un cliente del registro (encadena desde su vencimiento).
          show <clave>  Verifica una clave y muestra qué ve la app.
          init-keys     Crea el par de claves (una sola vez) y muestra la pública.
          public-key    Vuelve a mostrar la clave pública.
          new           Emite sin preguntar (para scripts):
                          --gym "Iron Temple SRL"   (sin esto, abre el asistente)
                          --cuit 30-71234567-8
                          --tier Basico|Pro|Ilimitado   (default: Pro)
                          --max-sites 3             (default: según el plan)
                          --max-members 1000        (default: según el plan)
                          --months 12               (default: 12)

        Planes: Basico 1 sede/300 socios · Pro 3 sedes/1000 socios · Ilimitado 99/100000
        """);
}

/// <summary>Preset de venta: los límites que se firman en la licencia.</summary>
sealed record Plan(string Tier, int Sedes, int Socios)
{
    public string Describir() =>
        Sedes >= 99 ? "sedes y socios sin límite práctico" : $"{Sedes} sede(s) · {Socios} socios";
}

/// <summary>Fila del registro de licencias emitidas.</summary>
sealed record Emitida(string Gym, string Cuit, string Tier, int Sedes, int Socios, DateOnly Vence, string Clave);
