namespace KapeIR.Core.Services;

/// <summary>Flags for <see cref="IPackageExporter.Export"/> (folder layout, deps, EXE).</summary>
public sealed record ExportOptions
{
    public bool InstallIntoKape { get; init; }
    public bool MakeZip { get; init; }
    public bool CopyDependencies { get; init; } = true;
    public bool IncludeModuleBin { get; init; } = true;
    public bool BuildStandaloneExe { get; init; } = true;
    public bool OverwriteExisting { get; init; }

    /// <summary>Unit-test / folder-only export: no Modules\bin, no standalone EXE.</summary>
    public static ExportOptions FolderOnly { get; } = new()
    {
        CopyDependencies = true,
        IncludeModuleBin = false,
        BuildStandaloneExe = false
    };
}
