using FluentAvalonia.UI.Controls;

namespace GymForge.Desktop.Views.Shell;

/// <summary>
/// Fuente de comandos de la paleta: una entrada por sección del sidebar (mismos títulos e íconos) más las
/// acciones rápidas que ya existen. No inventa acciones nuevas: solo navega y dispara lo que la app ya hace.
/// </summary>
public static class PaletteCatalog
{
    public const string NavigationGroup = "Ir a";
    public const string ActionsGroup = "Acciones";

    public static IReadOnlyList<PaletteCommand> Build(
        Action<NavSection> navigate, Action createMember, Action toggleTheme) =>
    [
        Section("Dashboard", Symbol.Home, NavSection.Dashboard, navigate),
        Section("Socios", Symbol.People, NavSection.Members, navigate),
        Section("Accesos", Symbol.Permissions, NavSection.Access, navigate),
        Section("Caja", Symbol.Calculator, NavSection.Cash, navigate),
        Section("Morosos", Symbol.Important, NavSection.Collections, navigate),
        Section("Planes", Symbol.List, NavSection.Plans, navigate),
        Section("Clases", Symbol.Calendar, NavSection.Classes, navigate),
        Section("Rutinas", Symbol.Target, NavSection.Routines, navigate),
        Section("Productos", Symbol.Shop, NavSection.Products, navigate),
        Section("Reportes", Symbol.Document, NavSection.Reports, navigate),
        Section("Configuración", Symbol.Settings, NavSection.Settings, navigate),
        new("Nuevo socio", ActionsGroup, Symbol.AddFriend, createMember),
        new("Registrar check-in", ActionsGroup, Symbol.Permissions, () => navigate(NavSection.Access)),
        new("Cobrar", ActionsGroup, Symbol.Calculator, () => navigate(NavSection.Cash)),
        new("Cambiar tema", ActionsGroup, Symbol.DarkTheme, toggleTheme)
    ];

    private static PaletteCommand Section(string title, Symbol icon, NavSection section, Action<NavSection> navigate) =>
        new(title, NavigationGroup, icon, () => navigate(section));
}
