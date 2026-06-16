using GymForge.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GymForge.Infrastructure.Persistence.Configurations;

public class ChargeConfiguration : IEntityTypeConfiguration<Charge>
{
    public void Configure(EntityTypeBuilder<Charge> builder)
    {
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Description).HasMaxLength(300).IsRequired();
        builder.Property(c => c.Amount).HasColumnType("DECIMAL(18,2)");
        builder.Property(c => c.TaxAmount).HasColumnType("DECIMAL(18,2)");
        builder.Property(c => c.AmountPaid).HasColumnType("DECIMAL(18,2)");
        builder.Property(c => c.ConceptType).HasConversion<string>().HasMaxLength(30);
        builder.Property(c => c.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(c => c.InvoiceNumber).HasMaxLength(20);
        builder.Property(c => c.FiscalCae).HasMaxLength(20);
        builder.Property(c => c.FiscalXmlUrl).HasMaxLength(500);

        builder.Ignore(c => c.TotalAmount);
        builder.Ignore(c => c.AmountOutstanding);

        builder.HasIndex(c => new { c.MemberId, c.Status }).HasDatabaseName("IX_Charges_MemberStatus");
        builder.HasIndex(c => new { c.CompanyId, c.DueDate, c.Status }).HasDatabaseName("IX_Charges_DueDateStatus");

        builder.HasOne(c => c.Member)
            .WithMany(m => m.Charges)
            .HasForeignKey(c => c.MemberId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(c => c.Membership)
            .WithMany()
            .HasForeignKey(c => c.MembershipId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasMany(c => c.Allocations)
            .WithOne(a => a.Charge)
            .HasForeignKey(a => a.ChargeId);

        builder.ToTable("Charges");
    }
}

public class PaymentConfiguration : IEntityTypeConfiguration<Payment>
{
    public void Configure(EntityTypeBuilder<Payment> builder)
    {
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Amount).HasColumnType("DECIMAL(18,2)");
        builder.Property(p => p.Method).HasConversion<string>().HasMaxLength(20);
        builder.Property(p => p.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(p => p.CardLast4).HasMaxLength(4);
        builder.Property(p => p.CardBrand).HasMaxLength(20);
        builder.Property(p => p.ProcessorTxId).HasMaxLength(100);
        builder.Property(p => p.Processor).HasMaxLength(50);

        builder.HasIndex(p => p.MemberId).HasDatabaseName("IX_Payments_Member");
        builder.HasIndex(p => p.ShiftId).HasDatabaseName("IX_Payments_Shift");
        builder.HasIndex(p => p.SaleId).HasDatabaseName("IX_Payments_Sale");

        // Socio opcional: una venta a consumidor final genera un pago sin socio.
        builder.HasOne(p => p.Member)
            .WithMany()
            .HasForeignKey(p => p.MemberId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.Restrict);

        // Venta asociada (POS), sin navegación inversa desde Sale.
        builder.HasOne<Sale>()
            .WithMany()
            .HasForeignKey(p => p.SaleId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasMany(p => p.Allocations)
            .WithOne(a => a.Payment)
            .HasForeignKey(a => a.PaymentId);

        builder.ToTable("Payments");
    }
}

public class PaymentAllocationConfiguration : IEntityTypeConfiguration<PaymentAllocation>
{
    public void Configure(EntityTypeBuilder<PaymentAllocation> builder)
    {
        builder.HasKey(a => a.Id);
        builder.Property(a => a.Amount).HasColumnType("DECIMAL(18,2)");
        builder.HasIndex(a => a.PaymentId).HasDatabaseName("IX_Allocations_Payment");
        builder.HasIndex(a => a.ChargeId).HasDatabaseName("IX_Allocations_Charge");
        builder.ToTable("PaymentAllocations");
    }
}
