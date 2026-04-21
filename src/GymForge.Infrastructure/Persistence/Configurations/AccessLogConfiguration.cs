using GymForge.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GymForge.Infrastructure.Persistence.Configurations;

public class AccessLogConfiguration : IEntityTypeConfiguration<AccessLog>
{
    public void Configure(EntityTypeBuilder<AccessLog> builder)
    {
        builder.HasKey(a => a.Id);
        builder.Property(a => a.Method).HasConversion<string>().HasMaxLength(20);
        builder.Property(a => a.Direction).HasConversion<string>().HasMaxLength(10);
        builder.Property(a => a.DenialReason).HasConversion<string>().HasMaxLength(30);
        builder.Property(a => a.TagSerial).HasMaxLength(50);

        builder.HasIndex(a => new { a.MemberId, a.SwipedAt }).HasDatabaseName("IX_AccessLogs_MemberSwipedAt");
        builder.HasIndex(a => new { a.DoorId, a.SwipedAt }).HasDatabaseName("IX_AccessLogs_DoorSwipedAt");
        builder.HasIndex(a => new { a.SiteId, a.SwipedAt }).HasDatabaseName("IX_AccessLogs_SiteSwipedAt");
        builder.HasIndex(a => a.TagSerial).HasDatabaseName("IX_AccessLogs_TagSerial");

        builder.HasOne(a => a.Member)
            .WithMany(m => m.AccessLogs)
            .HasForeignKey(a => a.MemberId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.ToTable("AccessLogs");
    }
}

public class AuditLogConfiguration : IEntityTypeConfiguration<AuditLog>
{
    public void Configure(EntityTypeBuilder<AuditLog> builder)
    {
        builder.HasKey(a => a.Id);
        builder.Property(a => a.EntityType).HasMaxLength(100).IsRequired();
        builder.Property(a => a.Action).HasMaxLength(50).IsRequired();
        builder.Property(a => a.Ip).HasMaxLength(45);

        builder.HasIndex(a => new { a.CompanyId, a.Timestamp }).HasDatabaseName("IX_AuditLogs_CompanyTimestamp");
        builder.HasIndex(a => new { a.EntityType, a.EntityId }).HasDatabaseName("IX_AuditLogs_Entity");

        builder.ToTable("AuditLogs");
    }
}
