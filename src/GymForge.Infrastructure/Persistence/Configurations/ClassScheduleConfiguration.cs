using GymForge.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GymForge.Infrastructure.Persistence.Configurations;

public class ClassDescriptionConfiguration : IEntityTypeConfiguration<ClassDescription>
{
    public void Configure(EntityTypeBuilder<ClassDescription> builder)
    {
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Name).HasMaxLength(150).IsRequired();
        builder.Property(c => c.Category).HasMaxLength(80);
        builder.Property(c => c.ImageUrl).HasMaxLength(500);
        builder.Property(c => c.EquipmentNeeded).HasMaxLength(300);

        builder.HasMany(c => c.Schedules)
            .WithOne(s => s.ClassDescription)
            .HasForeignKey(s => s.ClassDescriptionId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(c => c.CompanyId).HasDatabaseName("IX_ClassDescriptions_Company");
        builder.ToTable("ClassDescriptions");
    }
}

public class ClassScheduleConfiguration : IEntityTypeConfiguration<ClassSchedule>
{
    public void Configure(EntityTypeBuilder<ClassSchedule> builder)
    {
        builder.HasKey(s => s.Id);
        builder.Property(s => s.RoomId).HasMaxLength(50);
        builder.Property(s => s.RecurrenceRule).HasMaxLength(500);

        // Computed props — not mapped
        builder.Ignore(s => s.AttendedCount);
        builder.Ignore(s => s.BookedCount);
        builder.Ignore(s => s.WaitlistCount);
        builder.Ignore(s => s.IsFull);

        builder.HasMany(s => s.Bookings)
            .WithOne(b => b.ClassSchedule)
            .HasForeignKey(b => b.ClassScheduleId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(s => new { s.SiteId, s.StartDatetime })
            .HasDatabaseName("IX_ClassSchedules_SiteStart");
        builder.HasIndex(s => new { s.InstructorId, s.StartDatetime })
            .HasDatabaseName("IX_ClassSchedules_InstructorStart");

        builder.ToTable("ClassSchedules");
    }
}

public class BookingConfiguration : IEntityTypeConfiguration<Booking>
{
    public void Configure(EntityTypeBuilder<Booking> builder)
    {
        builder.HasKey(b => b.Id);
        builder.Property(b => b.BookingType).HasConversion<string>().HasMaxLength(20);
        builder.Property(b => b.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(b => b.BookingChannel).HasConversion<string>().HasMaxLength(20);

        builder.HasOne(b => b.Member)
            .WithMany()
            .HasForeignKey(b => b.MemberId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(b => new { b.MemberId, b.Status })
            .HasDatabaseName("IX_Bookings_MemberStatus");
        builder.HasIndex(b => new { b.ClassScheduleId, b.Status })
            .HasDatabaseName("IX_Bookings_ScheduleStatus");

        builder.ToTable("Bookings");
    }
}
