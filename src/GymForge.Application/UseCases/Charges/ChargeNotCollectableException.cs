namespace GymForge.Application.UseCases.Charges;

/// <summary>
/// Se pidió cobrar un cargo que ya no admite cobro (saldado, dado de baja o reembolsado). Es una regla de
/// negocio: el mensaje está redactado para el cajero y la UI puede mostrarlo tal cual.
/// </summary>
public sealed class ChargeNotCollectableException(string message) : InvalidOperationException(message);
