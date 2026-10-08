namespace KapeIR.Core.Services;

/// <summary>
/// Shared Phase-1 winpmem.exe presence / size checks for Builder export, preflight, and Triage runner.
/// Threshold and operator copy live here so IR policy cannot drift between hosts.
/// </summary>
public enum WinPmemBinaryStatus
{
    Ok,
    Missing,
    /// <summary>File exists but is under <see cref="WinPmemBinaryGuard.SuspectMiniMaxBytes"/> (unsigned mini).</summary>
    SuspectMini
}

public sealed record WinPmemBinaryCheck(
    WinPmemBinaryStatus Status,
    string Path,
    long? LengthBytes);

public static class WinPmemBinaryGuard
{
    public const string FileName = "winpmem.exe";

    /// <summary>
    /// Signed go-winpmem is typically several MB; unsigned mini builds are well under this.
    /// </summary>
    public const long SuspectMiniMaxBytes = 1_500_000;

    public const string SignedArtifactHint = "go-winpmem_amd64_1.0-rc2_signed.exe";

    /// <summary>KAPE / CollectPack root → <c>Modules\bin\winpmem.exe</c>.</summary>
    public static string ResolveUnderRoot(string kapeOrPackageRoot)
        => Path.Combine(kapeOrPackageRoot, "Modules", "bin", FileName);

    /// <summary>Already pointing at <c>Modules\bin</c>.</summary>
    public static string ResolveUnderBin(string modulesBinDir)
        => Path.Combine(modulesBinDir, FileName);

    public static WinPmemBinaryCheck InspectUnderRoot(string kapeOrPackageRoot)
        => Inspect(ResolveUnderRoot(kapeOrPackageRoot));

    public static WinPmemBinaryCheck InspectUnderBin(string modulesBinDir)
        => Inspect(ResolveUnderBin(modulesBinDir));

    public static WinPmemBinaryCheck Inspect(string winpmemPath)
    {
        if (string.IsNullOrWhiteSpace(winpmemPath) || !File.Exists(winpmemPath))
            return new WinPmemBinaryCheck(WinPmemBinaryStatus.Missing, winpmemPath ?? "", null);

        try
        {
            var len = new FileInfo(winpmemPath).Length;
            if (len > 0 && len < SuspectMiniMaxBytes)
                return new WinPmemBinaryCheck(WinPmemBinaryStatus.SuspectMini, winpmemPath, len);
            return new WinPmemBinaryCheck(WinPmemBinaryStatus.Ok, winpmemPath, len);
        }
        catch
        {
            // Best-effort size check — treat as present if we cannot read length.
            return new WinPmemBinaryCheck(WinPmemBinaryStatus.Ok, winpmemPath, null);
        }
    }

    /// <summary>Single warning line for Builder export dependency list; null when OK.</summary>
    public static string? FormatBuilderWarning(WinPmemBinaryCheck check) => check.Status switch
    {
        WinPmemBinaryStatus.Missing =>
            "ОШИБКА: winpmem.exe не найден в Modules\\bin. Без --skip-memory фаза 1 " +
            "будет остановлена. Нужен " + SignedArtifactHint + " → winpmem.exe " +
            "(см. BinaryUrl в Velocidex_WinPmem.mkape), не unsigned mini.",
        WinPmemBinaryStatus.SuspectMini =>
            $"ВНИМАНИЕ: winpmem.exe ≈ {check.LengthBytes:N0} байт (похоже на unsigned mini). " +
            "При Secure Boot дамп памяти обычно пустой. Замените на " +
            SignedArtifactHint + " → Modules\\bin\\winpmem.exe.",
        _ => null
    };

    /// <summary>Multi-line operator log for Triage <see cref="CollectionRunner"/> (empty when OK).</summary>
    public static IReadOnlyList<string> FormatRunnerLogs(WinPmemBinaryCheck check) => check.Status switch
    {
        WinPmemBinaryStatus.Missing => new[]
        {
            "winpmem.exe не найден в Modules\\bin — фаза 1 (VolatileFirst) не снимет дамп памяти.",
            "Скачайте " + SignedArtifactHint + " (BinaryUrl в Velocidex_WinPmem.mkape) и переименуйте в winpmem.exe.",
            "Либо запустите с --skip-memory (модуль VolatileFirst_NoMemory)."
        },
        WinPmemBinaryStatus.SuspectMini => new[]
        {
            $"ВНИМАНИЕ: winpmem.exe слишком маленький ({check.LengthBytes:N0} байт) — похоже на unsigned mini.",
            "При Secure Boot драйвер не загрузится, файл memory.raw не появится.",
            "Замените на " + SignedArtifactHint + " → winpmem.exe (см. Velocidex_WinPmem.mkape)."
        },
        _ => Array.Empty<string>()
    };

    /// <summary>Confirm-dialog lines for <see cref="ModulesBinPreflight"/> (empty when OK).</summary>
    public static IReadOnlyList<string> FormatPreflightNotes(WinPmemBinaryCheck check) => check.Status switch
    {
        WinPmemBinaryStatus.Missing => new[]
        {
            "winpmem.exe не найден — фаза 1 не снимет дамп памяти",
            "(нужен signed go-winpmem → Modules\\bin\\winpmem.exe или --skip-memory на целевой системе)."
        },
        WinPmemBinaryStatus.SuspectMini => new[]
        {
            $"winpmem.exe похож на unsigned mini (≈ {check.LengthBytes:N0} байт) — при Secure Boot дамп памяти обычно пустой.",
            "(замените на signed go-winpmem → Modules\\bin\\winpmem.exe)."
        },
        _ => Array.Empty<string>()
    };
}
