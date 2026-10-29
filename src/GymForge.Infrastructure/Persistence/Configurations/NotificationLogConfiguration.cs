using GymForge.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GymForge.Infrastructure.Persistence.Configurations;

public class NotificationLogConfiguration : IEntityTypeConfiguration<NotificationLog>
{
    public void Configure(EntityTypeBuilder<NotificationLog> builder)
    {
        builder.HasKey(n => n.Id);
        builder.Property(n => n.Channel).HasConversion<string>().HasMaxLength(20);
        builder.Property(n => n.Trigger).HasConversion<string>().HasMaxLength(20);
        builder.Property(n => n.ToPhone).HasMaxLength(30);

        builder.HasIndex(n => new { n.MemberId, n.SentAt }).HasDatabaseName("IX_NotificationLogs_MemberSentAt");
        // Soporta WasSentAsync (dedup del job por cargo + etapa).
        builder.HasIndex(n => new { n.CompanyId, n.ChargeId, n.StageDaysOverdue })
            .HasDatabaseName("IX_NotificationLogs_Dedup");

        // El log sobrevive al socio.
        builder.HasOne(n => n.Member)
            .WithMany()
            .HasForeignKey(n => n.MemberId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.ToTable("NotificationLogs");
    }
}
