using GymForge.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GymForge.Infrastructure.Persistence.Configurations;

public class ExerciseConfiguration : IEntityTypeConfiguration<Exercise>
{
    public void Configure(EntityTypeBuilder<Exercise> builder)
    {
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Name).HasMaxLength(150).IsRequired();
        builder.Property(e => e.PrimaryMuscleGroup).HasConversion<string>().HasMaxLength(20);
        builder.Property(e => e.Equipment).HasConversion<string>().HasMaxLength(20);
        builder.Property(e => e.MovementType).HasConversion<string>().HasMaxLength(20);
        builder.Property(e => e.VideoUrl).HasMaxLength(500);
        builder.Property(e => e.ImageUrl).HasMaxLength(500);
        builder.Property(e => e.AnimatedGifUrl).HasMaxLength(500);

        builder.HasIndex(e => e.TenantId).HasDatabaseName("IX_Exercises_Tenant");
        builder.HasIndex(e => e.PrimaryMuscleGroup).HasDatabaseName("IX_Exercises_MuscleGroup");
        builder.ToTable("Exercises");
    }
}

public class RoutineConfiguration : IEntityTypeConfiguration<Routine>
{
    public void Configure(EntityTypeBuilder<Routine> builder)
    {
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Name).HasMaxLength(150).IsRequired();
        builder.Property(r => r.Goal).HasConversion<string>().HasMaxLength(20);

        builder.HasMany(r => r.Days)
            .WithOne(d => d.Routine)
            .HasForeignKey(d => d.RoutineId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(r => r.MemberId).HasDatabaseName("IX_Routines_Member");
        builder.ToTable("Routines");
    }
}

public class RoutineDayConfiguration : IEntityTypeConfiguration<RoutineDay>
{
    public void Configure(EntityTypeBuilder<RoutineDay> builder)
    {
        builder.HasKey(d => d.Id);
        builder.Property(d => d.Name).HasMaxLength(100);

        builder.HasMany(d => d.Items)
            .WithOne(i => i.RoutineDay)
            .HasForeignKey(i => i.RoutineDayId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.ToTable("RoutineDays");
    }
}

public class RoutineItemConfiguration : IEntityTypeConfiguration<RoutineItem>
{
    public void Configure(EntityTypeBuilder<RoutineItem> builder)
    {
        builder.HasKey(i => i.Id);
        builder.Property(i => i.Technique).HasConversion<string>().HasMaxLength(20);
        builder.Property(i => i.Tempo).HasMaxLength(20);
        builder.Property(i => i.Notes).HasMaxLength(500);

        builder.HasOne(i => i.Exercise)
            .WithMany()
            .HasForeignKey(i => i.ExerciseId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(i => i.Sets)
            .WithOne(s => s.RoutineItem)
            .HasForeignKey(s => s.RoutineItemId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.ToTable("RoutineItems");
    }
}

public class RoutineItemSetConfiguration : IEntityTypeConfiguration<RoutineItemSet>
{
    public void Configure(EntityTypeBuilder<RoutineItemSet> builder)
    {
        builder.HasKey(s => s.Id);
        builder.Property(s => s.TargetWeightKg).HasColumnType("DECIMAL(6,2)");
        builder.ToTable("RoutineItemSets");
    }
}

public class WorkoutLogConfiguration : IEntityTypeConfiguration<WorkoutLog>
{
    public void Configure(EntityTypeBuilder<WorkoutLog> builder)
    {
        builder.HasKey(w => w.Id);
        builder.HasIndex(w => new { w.MemberId, w.PerformedAt }).HasDatabaseName("IX_WorkoutLogs_MemberPerformedAt");
        builder.ToTable("WorkoutLogs");
    }
}

public class BodyMeasurementConfiguration : IEntityTypeConfiguration<BodyMeasurement>
{
    public void Configure(EntityTypeBuilder<BodyMeasurement> builder)
    {
        builder.HasKey(b => b.Id);
        builder.Property(b => b.Method).HasConversion<string>().HasMaxLength(20);
        builder.Property(b => b.WeightKg).HasColumnType("DECIMAL(6,2)");
        builder.Property(b => b.HeightCm).HasColumnType("DECIMAL(6,2)");
        builder.Property(b => b.BodyFatPct).HasColumnType("DECIMAL(5,2)");
        builder.Property(b => b.MuscleMassKg).HasColumnType("DECIMAL(6,2)");

        builder.Ignore(b => b.Bmi);
        builder.Ignore(b => b.WaistToHipRatio);
        builder.Ignore(b => b.WaistToHeightRatio);

        builder.HasIndex(b => new { b.MemberId, b.MeasuredAt }).HasDatabaseName("IX_BodyMeasurements_MemberDate");
        builder.ToTable("BodyMeasurements");
    }
}
