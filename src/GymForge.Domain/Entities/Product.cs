namespace GymForge.Domain.Entities;

public class Product : BaseEntity
{
    public Guid CompanyId { get; private set; }
    public string Sku { get; private set; } = string.Empty;
    public string? Barcode { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string? Description { get; private set; }
    public Guid? CategoryId { get; private set; }
    public string? Brand { get; private set; }
    public string Unit { get; private set; } = "Unit";
    public decimal CostPrice { get; private set; }
    public decimal SalePrice { get; private set; }
    public decimal TaxRate { get; private set; } = 0.21m;
    public string? ImageUrl { get; private set; }
    public bool IsActive { get; private set; } = true;
    public bool IsSellableOnline { get; private set; }
    public decimal CommissionStaffPct { get; private set; }

    public ICollection<StockBySite> Stock { get; private set; } = [];

    private Product() { }

    public static Product Create(Guid companyId, string sku, string name, decimal salePrice, decimal costPrice = 0) =>
        new()
        {
            CompanyId = companyId,
            Sku = sku,
            Name = name,
            SalePrice = salePrice,
            CostPrice = costPrice
        };
}

public class StockBySite : BaseEntity
{
    public Guid ProductId { get; private set; }
    public Guid SiteId { get; private set; }
    public Guid CompanyId { get; private set; }
    public decimal Qty { get; private set; }
    public decimal ReorderPoint { get; private set; }
    public Guid? SupplierId { get; private set; }

    public Product Product { get; private set; } = null!;

    private StockBySite() { }

    public static StockBySite Create(Guid companyId, Guid productId, Guid siteId, decimal qty = 0) =>
        new() { CompanyId = companyId, ProductId = productId, SiteId = siteId, Qty = qty };

    public void AdjustStock(decimal delta)
    {
        Qty += delta;
        if (Qty < 0) throw new InvalidOperationException("Stock cannot go negative.");
        UpdatedAt = DateTime.UtcNow;
    }
}

public class Sale : BaseEntity
{
    public Guid? MemberId { get; private set; }
    public Guid CashierId { get; private set; }
    public Guid SiteId { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid? RegisterId { get; private set; }
    public Guid? ShiftId { get; private set; }
    public DateTime SaleDatetime { get; private set; } = DateTime.UtcNow;
    public decimal Subtotal { get; private set; }
    public decimal DiscountTotal { get; private set; }
    public decimal TaxTotal { get; private set; }
    public decimal Total { get; private set; }
    public string PaymentStatus { get; private set; } = "Paid";
    public string? InvoiceNumber { get; private set; }
    public string? FiscalCae { get; private set; }
    public string? FiscalXmlUrl { get; private set; }

    public ICollection<SaleLine> Lines { get; private set; } = [];

    private Sale() { }

    public static Sale Create(Guid companyId, Guid siteId, Guid cashierId, Guid? memberId = null, Guid? shiftId = null) =>
        new()
        {
            CompanyId = companyId,
            SiteId = siteId,
            CashierId = cashierId,
            MemberId = memberId,
            ShiftId = shiftId
        };

    public void RecalculateTotals()
    {
        Subtotal = Lines.Sum(l => l.LineTotal);
        DiscountTotal = Lines.Sum(l => l.Discount);
        TaxTotal = Lines.Sum(l => l.LineTotal * (l.TaxRate));
        Total = Subtotal + TaxTotal;
        UpdatedAt = DateTime.UtcNow;
    }
}

public class SaleLine : BaseEntity
{
    public Guid SaleId { get; private set; }
    public Guid? ProductId { get; private set; }
    public Guid? MembershipTypeId { get; private set; }
    public string Description { get; private set; } = string.Empty;
    public decimal Quantity { get; private set; }
    public decimal UnitPrice { get; private set; }
    public decimal Discount { get; private set; }
    public decimal TaxRate { get; private set; } = 0.21m;
    public decimal LineTotal => (UnitPrice * Quantity) - Discount;
    public Guid? CommissionStaffId { get; private set; }

    public Sale Sale { get; private set; } = null!;

    private SaleLine() { }

    public static SaleLine Create(Guid saleId, string description, decimal quantity, decimal unitPrice, decimal taxRate = 0.21m) =>
        new() { SaleId = saleId, Description = description, Quantity = quantity, UnitPrice = unitPrice, TaxRate = taxRate };
}
