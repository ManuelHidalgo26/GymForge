using GymForge.Application.DTOs;

namespace GymForge.Application.Interfaces;

/// <summary>Renderiza un recibo de pago como documento PDF.</summary>
public interface IReceiptPdfWriter
{
    byte[] Generate(ReceiptDto receipt);
}
