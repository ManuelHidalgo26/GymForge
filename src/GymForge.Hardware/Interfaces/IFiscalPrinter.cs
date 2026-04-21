namespace GymForge.Hardware.Interfaces;

public record FiscalItem(string Description, decimal Quantity, decimal UnitPrice, decimal TaxRate);
public record FiscalPayment(string Method, decimal Amount, string? CardLast4 = null);

public record FiscalTicketRequest(
    IReadOnlyList<FiscalItem> Items,
    FiscalPayment Payment,
    string? ClientCuit = null);

public record FiscalTicketResult(
    string Cae,
    DateTime CaeExpiry,
    string InvoiceNumber,
    byte[] PdfBytes,
    string XmlContent);

public record FiscalStatus(
    string Model,
    string Firmware,
    DateTime? LastZReport,
    bool PaperOk,
    bool IsOnline);

public interface IFiscalPrinter
{
    Task<FiscalTicketResult> PrintTicketAsync(FiscalTicketRequest request, CancellationToken ct = default);
    Task<FiscalTicketResult> PrintCreditNoteAsync(string originalInvoiceNumber, CancellationToken ct = default);
    Task<FiscalStatus> GetStatusAsync(CancellationToken ct = default);
    Task PrintZReportAsync(CancellationToken ct = default);
}
