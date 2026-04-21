using GymForge.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GymForge.Infrastructure.Persistence.Configurations;

public class CompanyConfiguration : IEntityTypeConfiguration<Company>
{
    public void Configure(EntityTypeBuilder<Company> builder)
    {
        builder.HasKey(c => c.Id);
        builder.Property(c => c.LegalName).HasMaxLength(200).IsRequired();
        builder.Property(c => c.TaxId).HasMaxLength(20).IsRequired();
        builder.Property(c => c.PrimaryLanguage).HasMaxLength(10);
        builder.Property(c => c.Currency).HasMaxLength(5);
        builder.Property(c => c.Timezone).HasMaxLength(50);
        builder.Property(c => c.BrandColorHex).HasMaxLength(10);
        builder.Property(c => c.LogoUrl).HasMaxLength(500);

        builder.HasMany(c => c.Sites)
            .WithOne(s => s.Company)
            .HasForeignKey(s => s.CompanyId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(c => c.TaxId).IsUnique().HasDatabaseName("IX_Companies_TaxId");
        builder.ToTable("Companies");
    }
}

public class SiteConfiguration : IEntityTypeConfiguration<Site>
{
    public void Configure(EntityTypeBuilder<Site> builder)
    {
        builder.HasKey(s => s.Id);
        builder.Property(s => s.Name).HasMaxLength(150).IsRequired();
        builder.Property(s => s.Address).HasMaxLength(300).IsRequired();
        builder.Property(s => s.Phone).HasMaxLength(30);
        builder.Property(s => s.BrandColorHex).HasMaxLength(10);

        builder.HasIndex(s => s.CompanyId).HasDatabaseName("IX_Sites_Company");
        builder.ToTable("Sites");
    }
}

public class StaffConfiguration : IEntityTypeConfiguration<Staff>
{
    public void Configure(EntityTypeBuilder<Staff> builder)
    {
        builder.HasKey(s => s.Id);
        builder.Property(s => s.FirstName).HasMaxLength(100).IsRequired();
        builder.Property(s => s.LastName).HasMaxLength(100).IsRequired();
        builder.Property(s => s.Email).HasMaxLength(254);
        builder.Property(s => s.Mobile).HasMaxLength(30);
        builder.Property(s => s.PinCodeHash).HasMaxLength(200).IsRequired();
        builder.Property(s => s.ColorHex).HasMaxLength(10);
        builder.Property(s => s.AvatarUrl).HasMaxLength(500);
        builder.Property(s => s.Role).HasConversion<string>().HasMaxLength(20);
        builder.Property(s => s.CommissionPctMemberships).HasColumnType("DECIMAL(5,2)");
        builder.Property(s => s.CommissionPctPt).HasColumnType("DECIMAL(5,2)");
        builder.Property(s => s.CommissionPctProducts).HasColumnType("DECIMAL(5,2)");

        builder.HasOne(s => s.Company)
            .WithMany(c => c.Staff)
            .HasForeignKey(s => s.CompanyId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(s => s.Email).HasDatabaseName("IX_Staff_Email");
        builder.HasIndex(s => new { s.CompanyId, s.Role }).HasDatabaseName("IX_Staff_CompanyRole");
        builder.ToTable("Staff");
    }
}
