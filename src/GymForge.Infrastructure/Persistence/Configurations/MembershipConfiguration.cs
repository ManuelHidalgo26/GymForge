using GymForge.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GymForge.Infrastructure.Persistence.Configurations;

public class MembershipConfiguration : IEntityTypeConfiguration<Membership>
{
    public void Configure(EntityTypeBuilder<Membership> builder)
    {
        builder.HasKey(m => m.Id);

        builder.Property(m => m.Status).HasConversion<string>().HasMaxLength(30);
        builder.Property(m => m.FreezeReason).HasMaxLength(500);
        builder.Property(m => m.CancelReason).HasMaxLength(500);
        builder.Property(m => m.ContractPdfUrl).HasMaxLength(500);

        builder.HasIndex(m => new { m.MemberId, m.Status }).HasDatabaseName("IX_Memberships_MemberStatus");
        builder.HasIndex(m => new { m.CompanyId, m.EndDate }).HasDatabaseName("IX_Memberships_CompanyEndDate");

        builder.HasOne(m => m.Member)
            .WithMany(mem => mem.Memberships)
            .HasForeignKey(m => m.MemberId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(m => m.MembershipType)
            .WithMany()
            .HasForeignKey(m => m.MembershipTypeId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.ToTable("Memberships");
    }
}

public class MembershipTypeConfiguration : IEntityTypeConfiguration<MembershipType>
{
    public void Configure(EntityTypeBuilder<MembershipType> builder)
    {
        builder.HasKey(m => m.Id);
        builder.Property(m => m.Name).HasMaxLength(100).IsRequired();
        builder.Property(m => m.Basis).HasConversion<string>().HasMaxLength(20);
        builder.Property(m => m.Price).HasColumnType("DECIMAL(18,2)");
        builder.Property(m => m.SignupFee).HasColumnType("DECIMAL(18,2)");
        builder.Property(m => m.GenderRestriction).HasConversion<string>().HasMaxLength(20);
        builder.Property(m => m.ColorHex).HasMaxLength(10);

        builder.HasIndex(m => new { m.CompanyId, m.IsActive }).HasDatabaseName("IX_MembershipTypes_CompanyActive");
        builder.ToTable("MembershipTypes");
    }
}
