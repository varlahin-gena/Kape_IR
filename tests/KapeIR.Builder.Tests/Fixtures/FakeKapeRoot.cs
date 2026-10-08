using KapeIR.Core.Models;
using KapeIR.Core.Services;

namespace KapeIR.Builder.Tests.Fixtures;

/// <summary>Deterministic mini-KAPE trees for CI (not a full EricZimmerman catalog).</summary>
public enum FakeKapeProfile
{
    /// <summary>kape.exe + Targets/Apps/DemoLeaf + empty Modules.</summary>
    Minimal,

    /// <summary>Minimal + LeafA/LeafB + Bundle compound target.</summary>
    WithCompounds,

    /// <summary>Two-phase IR: VolatileFirst(+NoMemory), WinPmem leaf, optional winpmem.exe.</summary>
    TwoPhaseIr,

    /// <summary>Minimal + active vs !Disabled copies (catalog skip rules).</summary>
    WithDisabled
}

public sealed class FakeKapeOptions
{
    /// <summary>For <see cref="FakeKapeProfile.TwoPhaseIr"/> — write Modules\bin\winpmem.exe (default true).</summary>
    public bool IncludeWinpmemBin { get; init; } = true;

    /// <summary>Write a tiny kape.exe so <see cref="KapeRootPaths.LooksLikeKapeRoot"/> passes.</summary>
    public bool IncludeKapeExe { get; init; } = true;
}

/// <summary>
/// Builds a disposable fake KAPE root under %TEMP% and refreshes a <see cref="KapeCatalog"/>.
/// Lab tests that need a full tree keep using <see cref="TestKapeRoot"/> + SkippableFact.
/// </summary>
public sealed class FakeKapeRoot : IDisposable
{
    public const string DemoLeafName = "DemoLeaf";
    public const string LeafAName = "LeafA";
    public const string LeafBName = "LeafB";
    public const string BundleName = "Bundle";
    public const string VolatileFirstName = "VolatileFirst";
    public const string VolatileFirstNoMemoryName = "VolatileFirst_NoMemory";
    public const string WinPmemModuleName = "Velocidex_WinPmem";

    public string Root { get; }
    public KapeCatalog Catalog { get; }
    public FakeKapeProfile Profile { get; }

    private bool _disposed;

    private FakeKapeRoot(string root, FakeKapeProfile profile, KapeCatalog catalog)
    {
        Root = root;
        Profile = profile;
        Catalog = catalog;
    }

    public static FakeKapeRoot Create(FakeKapeProfile profile, FakeKapeOptions? options = null)
    {
        options ??= new FakeKapeOptions();
        KapeCatalog.ClearFileCache();

        var root = Path.Combine(Path.GetTempPath(), "fake_kape_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        if (options.IncludeKapeExe)
            File.WriteAllBytes(Path.Combine(root, "kape.exe"), new byte[] { 0x4D, 0x5A, 0x00, 0x00 });

        switch (profile)
        {
            case FakeKapeProfile.Minimal:
                WriteMinimal(root);
                break;
            case FakeKapeProfile.WithCompounds:
                WriteMinimal(root);
                WriteCompoundTargets(root);
                break;
            case FakeKapeProfile.TwoPhaseIr:
                WriteMinimal(root);
                WriteTwoPhaseModules(root, options.IncludeWinpmemBin);
                break;
            case FakeKapeProfile.WithDisabled:
                WriteMinimal(root);
                WriteDisabledCopies(root);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(profile), profile, null);
        }

        var cat = new KapeCatalog(root);
        cat.Refresh();
        return new FakeKapeRoot(root, profile, cat);
    }

    public CatalogItem RequireTarget(string name)
    {
        var item = Catalog.FindTarget(name);
        Assert.NotNull(item);
        return item!;
    }

    public CatalogItem RequireModule(string name)
    {
        var item = Catalog.FindModule(name);
        Assert.NotNull(item);
        return item!;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        // Do not ClearFileCache here — other parallel tests share the process-wide cache.
        try { Directory.Delete(Root, true); } catch { /* ignore */ }
    }

    private static void WriteMinimal(string root)
    {
        var apps = Path.Combine(root, "Targets", "Apps");
        Directory.CreateDirectory(apps);
        Directory.CreateDirectory(Path.Combine(root, "Modules"));
        Directory.CreateDirectory(Path.Combine(root, "Modules", "bin"));
        File.WriteAllText(Path.Combine(apps, DemoLeafName + ".tkape"), LeafTargetYaml(
            DemoLeafName,
            "11111111-1111-1111-1111-111111111111",
            "Apps",
            @"C:\Windows\",
            "'*.log'"));
    }

    private static void WriteCompoundTargets(string root)
    {
        var apps = Path.Combine(root, "Targets", "Apps");
        var compound = Path.Combine(root, "Targets", "Compound");
        Directory.CreateDirectory(apps);
        Directory.CreateDirectory(compound);

        File.WriteAllText(Path.Combine(apps, LeafAName + ".tkape"), LeafTargetYaml(
            LeafAName,
            "22222222-2222-2222-2222-222222222222",
            "Apps",
            @"C:\A\",
            "'*.a'"));
        File.WriteAllText(Path.Combine(apps, LeafBName + ".tkape"), LeafTargetYaml(
            LeafBName,
            "33333333-3333-3333-3333-333333333333",
            "Apps",
            @"C:\B\",
            "'*.b'"));
        File.WriteAllText(Path.Combine(compound, BundleName + ".tkape"), """
Description: fixture bundle
Author: FakeKapeRoot
Version: 1.0
Id: 44444444-4444-4444-4444-444444444444
RecreateDirectories: true
Targets:
    -
        Name: LeafA
        Category: Apps
        Path: LeafA.tkape
    -
        Name: LeafB
        Category: Apps
        Path: LeafB.tkape
""");
    }

    private static void WriteTwoPhaseModules(string root, bool includeWinpmemBin)
    {
        var compound = Path.Combine(root, "Modules", "Compound");
        var github = Path.Combine(root, "Modules", "Apps", "GitHub");
        var live = Path.Combine(root, "Modules", "LiveResponse");
        var bin = Path.Combine(root, "Modules", "bin");
        Directory.CreateDirectory(compound);
        Directory.CreateDirectory(github);
        Directory.CreateDirectory(live);
        Directory.CreateDirectory(bin);

        File.WriteAllText(Path.Combine(compound, VolatileFirstName + ".mkape"), """
Description: 'IR VolatileFirst fixture'
Category: LiveResponse
Author: FakeKapeRoot
Version: 1.0
Id: c3d4e5f6-a7b8-9012-cd34-ef5678901222
ExportFormat: txt
Processors:
    -
        Executable: Velocidex_WinPmem.mkape
        CommandLine: ""
        ExportFormat: ""
    -
        Executable: LiveResponse_NetworkDetails.mkape
        CommandLine: ""
        ExportFormat: ""
    -
        Executable: LiveResponse_ProcessDetails.mkape
        CommandLine: ""
        ExportFormat: ""
""");

        File.WriteAllText(Path.Combine(compound, VolatileFirstNoMemoryName + ".mkape"), """
Description: 'IR VolatileFirst without RAM'
Category: LiveResponse
Author: FakeKapeRoot
Version: 1.0
Id: d4e5f6a7-b8c9-0123-de45-f67890123333
ExportFormat: txt
Processors:
    -
        Executable: LiveResponse_NetworkDetails.mkape
        CommandLine: ""
        ExportFormat: ""
    -
        Executable: LiveResponse_ProcessDetails.mkape
        CommandLine: ""
        ExportFormat: ""
""");

        File.WriteAllText(Path.Combine(github, WinPmemModuleName + ".mkape"), """
Description: WinPmem Memory Dump
Category: Memory
Author: FakeKapeRoot
Version: 4.0
Id: 1d284835-417b-459e-a396-d228edea3808
ExportFormat: raw
Processors:
    -
        Executable: winpmem.exe
        CommandLine: acquire --progress "%destinationDirectory%\\memory.raw"
        ExportFormat: raw
""");

        WriteLiveLeaf(live, "LiveResponse_NetworkDetails", "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeee01");
        WriteLiveLeaf(live, "LiveResponse_ProcessDetails", "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeee02");

        if (includeWinpmemBin)
            File.WriteAllText(Path.Combine(bin, "winpmem.exe"), "MZ-fake-winpmem");
    }

    private static void WriteLiveLeaf(string dir, string name, string id)
    {
        File.WriteAllText(Path.Combine(dir, name + ".mkape"), $"""
Description: {name} fixture
Category: LiveResponse
Author: FakeKapeRoot
Version: 1.0
Id: {id}
Processors:
    -
        Executable: C:\Windows\System32\cmd.exe
        CommandLine: /c echo {name}
        ExportFormat: txt
""");
    }

    private static void WriteDisabledCopies(string root)
    {
        var apps = Path.Combine(root, "Targets", "Apps");
        var disabledT = Path.Combine(root, "Targets", "!Disabled");
        var ez = Path.Combine(root, "Modules", "EZTools");
        var disabledM = Path.Combine(root, "Modules", "!Disabled");
        Directory.CreateDirectory(disabledT);
        Directory.CreateDirectory(ez);
        Directory.CreateDirectory(disabledM);

        var leaf = Path.Combine(apps, DemoLeafName + ".tkape");
        File.Copy(leaf, Path.Combine(apps, "Active.tkape"), overwrite: true);
        File.Copy(leaf, Path.Combine(disabledT, "Hidden.tkape"), overwrite: true);

        var modYaml = """
Description: active module
Category: EZTools
Author: FakeKapeRoot
Version: 1.0
Id: 55555555-5555-5555-5555-555555555555
Processors:
    -
        Executable: C:\Windows\System32\cmd.exe
        CommandLine: /c echo ok
        ExportFormat: txt
""";
        File.WriteAllText(Path.Combine(ez, "Active.mkape"), modYaml);
        File.WriteAllText(Path.Combine(disabledM, "Hidden.mkape"), modYaml);
    }

    private static string LeafTargetYaml(string name, string id, string category, string path, string fileMask) => $"""
Description: fixture {name}
Author: FakeKapeRoot
Version: 1.0
Id: {id}
RecreateDirectories: true
Targets:
    -
        Name: {name}
        Category: {category}
        Path: {path}
        FileMask: {fileMask}
""";
}
