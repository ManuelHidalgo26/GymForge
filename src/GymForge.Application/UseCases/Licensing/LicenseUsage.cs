namespace GymForge.Application.UseCases.Licensing;

/// <summary>
/// Una línea de uso de la licencia ("Sedes: 1 de 3"). Si lo usado supera el tope del plan
/// lo dice explícitamente en vez de mostrar un "2 de 1" que parece un error.
/// </summary>
public sealed record LicenseUsageLine(string Label, int Used, int Max)
{
    public bool OverLimit => Used > Max;

    public string Display => OverLimit
        ? $"{Label}: {Used} de {Max} (excede el plan)"
        : $"{Label}: {Used} de {Max}";
}

/// <summary>
/// Arma las líneas de uso de socios y sedes contra los topes del plan vigente. El exceso solo se
/// visibiliza: no se tocan sedes ni socios preexistentes (el alta nueva ya está bloqueada).
/// </summary>
public static class LicenseUsage
{
    public static LicenseUsageLine Members(int used, LicenseState state) =>
        new("Socios", used, state.MaxMembers);

    public static LicenseUsageLine Sites(int used, LicenseState state) =>
        new("Sedes", used, state.MaxSites);
}
