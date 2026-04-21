using GymForge.Domain.Enums;

namespace GymForge.Domain.Entities;

public class BodyMeasurement : BaseEntity
{
    public Guid MemberId { get; private set; }
    public Guid CompanyId { get; private set; }
    public DateTime MeasuredAt { get; private set; }
    public Guid? TakenByStaffId { get; private set; }
    public BodyMeasurementMethod Method { get; private set; }

    // Básicos
    public decimal? WeightKg { get; private set; }
    public decimal? HeightCm { get; private set; }
    public decimal? Bmi => WeightKg.HasValue && HeightCm.HasValue && HeightCm > 0
        ? Math.Round(WeightKg.Value / (decimal)Math.Pow((double)(HeightCm.Value / 100), 2), 1)
        : null;
    public decimal? BodyFatPct { get; private set; }
    public string? BodyFatMethod { get; private set; }
    public decimal? MuscleMassKg { get; private set; }
    public decimal? BodyWaterPct { get; private set; }
    public int? VisceralFat { get; private set; }
    public int? BasalMetabolicRate { get; private set; }

    // Circunferencias (cm)
    public decimal? NeckCm { get; private set; }
    public decimal? ShouldersCm { get; private set; }
    public decimal? ChestCm { get; private set; }
    public decimal? WaistCm { get; private set; }
    public decimal? AbdomenCm { get; private set; }
    public decimal? HipsCm { get; private set; }
    public decimal? LeftBicepCm { get; private set; }
    public decimal? RightBicepCm { get; private set; }
    public decimal? LeftForearmCm { get; private set; }
    public decimal? RightForearmCm { get; private set; }
    public decimal? LeftThighCm { get; private set; }
    public decimal? RightThighCm { get; private set; }
    public decimal? LeftCalfCm { get; private set; }
    public decimal? RightCalfCm { get; private set; }

    // Pliegues (mm)
    public decimal? TricepsMm { get; private set; }
    public decimal? SubscapularMm { get; private set; }
    public decimal? SuprailiacMm { get; private set; }
    public decimal? AbdominalMm { get; private set; }
    public decimal? ThighMm { get; private set; }
    public decimal? ChestMm { get; private set; }

    // Calculados
    public decimal? WaistToHipRatio => WaistCm.HasValue && HipsCm.HasValue && HipsCm > 0
        ? Math.Round(WaistCm.Value / HipsCm.Value, 2)
        : null;
    public decimal? WaistToHeightRatio => WaistCm.HasValue && HeightCm.HasValue && HeightCm > 0
        ? Math.Round(WaistCm.Value / HeightCm.Value, 2)
        : null;

    // Fotos
    public string? PhotoFront { get; private set; }
    public string? PhotoSide { get; private set; }
    public string? PhotoBack { get; private set; }
    public string? Notes { get; private set; }

    public Member Member { get; private set; } = null!;

    private BodyMeasurement() { }

    public static BodyMeasurement Create(
        Guid companyId,
        Guid memberId,
        DateTime measuredAt,
        BodyMeasurementMethod method,
        Guid? takenByStaffId = null)
    {
        return new BodyMeasurement
        {
            CompanyId = companyId,
            MemberId = memberId,
            MeasuredAt = measuredAt,
            Method = method,
            TakenByStaffId = takenByStaffId
        };
    }

    public BodyMeasurement WithWeight(decimal weightKg, decimal? heightCm = null)
    {
        WeightKg = weightKg;
        if (heightCm.HasValue) HeightCm = heightCm;
        return this;
    }

    public BodyMeasurement WithCircumferences(
        decimal? waist = null, decimal? hips = null, decimal? chest = null,
        decimal? neck = null, decimal? shoulders = null, decimal? abdomen = null,
        decimal? leftBicep = null, decimal? rightBicep = null)
    {
        WaistCm = waist;
        HipsCm = hips;
        ChestCm = chest;
        NeckCm = neck;
        ShouldersCm = shoulders;
        AbdomenCm = abdomen;
        LeftBicepCm = leftBicep;
        RightBicepCm = rightBicep;
        return this;
    }
}
