namespace KapeIR.Core.Services;

/// <summary>User-facing product names (EXE / UI / AppData). Project folders may differ.</summary>
public static class ProductIdentity
{
    public const string Builder = "KapeIR";
    public const string Triage = "KapeIR.Triage";

    public const string BuilderExe = Builder + ".exe";
    public const string TriageExe = Triage + ".exe";

    /// <summary>%LocalAppData%\%AppDataFolder%\…</summary>
    public const string AppDataFolder = Builder;

    /// <summary>Legacy AppData folder on disk (pre product rename); used once to migrate settings.</summary>
    public const string LegacyAppDataFolder = "KapePackBuilder";

    /// <summary>Legacy triage stub filename next to Builder / in Tools.</summary>
    public const string LegacyTriageExe = "KapePackRunner.exe";
}
