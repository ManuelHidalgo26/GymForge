using FluentAvalonia.UI.Controls;

namespace GymForge.Desktop.Views.Shell;

/// <summary>Un comando de la paleta (Ctrl+K): título visible, grupo ("Ir a" / "Acciones"), ícono y qué ejecuta.</summary>
public sealed record PaletteCommand(string Title, string Group, Symbol Icon, Action Run);
