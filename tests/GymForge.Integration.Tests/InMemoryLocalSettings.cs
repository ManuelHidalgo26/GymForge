using GymForge.Application.Interfaces;

namespace GymForge.Integration.Tests;

/// <summary>Settings locales en memoria: los tests no deben leer ni escribir el archivo real del usuario.</summary>
internal sealed class InMemoryLocalSettings : ILocalSettings
{
    public string? OffsiteBackupDir { get; set; }

    public DateTime? LastOffsiteBackupUtc { get; set; }

    public string? UpdateFeedUrl { get; set; }

    public int SaveCount { get; private set; }

    public void Save() => SaveCount++;
}
