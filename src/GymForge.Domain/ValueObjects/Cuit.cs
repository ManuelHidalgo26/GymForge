namespace GymForge.Domain.ValueObjects;

/// <summary>CUIT/CUIL argentino con validación de dígito verificador.</summary>
public sealed record Cuit
{
    public string Value { get; }

    public Cuit(string value)
    {
        var digits = value.Replace("-", "").Trim();
        if (digits.Length != 11 || !digits.All(char.IsDigit))
            throw new ArgumentException($"CUIT inválido: {value}");

        if (!IsValid(digits))
            throw new ArgumentException($"CUIT con dígito verificador incorrecto: {value}");

        Value = $"{digits[..2]}-{digits[2..10]}-{digits[10]}";
    }

    private static bool IsValid(string digits)
    {
        int[] weights = [5, 4, 3, 2, 7, 6, 5, 4, 3, 2];
        int sum = 0;
        for (int i = 0; i < 10; i++)
            sum += (digits[i] - '0') * weights[i];

        int remainder = sum % 11;
        int verifier = remainder == 0 ? 0 : remainder == 1 ? 9 : 11 - remainder;
        return verifier == (digits[10] - '0');
    }

    public static implicit operator string(Cuit c) => c.Value;
    public override string ToString() => Value;
}
