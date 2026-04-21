namespace GymForge.Domain.ValueObjects;

public sealed record PhoneNumber
{
    public string Value { get; }

    public PhoneNumber(string value)
    {
        var normalized = value.Replace(" ", "").Replace("-", "").Replace("(", "").Replace(")", "");
        if (string.IsNullOrWhiteSpace(normalized))
            throw new ArgumentException("Phone number cannot be empty.");
        Value = normalized;
    }

    public static implicit operator string(PhoneNumber p) => p.Value;
    public override string ToString() => Value;
}
