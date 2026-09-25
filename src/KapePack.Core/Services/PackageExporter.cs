using System.IO.Compression;
using System.Text;
using System.Text.Json;
using KapePack.Core.Models;

namespace KapePack.Core.Services;

public sealed class PackageExporter : IPackageExporter
{
    private readonly KapeCatalog _catalog;
    private readonly PackageDependencyCopier _deps;
    private readonly PackageRuntimePacker _runtime;

    public PackageExporter(KapeCatalog catalog)
    {
        _catalog = catalog;
        _deps = new PackageDependencyCopier(catalog);
        _runtime = new PackageRuntimePacker(catalog);
    }

    public ExportResult Export(
        PackageDefinition pkg,
        string outputDir,
        bool installIntoKape = false,
        bool makeZip = false,
        bool copyDependencies = true,
        bool includeModuleBin = true,
        bool buildStandaloneExe = true,
        bool overwriteExisting = false,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        // Work on a clone — Export mutates Phase2 names / VolatileFirst inject / PackageId.
        pkg = pkg.Clone();
        var warnings = new List<string>();
        pkg.PackageId = KapeFileIo.EnsureGuid(pkg.PackageId);
        void Report(string msg) => progress?.Report(msg);
        void Check() => cancellationToken.ThrowIfCancellationRequested();

        Check();
        Report("Подготовка папки пакета…");
        var packageDir = Path.Combine(outputDir, PackageDefinition.SafeDir(pkg.Name));
        if (Directory.Exists(packageDir))
        {
            if (!overwriteExisting)
                throw new IOException(
                    "Папка пакета уже существует. Передайте overwriteExisting=true после подтверждения пользователя:\n" +
                    packageDir);
            Report("Удаление существующей папки пакета…");
            Directory.Delete(packageDir, true);
        }
        Directory.CreateDirectory(packageDir);

        var targetsOut = Path.Combine(packageDir, "Targets", "Compound");
        Directory.CreateDirectory(targetsOut);

        var targetName = pkg.TargetCompoundName + ".tkape";
        var targetFile = Path.Combine(targetsOut, targetName);
        File.WriteAllText(targetFile, KapeFileIo.RenderCompoundTarget(pkg));

        if (pkg.IsTwoPhase)
            EnsureTwoPhaseModules(pkg, warnings);

        string? moduleFile = null;
        string? phase2ModuleFile = null;
        List<SelectionEntry>? phase2Entries = null;
        var modulesOut = Path.Combine(packageDir, "Modules", "Compound");

        if (pkg.IsTwoPhase)
        {
            Directory.CreateDirectory(modulesOut);
            // Phase 1: shipped VolatileFirst*.mkape (copied with deps).
            moduleFile = Path.Combine(modulesOut, pkg.ModuleCompoundName + ".mkape");

            // Phase 2: any other selected modules → generated compound (skip missing bins).
            // Filter once — rewrite after CopyModules reuses the same list (no duplicate warnings).
            phase2Entries = FilterPhase2Modules(pkg, warnings);
            if (phase2Entries.Count > 0)
            {
                pkg.Phase2ModuleName = pkg.DefaultPhase2ModuleCompoundName;
                phase2ModuleFile = Path.Combine(modulesOut, pkg.Phase2ModuleName + ".mkape");
                File.WriteAllText(
                    phase2ModuleFile,
                    KapeFileIo.RenderCompoundModule(pkg, phase2Entries));
            }
            else
            {
                pkg.Phase2ModuleName = null;
            }
        }
        else if (pkg.Modules.Count > 0)
        {
            Directory.CreateDirectory(modulesOut);
            var moduleName = pkg.ModuleCompoundName + ".mkape";
            moduleFile = Path.Combine(modulesOut, moduleName!);
            var moduleEntries = FilterSinglePhaseModules(pkg, warnings);
            if (moduleEntries.Count > 0)
                File.WriteAllText(moduleFile, KapeFileIo.RenderCompoundModule(pkg, moduleEntries));
            else
                moduleFile = null;
        }

        Check();
        // Autonomous packs always need dependency targets/modules.
        if (copyDependencies || buildStandaloneExe)
        {
            Report("Копирование таргетов…");
            warnings.AddRange(_deps.CopyTargets(pkg, packageDir, cancellationToken));
            if (pkg.Modules.Count > 0 || pkg.IsTwoPhase)
            {
                Report("Копирование модулей…");
                warnings.AddRange(_deps.CopyModules(
                    pkg, packageDir, includeModuleBin || buildStandaloneExe, cancellationToken));
            }
        }

        if (pkg.IsTwoPhase)
        {
            var p1 = Path.Combine(packageDir, "Modules", "Compound",
                (pkg.ModuleCompoundName ?? PackageDefinition.DefaultPhase1Module) + ".mkape");
            if (!File.Exists(p1))
                warnings.Add($"Двухфазный пакет: не найден {Path.GetFileName(p1)} после копирования зависимостей.");
            var noMem = Path.Combine(packageDir, "Modules", "Compound",
                PackageDefinition.DefaultPhase1ModuleNoMemory + ".mkape");
            if (!File.Exists(noMem))
                warnings.Add($"Для --skip-memory нужен {PackageDefinition.DefaultPhase1ModuleNoMemory}.mkape.");
            if (File.Exists(p1))
                moduleFile = p1;

            // Re-write phase2 compound if CopyModules skipped it due to name collision with generated path.
            if (!string.IsNullOrWhiteSpace(pkg.Phase2ModuleName) &&
                phase2Entries is { Count: > 0 })
            {
                var p2 = Path.Combine(packageDir, "Modules", "Compound", pkg.Phase2ModuleName + ".mkape");
                File.WriteAllText(p2, KapeFileIo.RenderCompoundModule(pkg, phase2Entries));
                phase2ModuleFile = p2;
            }
        }

        Check();
        if (buildStandaloneExe)
        {
            Report(includeModuleBin
                ? "Копирование kape.exe / selective Modules\\bin…"
                : "Копирование kape.exe / runtime…");
            warnings.AddRange(_runtime.CopyRuntime(
                packageDir,
                includeModuleBin,
                pkg,
                fullModuleBin: false,
                cancellationToken,
                progress));
        }

        Check();
        Report("Запись скриптов и манифеста…");
        var batFile = Path.Combine(packageDir, "run_collection.bat");
        var ps1File = Path.Combine(packageDir, "run_collection.ps1");
        File.WriteAllText(batFile, KapeFileIo.RenderRunBat(pkg), KapeFileIo.BatEncoding);
        File.WriteAllText(ps1File, KapeFileIo.RenderRunPs1(pkg), KapeFileIo.BatEncoding);
        // Example only — active _kape.cli next to kape.exe makes KAPE ignore CollectPack CLI
        // args (--sim etc.) and spawn batch children. Fleet: rename to _kape.cli before kape.exe.
        File.WriteAllText(
            Path.Combine(packageDir, "_kape.cli.example"),
            KapeFileIo.RenderKapeCli(pkg),
            KapeFileIo.BatEncoding);

        var manifest = BuildManifest(pkg, warnings);
        var manifestFile = Path.Combine(packageDir, "package.json");
        File.WriteAllText(manifestFile, JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }));

        File.WriteAllText(Path.Combine(packageDir, "README.txt"), ReadmeText(pkg, installIntoKape, buildStandaloneExe));
        File.WriteAllText(
            Path.Combine(packageDir, EvidenceWrapUp.FindingsTemplateFileName),
            EvidenceWrapUp.FindingsTemplateCsv,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));

        string? installedTarget = null;
        string? installedModule = null;
        if (installIntoKape)
        {
            Check();
            Report("Установка в локальный KAPE…");
            (installedTarget, installedModule) = InstallIntoKape(pkg, targetFile, moduleFile, phase2ModuleFile);
            warnings.Add(
                "В корень KAPE записан _kape.cli.example (не активный _kape.cli). " +
                "Для fleet: переименуйте в _kape.cli перед запуском kape.exe без аргументов.");
        }

        // Always build zip when making standalone EXE (payload); optional keep zip for user.
        string? zipFile = null;
        var needZip = makeZip || buildStandaloneExe;
        var zipPath = Path.Combine(outputDir, PackageDefinition.SafeDir(pkg.Name) + ".zip");
        if (needZip)
        {
            Check();
            Report("Создание ZIP…");
            if (File.Exists(zipPath)) File.Delete(zipPath);
            ZipFile.CreateFromDirectory(packageDir, zipPath, CompressionLevel.Optimal, false);
            if (makeZip)
                zipFile = zipPath;
        }

        string? standaloneExe = null;
        if (buildStandaloneExe)
        {
            Check();
            Report("Сборка автономного EXE…");
            var stub = StandaloneExeBuilder.ResolveStubPath(_catalog.KapeRoot);
            var exeName = PackageDefinition.SafeDir(pkg.Name) + ".exe";
            standaloneExe = Path.Combine(outputDir, exeName);
            StandaloneExeBuilder.Build(stub, zipPath, standaloneExe);
            FileHash.WriteSha256Sidecar(standaloneExe);
            if (!makeZip && File.Exists(zipPath))
            {
                try { File.Delete(zipPath); } catch { /* keep if locked */ }
            }

            // Staging folder was only needed to build the payload; deliverable is the EXE
            // (+ optional ZIP / .sha256). Leave the folder only if standalone EXE was not requested.
            Check();
            Report("Удаление промежуточной папки пакета…");
            try
            {
                if (Directory.Exists(packageDir))
                    Directory.Delete(packageDir, true);
            }
            catch (Exception ex)
            {
                warnings.Add(
                    "Не удалось удалить промежуточную папку пакета (закройте файлы внутри): " +
                    packageDir + " — " + ex.Message);
            }

            if (!Directory.Exists(packageDir))
            {
                packageDir = "";
                targetFile = "";
                moduleFile = null;
                batFile = "";
                ps1File = "";
                manifestFile = "";
            }
        }

        Report("Готово");
        return new ExportResult
        {
            PackageDir = packageDir,
            TargetFile = targetFile,
            ModuleFile = moduleFile,
            BatFile = batFile,
            Ps1File = ps1File,
            ManifestFile = manifestFile,
            ZipFile = zipFile,
            StandaloneExe = standaloneExe,
            InstalledTarget = installedTarget,
            InstalledModule = installedModule,
            Warnings = warnings
        };
    }

    public static PackageDefinition LoadPackageJson(string path)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        var root = doc.RootElement;
        var pkg = new PackageDefinition
        {
            Name = root.TryGetProperty("name", out var n) ? n.GetString() ?? "Package" : "Package",
            Description = GetStr(root, "description"),
            Author = GetStr(root, "author"),
            Version = NullIfEmpty(GetStr(root, "version")) ?? "1.0",
            PackageId = GetStr(root, "package_id"),
            RecreateDirectories = !root.TryGetProperty("recreate_directories", out var rd) || rd.GetBoolean(),
            Tsource = NullIfEmpty(GetStr(root, "tsource")) ?? "C:",
            ZipOutput = !root.TryGetProperty("zip_output", out var zo) || zo.GetBoolean(),
            Flush = root.TryGetProperty("flush", out var fl) && fl.GetBoolean(),
            Vss = root.TryGetProperty("vss", out var vs) && vs.GetBoolean(),
            Notes = GetStr(root, "notes")
        };
        pkg.Targets = ReadEntries(root, "targets");
        pkg.Modules = ReadEntries(root, "modules");
        pkg.CollectionMode = LaunchManifestIo.ParseCollectionMode(root);
        pkg.CaseId = GetStr(root, "case_id");
        pkg.Phase1ModuleName = NullIfEmpty(GetStr(root, "phase1_module"))
                               ?? NullIfEmpty(GetStr(root, "phase1_module_name"))
                               ?? PackageDefinition.DefaultPhase1Module;
        pkg.Phase2ModuleName = NullIfEmpty(GetStr(root, "phase2_module"))
                               ?? NullIfEmpty(GetStr(root, "phase2_module_name"));
        return pkg;
    }

    /// <summary>Ensure VolatileFirst (+ NoMemory) are selected for two-phase export.</summary>
    private void EnsureTwoPhaseModules(PackageDefinition pkg, List<string> warnings)
    {
        if (string.IsNullOrWhiteSpace(pkg.Phase1ModuleName))
            pkg.Phase1ModuleName = PackageDefinition.DefaultPhase1Module;

        void EnsureModule(string name)
        {
            var file = name.EndsWith(".mkape", StringComparison.OrdinalIgnoreCase) ? name : name + ".mkape";
            var bare = Path.GetFileNameWithoutExtension(file);
            if (pkg.Modules.Any(m =>
                    Path.GetFileNameWithoutExtension(m.Path).Equals(bare, StringComparison.OrdinalIgnoreCase) ||
                    m.Name.Equals(bare, StringComparison.OrdinalIgnoreCase)))
                return;

            var hit = _catalog.Modules.FirstOrDefault(m =>
                m.Name.Equals(bare, StringComparison.OrdinalIgnoreCase) ||
                Path.GetFileNameWithoutExtension(m.RelativePath)
                    .Equals(bare, StringComparison.OrdinalIgnoreCase));
            if (hit is null)
            {
                warnings.Add($"Двухфазный режим: модуль «{bare}» не найден в каталоге KAPE.");
                pkg.Modules.Add(new SelectionEntry
                {
                    Name = bare,
                    Category = "LiveResponse",
                    Path = file,
                    Comments = "IR VolatileFirst"
                });
                return;
            }

            pkg.Modules.Add(new SelectionEntry
            {
                Name = hit.Name,
                Category = hit.Category,
                Path = Path.GetFileName(hit.AbsolutePath),
                Comments = "IR VolatileFirst"
            });
        }

        EnsureModule(pkg.Phase1ModuleName);
        EnsureModule(PackageDefinition.DefaultPhase1ModuleNoMemory);
    }

    private List<SelectionEntry> FilterPhase2Modules(PackageDefinition pkg, List<string> warnings)
    {
        var raw = pkg.GetPhase2ModuleEntries();
        if (raw.Count == 0)
            return raw;

        var withoutSync = new List<SelectionEntry>();
        var syncSkipped = new List<string>();
        foreach (var e in raw)
        {
            if (ModuleBinGate.IsSyncOrMaintenanceModule(e))
                syncSkipped.Add(e.Name);
            else
                withoutSync.Add(e);
        }

        if (syncSkipped.Count > 0)
        {
            warnings.Add(
                $"Phase2: пропущено {syncSkipped.Count} Sync/ToolSync модулей (не для triage) " +
                $"(например: {string.Join(", ", syncSkipped.Take(5))}{(syncSkipped.Count > 5 ? "…" : "")})");
        }

        var gated = ModuleBinGate.FilterByAvailableBinaries(_catalog, withoutSync);
        if (gated.Skipped.Count > 0)
        {
            warnings.Add(
                $"Phase2: пропущено {gated.Skipped.Count} модулей без бинарников в Modules\\bin " +
                $"(например: {string.Join(", ", gated.Skipped.Take(5))}{(gated.Skipped.Count > 5 ? "…" : "")})");
        }

        return gated.Kept;
    }

    /// <summary>Single-phase module compound: drop Sync/ToolSync and modules missing Modules\bin.</summary>
    private List<SelectionEntry> FilterSinglePhaseModules(PackageDefinition pkg, List<string> warnings)
    {
        var withoutSync = new List<SelectionEntry>();
        var syncSkipped = new List<string>();
        foreach (var e in pkg.Modules)
        {
            if (ModuleBinGate.IsSyncOrMaintenanceModule(e))
                syncSkipped.Add(e.Name);
            else
                withoutSync.Add(e);
        }

        if (syncSkipped.Count > 0)
        {
            warnings.Add(
                $"Пропущено {syncSkipped.Count} Sync/ToolSync модулей (не для triage) " +
                $"(например: {string.Join(", ", syncSkipped.Take(5))}{(syncSkipped.Count > 5 ? "…" : "")})");
        }

        var gated = ModuleBinGate.FilterByAvailableBinaries(_catalog, withoutSync);
        if (gated.Skipped.Count > 0)
        {
            warnings.Add(
                $"Пропущено {gated.Skipped.Count} модулей без бинарников в Modules\\bin " +
                $"(например: {string.Join(", ", gated.Skipped.Take(5))}{(gated.Skipped.Count > 5 ? "…" : "")})");
        }

        return gated.Kept;
    }

    private (string, string?) InstallIntoKape(
        PackageDefinition pkg,
        string targetFile,
        string? moduleFile,
        string? phase2ModuleFile = null)
    {
        var destT = Path.Combine(_catalog.KapeRoot, "Targets", "Compound", Path.GetFileName(targetFile));
        Directory.CreateDirectory(Path.GetDirectoryName(destT)!);
        File.Copy(targetFile, destT, true);

        string? destM = null;
        if (moduleFile is not null && File.Exists(moduleFile))
        {
            destM = Path.Combine(_catalog.KapeRoot, "Modules", "Compound", Path.GetFileName(moduleFile));
            Directory.CreateDirectory(Path.GetDirectoryName(destM)!);
            File.Copy(moduleFile, destM, true);
        }

        if (phase2ModuleFile is not null && File.Exists(phase2ModuleFile))
        {
            var destP2 = Path.Combine(_catalog.KapeRoot, "Modules", "Compound", Path.GetFileName(phase2ModuleFile));
            Directory.CreateDirectory(Path.GetDirectoryName(destP2)!);
            File.Copy(phase2ModuleFile, destP2, true);
        }

        var safe = PackageDefinition.SafeDir(pkg.Name);
        File.WriteAllText(Path.Combine(_catalog.KapeRoot, $"run_{safe}.bat"), KapeFileIo.RenderRunBat(pkg), KapeFileIo.BatEncoding);
        File.WriteAllText(Path.Combine(_catalog.KapeRoot, $"run_{safe}.ps1"), KapeFileIo.RenderRunPs1(pkg), KapeFileIo.BatEncoding);
        // Example only — same rule as package export: active _kape.cli forces batch mode.
        File.WriteAllText(
            Path.Combine(_catalog.KapeRoot, "_kape.cli.example"),
            KapeFileIo.RenderKapeCli(pkg),
            KapeFileIo.BatEncoding);
        return (destT, destM);
    }

    private static object BuildManifest(PackageDefinition pkg, List<string> warnings) => new
    {
        name = pkg.Name,
        description = pkg.Description,
        author = pkg.Author,
        version = pkg.Version,
        package_id = pkg.PackageId,
        recreate_directories = pkg.RecreateDirectories,
        targets = pkg.Targets.Select(t => new { name = t.Name, category = t.Category, path = t.Path, comments = t.Comments }),
        modules = pkg.Modules.Select(m => new { name = m.Name, category = m.Category, path = m.Path, comments = m.Comments }),
        tsource = pkg.Tsource,
        zip_output = pkg.ZipOutput,
        flush = pkg.Flush,
        vss = pkg.Vss,
        notes = pkg.Notes,
        target_compound = pkg.TargetCompoundName,
        module_compound = pkg.ModuleCompoundName,
        collection_mode = pkg.IsTwoPhase ? "two_phase" : "single",
        case_id = pkg.CaseId ?? "",
        phase1_module = pkg.IsTwoPhase
            ? (pkg.ModuleCompoundName ?? PackageDefinition.DefaultPhase1Module)
            : pkg.ModuleCompoundName,
        phase2_module = pkg.IsTwoPhase ? pkg.ResolvePhase2ModuleName() : null,
        warnings
    };

    private static string ReadmeText(PackageDefinition pkg, bool installed, bool standalone)
    {
        var lines = new List<string>
        {
            $"Пакет KAPE: {pkg.Name}",
            $"Описание: {pkg.Description}",
            $"Автор: {pkg.Author}",
            $"Версия: {pkg.Version}",
            $"Режим сбора: {(pkg.IsTwoPhase ? "two_phase (volatile → disk)" : "single")}",
            string.IsNullOrWhiteSpace(pkg.CaseId) ? "" : $"Case ID: {pkg.CaseId}",
            "",
            "Содержимое:",
            $"  - Compound-таргет: {pkg.TargetCompoundName}.tkape"
        };
        if (pkg.ModuleCompoundName is not null)
            lines.Add($"  - Compound-модуль: {pkg.ModuleCompoundName}.mkape");
        if (pkg.IsTwoPhase)
        {
            lines.Add($"  - Фаза 1: {pkg.Phase1ModuleName} → RESULTS\\%m\\Phase1_Volatile");
            lines.Add($"  - Фаза 2: {pkg.TargetCompoundName} → RESULTS\\%m\\Phase2_Disk");
            var p2m = pkg.ResolvePhase2ModuleName();
            if (p2m is not null)
                lines.Add($"  - Фаза 2 modules: {p2m}.mkape");
            lines.Add($"  - Также: {PackageDefinition.DefaultPhase1ModuleNoMemory} (--skip-memory)");
        }

        lines.AddRange(new[]
        {
            "  - run_collection.bat / run_collection.ps1",
            "  - _kape.cli.example (fleet: переименуйте в _kape.cli рядом с kape.exe и запустите kape.exe без аргументов;",
            "    не держите активный _kape.cli при запуске CollectPack / run_collection — KAPE тогда игнорирует CLI)",
            "  - манифест package.json",
            $"  - {EvidenceWrapUp.FindingsTemplateFileName} (скопируйте в findings.csv после сбора)",
            "  - зависимые Targets/Modules",
            ""
        });

        if (standalone)
        {
            lines.AddRange(new[]
            {
                "Автономный EXE (один файл CollectPack):",
                "  GUI:  CollectPack.exe — окно, оценка (--sim), выбор диска, сбор (UAC).",
                "  Silent / EDR:",
                "    CollectPack.exe --silent --tsource C:",
                "    CollectPack.exe --sim-only --tsource C:",
                "    CollectPack.exe --silent --tsource C: --log C:\\Windows\\Temp\\kape_pack.log",
                "  Двухфазный IR:",
                "    CollectPack.exe --silent --tsource C: --case-id IR-2026-001",
                "    CollectPack.exe --silent --tsource C: --phase 1",
                "    CollectPack.exe --silent --tsource C: --phase 2 --skip-memory",
                "  Коды выхода: 0=OK, 1=сбой сбора, 2=аргументы/payload, 3=ошибка подготовки.",
                ""
            });
        }

        lines.Add("Использование папки пакета:");
        lines.Add("  1. Нужен kape.exe в этой папке (или полный KAPE).");
        if (installed)
            lines.Add($"  2. Также установлено в локальный KAPE: target {pkg.TargetCompoundName}.");
        lines.AddRange(new[]
        {
            "  3. Запускайте от имени администратора.",
            "",
            "Чеклист после сбора:",
        });
        foreach (var item in EvidenceWrapUp.PostCollectionChecklistRu)
            lines.Add("  " + item);
        lines.AddRange(new[]
        {
            "",
            "Сгенерировано KAPE Pack Builder",
            ""
        });
        return string.Join('\n', lines.Where(l => l is not null)!);
    }

    private static List<SelectionEntry> ReadEntries(JsonElement root, string prop)
    {
        var list = new List<SelectionEntry>();
        if (!root.TryGetProperty(prop, out var arr) || arr.ValueKind != JsonValueKind.Array)
            return list;
        foreach (var el in arr.EnumerateArray())
        {
            list.Add(new SelectionEntry
            {
                Name = GetStr(el, "name"),
                Category = NullIfEmpty(GetStr(el, "category")) ?? "General",
                Path = GetStr(el, "path"),
                Comments = GetStr(el, "comments")
            });
        }
        return list;
    }

    private static string GetStr(JsonElement el, string name)
        => el.TryGetProperty(name, out var p) ? p.GetString() ?? "" : "";

    private static string? NullIfEmpty(string s) => string.IsNullOrWhiteSpace(s) ? null : s;
}
