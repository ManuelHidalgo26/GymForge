using GymForge.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GymForge.Infrastructure.Persistence.Configurations;

public class ProductConfiguration : IEntityTypeConfiguration<Product>
{
    public void Configure(EntityTypeBuilder<Product> builder)
    {
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Sku).HasMaxLength(50).IsRequired();
        builder.Property(p => p.Barcode).HasMaxLength(50);
        builder.Property(p => p.Name).HasMaxLength(200).IsRequired();
        builder.Property(p => p.SalePrice).HasColumnType("DECIMAL(18,2)");
        builder.Property(p => p.CostPrice).HasColumnType("DECIMAL(18,2)");
        builder.Property(p => p.TaxRate).HasColumnType("DECIMAL(5,4)");
        builder.Property(p => p.Unit).HasMaxLength(20);

        builder.HasMany(p => p.Stock)
            .WithOne(s => s.Product)
            .HasForeignKey(s => s.ProductId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(p => new { p.CompanyId, p.Sku }).IsUnique().HasDatabaseName("IX_Products_CompanySku");
        builder.HasIndex(p => p.Barcode).HasDatabaseName("IX_Products_Barcode");
        builder.ToTable("Products");
    }
}

public class StockBySiteConfiguration : IEntityTypeConfiguration<StockBySite>
{
    public void Configure(EntityTypeBuilder<StockBySite> builder)
    {
        builder.HasKey(s => s.Id);
        builder.Property(s => s.Qty).HasColumnType("DECIMAL(12,3)");
        builder.Property(s => s.ReorderPoint).HasColumnType("DECIMAL(12,3)");

        builder.HasIndex(s => new { s.ProductId, s.SiteId }).IsUnique().HasDatabaseName("IX_StockBySite_ProductSite");
        builder.ToTable("StockBySite");
    }
}

public class SaleConfiguration : IEntityTypeConfiguration<Sale>
{
    public void Configure(EntityTypeBuilder<Sale> builder)
    {
        builder.HasKey(s => s.Id);
        builder.Property(s => s.Subtotal).HasColumnType("DECIMAL(18,2)");
        builder.Property(s => s.DiscountTotal).HasColumnType("DECIMAL(18,2)");
        builder.Property(s => s.TaxTotal).HasColumnType("DECIMAL(18,2)");
        builder.Property(s => s.Total).HasColumnType("DECIMAL(18,2)");
        builder.Property(s => s.InvoiceNumber).HasMaxLength(20);
        builder.Property(s => s.FiscalCae).HasMaxLength(20);
        builder.Property(s => s.PaymentStatus).HasMaxLength(20);

        builder.HasMany(s => s.Lines)
            .WithOne(l => l.Sale)
            .HasForeignKey(l => l.SaleId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(s => new { s.SiteId, s.SaleDatetime }).HasDatabaseName("IX_Sales_SiteDate");
        builder.HasIndex(s => s.MemberId).HasDatabaseName("IX_Sales_Member");
        builder.ToTable("Sales");
    }
}

public class SaleLineConfiguration : IEntityTypeConfiguration<SaleLine>
{
    public void Configure(EntityTypeBuilder<SaleLine> builder)
    {
        builder.HasKey(l => l.Id);
        builder.Property(l => l.Description).HasMaxLength(300).IsRequired();
        builder.Property(l => l.Quantity).HasColumnType("DECIMAL(12,3)");
        builder.Property(l => l.UnitPrice).HasColumnType("DECIMAL(18,2)");
        builder.Property(l => l.Discount).HasColumnType("DECIMAL(18,2)");
        builder.Property(l => l.TaxRate).HasColumnType("DECIMAL(5,4)");

        builder.Ignore(l => l.LineTotal);
        builder.ToTable("SaleLines");
    }
}

public class ShiftConfiguration : IEntityTypeConfiguration<Shift>
{
    public void Configure(EntityTypeBuilder<Shift> builder)
    {
        builder.HasKey(s => s.Id);
        builder.Property(s => s.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(s => s.OpeningCash).HasColumnType("DECIMAL(18,2)");
        builder.Property(s => s.ClosingCashDeclared).HasColumnType("DECIMAL(18,2)");
        builder.Property(s => s.ClosingCashSystem).HasColumnType("DECIMAL(18,2)");
        builder.Property(s => s.Notes).HasMaxLength(1000);

        builder.Ignore(s => s.Difference);
        builder.Ignore(s => s.CashIn);
        builder.Ignore(s => s.CashOut);
        builder.Ignore(s => s.ExpectedCash);

        builder.HasMany(s => s.Movements)
            .WithOne(m => m.Shift)
            .HasForeignKey(m => m.ShiftId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(s => new { s.SiteId, s.Status }).HasDatabaseName("IX_Shifts_SiteStatus");
        builder.ToTable("Shifts");
    }
}

public class CashMovementConfiguration : IEntityTypeConfiguration<CashMovement>
{
    public void Configure(EntityTypeBuilder<CashMovement> builder)
    {
        builder.HasKey(m => m.Id);
        builder.Property(m => m.Type).HasConversion<string>().HasMaxLength(20);
        builder.Property(m => m.Category).HasConversion<string>().HasMaxLength(30);
        builder.Property(m => m.Amount).HasColumnType("DECIMAL(18,2)");
        builder.Property(m => m.Notes).HasMaxLength(500);

        builder.HasIndex(m => m.ShiftId).HasDatabaseName("IX_CashMovements_Shift");
        builder.ToTable("CashMovements");
    }
}
