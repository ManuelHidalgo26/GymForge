using System.Globalization;
using System.Text;
using GymForge.Domain.Enums;

namespace GymForge.Application.UseCases.Members;

/// <summary>Una fila de socio ya parseada y normalizada, lista para importar.</summary>
public sealed record ImportMemberRow(
    string FirstName,
    string LastName,
    string DocumentNumber,
    string? Email,
    string? Mobile,
    DateOnly? BirthDate,
    Gender Gender = Gender.PreferNotToSay);

public sealed record CsvParseResult(
    IReadOnlyList<ImportMemberRow> Rows,
    IReadOnlyList<string> Errors);

/// <summary>
/// Parser tolerante para migrar el padrón existente desde un CSV/Excel exportado.
/// Reconoce las columnas por nombre (en cualquier orden, con o sin acentos) y
/// autodetecta el separador (coma o punto y coma, como exporta Excel en es-AR).
/// Columnas requeridas: nombre, apellido, dni. Opcionales: email, telefono,
/// nacimiento, genero. Las filas inválidas se reportan y se omiten, no abortan todo.
/// </summary>
public static class MemberCsvParser
{
    private static readonly string[] FirstNameKeys = { "nombre", "nombres", "firstname", "first" };
    private static readonly string[] LastNameKeys = { "apellido", "apellidos", "lastname", "last" };
    private static readonly string[] DocumentKeys = { "dni", "documento", "doc", "nrodocumento", "nrodoc" };
    private static readonly string[] EmailKeys = { "email", "mail", "correo", "correoelectronico" };
    private static readonly string[] MobileKeys = { "telefono", "celular", "movil", "tel", "telefonomovil", "whatsapp" };
    private static readonly string[] BirthKeys = { "nacimiento", "fechanacimiento", "fechadenacimiento", "nac", "fechanac" };
    private static readonly string[] GenderKeys = { "genero", "sexo", "gender" };

    public static CsvParseResult Parse(string? csv)
    {
        var rows = new List<ImportMemberRow>();
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(csv))
            return new CsvParseResult(rows, new[] { "El archivo está vacío." });

        var lines = csv.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');

        int headerIndex = Array.FindIndex(lines, l => !string.IsNullOrWhiteSpace(l));
        if (headerIndex < 0)
            return new CsvParseResult(rows, new[] { "El archivo está vacío." });

        char delimiter = lines[headerIndex].Contains(';') ? ';' : ',';
        var header = SplitLine(lines[headerIndex], delimiter).Select(Normalize).ToList();

        int iFirst = IndexOfAny(header, FirstNameKeys);
        int iLast = IndexOfAny(header, LastNameKeys);
        int iDoc = IndexOfAny(header, DocumentKeys);
        int iEmail = IndexOfAny(header, EmailKeys);
        int iMobile = IndexOfAny(header, MobileKeys);
        int iBirth = IndexOfAny(header, BirthKeys);
        int iGender = IndexOfAny(header, GenderKeys);

        if (iFirst < 0 || iLast < 0 || iDoc < 0)
            return new CsvParseResult(rows, new[]
            {
                "El encabezado debe incluir al menos las columnas: nombre, apellido y dni."
            });

        for (int i = headerIndex + 1; i < lines.Length; i++)
        {
            if (string.IsNullOrWhiteSpace(lines[i])) continue;

            var cols = SplitLine(lines[i], delimiter);
            string Get(int idx) => idx >= 0 && idx < cols.Count ? cols[idx].Trim() : string.Empty;

            var first = Get(iFirst);
            var last = Get(iLast);
            var doc = Get(iDoc);
            int lineNo = i + 1;

            if (first.Length == 0 || last.Length == 0 || doc.Length == 0)
            {
                errors.Add($"Línea {lineNo}: faltan nombre, apellido o DNI — se omite.");
                continue;
            }

            DateOnly? birth = null;
            var birthRaw = Get(iBirth);
            if (birthRaw.Length > 0)
            {
                if (TryParseDate(birthRaw, out var d)) birth = d;
                else errors.Add($"Línea {lineNo}: fecha de nacimiento inválida ('{birthRaw}') — se importa sin fecha.");
            }

            rows.Add(new ImportMemberRow(
                first, last, doc,
                NullIfEmpty(Get(iEmail)),
                NullIfEmpty(Get(iMobile)),
                birth,
                ParseGender(Get(iGender))));
        }

        return new CsvParseResult(rows, errors);
    }

    private static string? NullIfEmpty(string s) => string.IsNullOrWhiteSpace(s) ? null : s;

    private static Gender ParseGender(string raw) => Normalize(raw) switch
    {
        "m" or "masculino" or "male" or "varon" or "hombre" => Gender.Male,
        "f" or "femenino" or "female" or "mujer" => Gender.Female,
        "x" or "nobinario" or "nonbinary" => Gender.NonBinary,
        _ => Gender.PreferNotToSay,
    };

    private static bool TryParseDate(string raw, out DateOnly date)
    {
        string[] formats = { "dd/MM/yyyy", "d/M/yyyy", "yyyy-MM-dd", "dd-MM-yyyy", "MM/dd/yyyy" };
        var trimmed = raw.Trim();
        return DateOnly.TryParseExact(trimmed, formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out date)
            || DateOnly.TryParse(trimmed, new CultureInfo("es-AR"), DateTimeStyles.None, out date);
    }

    private static int IndexOfAny(List<string> header, string[] keys)
    {
        for (int i = 0; i < header.Count; i++)
            if (keys.Contains(header[i])) return i;
        return -1;
    }

    /// <summary>Minúsculas, sin espacios y sin acentos, para comparar encabezados.</summary>
    private static string Normalize(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return string.Empty;
        var trimmed = s.Trim().ToLowerInvariant().Replace(" ", string.Empty);
        var decomposed = trimmed.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(decomposed.Length);
        foreach (var ch in decomposed)
            if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark)
                sb.Append(ch);
        return sb.ToString().Normalize(NormalizationForm.FormC);
    }

    /// <summary>Split que respeta comillas dobles: "Pérez, Juan" queda como un solo campo.</summary>
    private static List<string> SplitLine(string line, char delimiter)
    {
        var fields = new List<string>();
        var sb = new StringBuilder();
        bool inQuotes = false;

        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < line.Length && line[i + 1] == '"') { sb.Append('"'); i++; }
                    else inQuotes = false;
                }
                else sb.Append(c);
            }
            else if (c == '"') inQuotes = true;
            else if (c == delimiter) { fields.Add(sb.ToString()); sb.Clear(); }
            else sb.Append(c);
        }

        fields.Add(sb.ToString());
        return fields;
    }
}
