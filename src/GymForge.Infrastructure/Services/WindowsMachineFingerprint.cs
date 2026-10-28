using System.Runtime.InteropServices;
using GymForge.Application.Interfaces;
using GymForge.Application.UseCases.Licensing;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;

namespace GymForge.Infrastructure.Services;

/// <summary>
/// Huella de esta PC derivada de un identificador estable de Windows: el <c>MachineGuid</c> del registro
/// (sobrevive a reinicios y a cambios de discos de datos; cambia al reinstalar Windows) y, si no se puede
/// leer, el serial del volumen del sistema. Se calcula una sola vez y nunca lanza: sin ninguna fuente
/// devuelve <c>""</c>, que la licencia interpreta como "no comparable".
/// </summary>
public sealed partial class WindowsMachineFingerprint : IMachineFingerprint
{
    private readonly ILogger<WindowsMachineFingerprint> _log;
    private readonly IReadOnlyList<Func<string?>> _sources;
    private readonly Lazy<string> _current;

    public WindowsMachineFingerprint(ILogger<WindowsMachineFingerprint> log)
        : this(log, [ReadMachineGuid, ReadSystemVolumeSerial]) { }

    /// <summary>Para tests: permite inyectar las fuentes, en orden de preferencia.</summary>
    public WindowsMachineFingerprint(ILogger<WindowsMachineFingerprint> log, IReadOnlyList<Func<string?>> sources)
    {
        _log = log;
        _sources = sources;
        _current = new Lazy<string>(Compute);
    }

    public string Current() => _current.Value;

    private string Compute()
    {
        foreach (var source in _sources)
        {
            var raw = TryRead(source);
            if (!string.IsNullOrWhiteSpace(raw))
                return MachineCode.FromSource(raw.Trim());
        }

        LogNoSourceAvailable();
        return string.Empty;
    }

    private string? TryRead(Func<string?> source)
    {
        try
        {
            return source();
        }
        // Sondeo del sistema: registro sin permisos, DLL ausente, disco raro... cualquier fallo es solo
        // "esta fuente no está disponible" y se prueba la siguiente; nunca debe tirar el arranque.
        catch (Exception ex)
        {
            LogSourceFailed(ex);
            return null;
        }
    }

    private static string? ReadMachineGuid()
    {
        if (!OperatingSystem.IsWindows()) return null;

        // Vista de 64 bits explícita: el mismo valor aunque el proceso sea de 32 bits.
        using var hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
        using var cryptography = hklm.OpenSubKey(@"SOFTWARE\Microsoft\Cryptography");
        return cryptography?.GetValue("MachineGuid") as string;
    }

    private static string? ReadSystemVolumeSerial()
    {
        if (!OperatingSystem.IsWindows()) return null;

        var systemRoot = Path.GetPathRoot(Environment.SystemDirectory);
        var found = GetVolumeInformation(
            systemRoot, IntPtr.Zero, 0, out var serial, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 0);
        return found && serial != 0 ? serial.ToString("X8") : null;
    }

    // DllImport y no LibraryImport: este generador exige AllowUnsafeBlocks en todo el proyecto, y para una
    // única llamada de respaldo no vale la pena habilitar código no seguro en Infrastructure.
#pragma warning disable SYSLIB1054
    [DllImport("kernel32.dll", EntryPoint = "GetVolumeInformationW", CharSet = CharSet.Unicode, ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetVolumeInformation(
        string? rootPathName, IntPtr volumeNameBuffer, uint volumeNameSize, out uint volumeSerialNumber,
        IntPtr maximumComponentLength, IntPtr fileSystemFlags, IntPtr fileSystemNameBuffer, uint fileSystemNameSize);
#pragma warning restore SYSLIB1054

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Huella de la PC: una fuente de identificación no estuvo disponible")]
    private partial void LogSourceFailed(Exception ex);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Huella de la PC: ninguna fuente disponible; la licencia no se puede comparar con esta máquina")]
    private partial void LogNoSourceAvailable();
}
