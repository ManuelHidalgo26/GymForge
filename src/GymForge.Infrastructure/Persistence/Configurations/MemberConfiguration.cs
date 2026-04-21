using GymForge.Domain.Entities;
using GymForge.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GymForge.Infrastructure.Persistence.Configurations;

public class MemberConfiguration : IEntityTypeConfiguration<Member>
{
    public void Configure(EntityTypeBuilder<Member> builder)
    {
        builder.HasKey(m => m.Id);

        builder.Property(m => m.FirstName).HasMaxLength(100).IsRequired();
        builder.Property(m => m.LastName).HasMaxLength(100).IsRequired();
        builder.Property(m => m.DocumentNumber).HasMaxLength(20).IsRequired();
        builder.Property(m => m.Email).HasMaxLength(254);
        builder.Property(m => m.Mobile).HasMaxLength(30);
        builder.Property(m => m.TagSerial).HasMaxLength(50);
        builder.Property(m => m.PhotoUrl).HasMaxLength(500);
        builder.Property(m => m.SignatureUrl).HasMaxLength(500);

        builder.Property(m => m.DocumentType)
            .HasConversion<string>()
            .HasMaxLength(10);

        builder.Property(m => m.Gender)
            .HasConversion<string>()
            .HasMaxLength(20);

        builder.Property(m => m.Status)
            .HasConversion<string>()
            .HasMaxLength(20);

        builder.Property(m => m.BloodType)
            .HasConversion<string>()
            .HasMaxLength(10);

        builder.Property(m => m.Source)
            .HasConversion<string>()
            .HasMaxLength(30);

        builder.Property(m => m.FingerprintTemplate)
            .HasColumnType("BLOB");

        // FTS5 virtual table is created via raw SQL in migration
        builder.HasIndex(m => m.DocumentNumber).HasDatabaseName("IX_Members_Document");
        builder.HasIndex(m => m.Email).HasDatabaseName("IX_Members_Email");
        builder.HasIndex(m => new { m.CompanyId, m.SiteId, m.Status }).HasDatabaseName("IX_Members_CompanySiteStatus");
        builder.HasIndex(m => m.TagSerial).HasDatabaseName("IX_Members_TagSerial");

        builder.HasOne(m => m.Site)
            .WithMany(s => s.Members)
            .HasForeignKey(m => m.SiteId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(m => m.Memberships)
            .WithOne(ms => ms.Member)
            .HasForeignKey(ms => ms.MemberId);

        builder.HasMany(m => m.Charges)
            .WithOne(c => c.Member)
            .HasForeignKey(c => c.MemberId);

        builder.HasMany(m => m.AccessLogs)
            .WithOne(al => al.Member)
            .HasForeignKey(al => al.MemberId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.ToTable("Members");
    }
}
