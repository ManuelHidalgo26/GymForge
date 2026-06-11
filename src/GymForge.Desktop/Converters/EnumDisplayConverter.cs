using System.Globalization;
using Avalonia.Data.Converters;
using GymForge.Desktop.ViewModels.Cash;
using GymForge.Desktop.ViewModels.Charges;
using GymForge.Domain.Enums;

namespace GymForge.Desktop.Converters;

/// <summary>Muestra los enums del dominio en español. Fallback: el nombre del enum.</summary>
public class EnumDisplayConverter : IValueConverter
{
    private static readonly Dictionary<object, string> Map = new()
    {
        // Métodos de pago
        [PaymentMethod.Cash] = "Efectivo",
        [PaymentMethod.CreditCard] = "Tarjeta de crédito",
        [PaymentMethod.DebitCard] = "Tarjeta de débito",
        [PaymentMethod.BankTransfer] = "Transferencia",
        [PaymentMethod.DirectDebit] = "Débito automático",
        [PaymentMethod.MercadoPago] = "Mercado Pago",
        [PaymentMethod.Voucher] = "Voucher",
        [PaymentMethod.AccountCredit] = "Saldo a favor",
        [PaymentMethod.Cheque] = "Cheque",

        // Movimientos de caja
        [CashMovementType.Income] = "Ingreso",
        [CashMovementType.Expense] = "Egreso",
        [CashMovementType.Withdrawal] = "Retiro",
        [CashMovementType.Deposit] = "Depósito",

        // Documento / género
        [DocumentType.DNI] = "DNI",
        [DocumentType.PASS] = "Pasaporte",
        [DocumentType.CUIL] = "CUIL",
        [DocumentType.CE] = "Cédula",
        [Gender.Male] = "Masculino",
        [Gender.Female] = "Femenino",
        [Gender.NonBinary] = "No binario",
        [Gender.PreferNotToSay] = "Prefiere no decir",

        // Estado de socio
        [MemberStatus.Prospect] = "Prospecto",
        [MemberStatus.Active] = "Activo",
        [MemberStatus.Suspended] = "Suspendido",
        [MemberStatus.Frozen] = "Congelado",
        [MemberStatus.Overdue] = "En mora",
        [MemberStatus.Expired] = "Vencido",
        [MemberStatus.Cancelled] = "Cancelado",

        // Estado de membresía
        [MembershipStatus.Trial] = "Prueba",
        [MembershipStatus.PendingActivation] = "Pendiente",
        [MembershipStatus.Active] = "Activa",
        [MembershipStatus.Frozen] = "Congelada",
        [MembershipStatus.Overdue] = "En mora",
        [MembershipStatus.Suspended] = "Suspendida",
        [MembershipStatus.Expired] = "Vencida",
        [MembershipStatus.Cancelled] = "Cancelada",

        // Origen del socio
        [MemberSource.WalkIn] = "Mostrador",
        [MemberSource.Referral] = "Referido",
        [MemberSource.SocialMedia] = "Redes sociales",
        [MemberSource.GoogleAds] = "Google Ads",
        [MemberSource.Website] = "Sitio web",
        [MemberSource.Event] = "Evento",
        [MemberSource.Other] = "Otro",

        // Concepto de cobro
        [SaleConcept.Membership] = "Membresía",
        [SaleConcept.Product] = "Producto",

        // Filtro de cobros
        [ChargeStatusFilter.Pending] = "Pendientes",
        [ChargeStatusFilter.Overdue] = "Vencidos",
        [ChargeStatusFilter.All] = "Todos",

        // Modalidad de plan
        [MembershipBasis.OpenEnded] = "Sin vencimiento",
        [MembershipBasis.Renewal] = "Renovable",
        [MembershipBasis.PresetDate] = "Hasta fecha fija",
        [MembershipBasis.VisitPack] = "Pack de visitas",
        [MembershipBasis.ClassPack] = "Pack de clases",
        [MembershipBasis.Trial] = "Prueba",
        [MembershipBasis.AddOn] = "Adicional",

        // Unidades de duración (strings del dominio)
        ["Day"] = "Día(s)",
        ["Month"] = "Mes(es)",
        ["Year"] = "Año(s)",

        // Grupos musculares
        [MuscleGroup.Chest] = "Pecho",
        [MuscleGroup.Back] = "Espalda",
        [MuscleGroup.Shoulders] = "Hombros",
        [MuscleGroup.Biceps] = "Bíceps",
        [MuscleGroup.Triceps] = "Tríceps",
        [MuscleGroup.Forearms] = "Antebrazos",
        [MuscleGroup.Quads] = "Cuádriceps",
        [MuscleGroup.Hamstrings] = "Isquiotibiales",
        [MuscleGroup.Glutes] = "Glúteos",
        [MuscleGroup.Calves] = "Gemelos",
        [MuscleGroup.Abs] = "Abdominales",
        [MuscleGroup.Obliques] = "Oblicuos",
        [MuscleGroup.Traps] = "Trapecios",
        [MuscleGroup.Lats] = "Dorsales",
        [MuscleGroup.Cardio] = "Cardio",
        [MuscleGroup.FullBody] = "Cuerpo completo",

        // Equipamiento
        [Equipment.Barbell] = "Barra",
        [Equipment.Dumbbell] = "Mancuernas",
        [Equipment.Machine] = "Máquina",
        [Equipment.Cable] = "Polea",
        [Equipment.Bodyweight] = "Peso corporal",
        [Equipment.Band] = "Banda elástica",
        [Equipment.Kettlebell] = "Pesa rusa",
        [Equipment.Other] = "Otro",

        // Tipo de movimiento
        [MovementType.Compound] = "Compuesto",
        [MovementType.Isolation] = "Aislamiento",
        [MovementType.Cardio] = "Cardio",
        [MovementType.Mobility] = "Movilidad",
    };

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is not null && Map.TryGetValue(value, out var es) ? es : value?.ToString() ?? string.Empty;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
