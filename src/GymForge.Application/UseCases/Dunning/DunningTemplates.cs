using System.Globalization;

namespace GymForge.Application.UseCases.Dunning;

/// <summary>Arma el texto del recordatorio de cobro según la etapa (tono creciente).</summary>
public static class DunningTemplates
{
    private static readonly CultureInfo Ar = CultureInfo.GetCultureInfo("es-AR");

    public static string Build(DunningStage stage, string firstName, decimal amount, DateOnly dueDate, string gymName)
    {
        var monto = amount.ToString("C0", Ar);
        var venc = dueDate.ToString("dd/MM/yyyy", Ar);

        return stage.Tone switch
        {
            "amable" =>
                $"¡Hola {firstName}! Te recordamos que tu cuota de {gymName} venció el {venc} y quedó un saldo de {monto}. Cuando puedas, acercate a abonarla. ¡Gracias!",
            "recordatorio" =>
                $"Hola {firstName}, seguimos sin registrar el pago de tu cuota de {gymName} (venció el {venc}, saldo {monto}). Te esperamos para regularizarla.",
            "firme" =>
                $"Hola {firstName}, tu cuota de {gymName} está vencida hace una semana (saldo {monto}). Para seguir ingresando necesitás ponerte al día.",
            "urgente" =>
                $"{firstName}, tu deuda con {gymName} lleva 15 días (saldo {monto}, venció el {venc}). Regularizá tu situación para no perder el acceso.",
            "final" =>
                $"{firstName}, último aviso: tu cuota de {gymName} está vencida hace un mes (saldo {monto}). Comunicate con recepción para no dar de baja tu membresía.",
            _ =>
                $"Hola {firstName}, tenés un saldo pendiente de {monto} en {gymName} (venció el {venc}).",
        };
    }
}
