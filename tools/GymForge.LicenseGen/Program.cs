using System.Security.Cryptography;
using GymForge.Application.UseCases.Licensing;

// Herramienta del VENDEDOR (no se distribuye con la app): emite claves de
// licencia firmadas con ECDSA P-256. La clave privada vive FUERA del repo, en
// %LOCALAPPDATA%\GymForge\vendor\license-private.key; la pública correspondiente
// va embebida en LicenseService.VendorPublicKey (la app solo verifica).

var keyDir = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
    "GymForge", "vendor");
var keyPath = Path.Combine(keyDir, "license-private.key");

switch (args.FirstOrDefault())
{
    case "init-keys": InitKeys(); break;
    case "public-key": WithPrivateKey(PrintPublicKey); break;
    case "new": GenerateLicense(args.Skip(1).ToArray()); break;
    default: PrintHelp(); break;
}

return;

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
        Console.WriteLine("No hay clave privada todavía. Corré primero: ... init-keys");
        Environment.Exit(1);
    }

    using var ecdsa = ECDsa.Create();
    ecdsa.ImportPkcs8PrivateKey(Convert.FromBase64String(File.ReadAllText(keyPath).Trim()), out _);
    action(ecdsa);
}

static void PrintPublicKey(ECDsa ecdsa)
{
    Console.WriteLine();
    Console.WriteLine("Clave pública (valor de LicenseService.VendorPublicKey):");
    Console.WriteLine(Convert.ToBase64String(ecdsa.ExportSubjectPublicKeyInfo()));
}

void GenerateLicense(string[] a)
{
    string? Opt(string name)
    {
        var i = Array.IndexOf(a, "--" + name);
        return i >= 0 && i + 1 < a.Length ? a[i + 1] : null;
    }

    var gym = Opt("gym");
    if (string.IsNullOrWhiteSpace(gym))
    {
        Console.WriteLine("Falta --gym \"Nombre del gimnasio\".");
        Environment.Exit(1);
        return;
    }

    var cuit = Opt("cuit") ?? string.Empty;
    var tier = Opt("tier") ?? "Pro";
    var maxSites = int.Parse(Opt("max-sites") ?? "3");
    var maxMembers = int.Parse(Opt("max-members") ?? "1000");
    var months = int.Parse(Opt("months") ?? "12");

    var today = DateOnly.FromDateTime(DateTime.Today);
    var payload = new LicensePayload(
        Guid.NewGuid(), gym.Trim(), cuit.Trim(), tier.Trim(),
        maxSites, maxMembers, today, today.AddMonths(months));

    WithPrivateKey(ecdsa =>
    {
        Console.WriteLine($"Licencia {payload.Tier} para {payload.Gym}");
        Console.WriteLine($"  Sedes: {payload.MaxSites} · Socios: {payload.MaxMembers} · Vence: {payload.ExpiresOn:dd/MM/yyyy}");
        Console.WriteLine();
        Console.WriteLine(LicenseCodec.Encode(payload, ecdsa));
    });
}

static void PrintHelp()
{
    Console.WriteLine("""
        GymForge.LicenseGen — emisión de claves de licencia (uso interno del vendedor)

        Comandos:
          init-keys     Crea el par de claves (una sola vez) y muestra la pública.
          public-key    Vuelve a mostrar la clave pública.
          new           Emite una licencia:
                          --gym "Iron Temple SRL"   (obligatorio)
                          --cuit 30-71234567-8
                          --tier Pro                (default: Pro)
                          --max-sites 3             (default: 3)
                          --max-members 1000        (default: 1000)
                          --months 12               (default: 12)

        Ejemplo:
          dotnet run --project tools/GymForge.LicenseGen -- new --gym "Iron Temple" --months 12
        """);
}
