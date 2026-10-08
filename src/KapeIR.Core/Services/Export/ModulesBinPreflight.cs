using System.Text;
using KapeIR.Core.Models;

namespace KapeIR.Core.Services;

/// <summary>
/// Pre-export check: missing Modules\bin payloads for the selected module set
/// (before the user picks an output folder).
/// </summary>
public static class ModulesBinPreflight
{
    public sealed record Result(
        IReadOnlyList<string> MissingPayloads,
        IReadOnlyList<string> SkippedModules,
        bool WinpmemMissing,
        bool WinpmemSuspectMini,
        string ModulesBinPath,
        WinPmemBinaryCheck? Winpmem = null)
    {
        public bool HasIssues =>
            MissingPayloads.Count > 0 ||
            SkippedModules.Count > 0 ||
            WinpmemMissing ||
            WinpmemSuspectMini;
    }

    /// <summary>
    /// Analyze package as it will be exported (injects VolatileFirst* when two-phase).
    /// </summary>
    public static Result Check(KapeCatalog catalog, PackageDefinition pkg)
    {
        var work = pkg.Clone();
        if (work.IsTwoPhase)
            EnsureTwoPhaseModules(work, catalog);

        var binPath = Path.Combine(catalog.KapeRoot, "Modules", "bin");

        var withoutSync = work.Modules
            .Where(m => !ModuleBinGate.IsSyncOrMaintenanceModule(m))
            .ToList();
        var gated = ModuleBinGate.FilterByAvailableBinaries(catalog, withoutSync);
        var analysis = SelectiveModulesBinCopier.Analyze(catalog, work);

        WinPmemBinaryCheck? winpmem = null;
        var winpmemMissing = false;
        var winpmemSuspectMini = false;
        if (work.IsTwoPhase)
        {
            winpmem = WinPmemBinaryGuard.InspectUnderBin(binPath);
            winpmemMissing = winpmem.Status == WinPmemBinaryStatus.Missing;
            winpmemSuspectMini = winpmem.Status == WinPmemBinaryStatus.SuspectMini;
        }

        return new Result(
            analysis.Missing,
            gated.Skipped,
            winpmemMissing,
            winpmemSuspectMini,
            binPath,
            winpmem);
    }

    public static string FormatConfirmMessage(Result r, int maxLines = 18)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Перед сборкой не хватает бинарников в Modules\\bin.");
        sb.AppendLine("Модули без файлов будут пропущены (или Phase1 без RAM).");
        sb.AppendLine();
        sb.AppendLine($"Папка: {r.ModulesBinPath}");
        sb.AppendLine();

        if (r.MissingPayloads.Count > 0)
        {
            sb.AppendLine($"Отсутствуют файлы/папки ({r.MissingPayloads.Count}):");
            foreach (var m in r.MissingPayloads.Take(maxLines))
                sb.AppendLine("  • " + m);
            if (r.MissingPayloads.Count > maxLines)
                sb.AppendLine($"  … и ещё {r.MissingPayloads.Count - maxLines}");
            sb.AppendLine();
        }

        if (r.SkippedModules.Count > 0)
        {
            sb.AppendLine($"Модули, которые будут пропущены ({r.SkippedModules.Count}):");
            foreach (var m in r.SkippedModules.Take(maxLines))
                sb.AppendLine("  • " + m);
            if (r.SkippedModules.Count > maxLines)
                sb.AppendLine($"  … и ещё {r.SkippedModules.Count - maxLines}");
            sb.AppendLine();
        }

        if (r.Winpmem is { Status: not WinPmemBinaryStatus.Ok } check)
        {
            foreach (var line in WinPmemBinaryGuard.FormatPreflightNotes(check))
                sb.AppendLine(line);
            sb.AppendLine();
        }

        sb.AppendLine("Положите недостающее в Modules\\bin и соберите снова,");
        sb.AppendLine("либо продолжите без этих инструментов.");
        sb.AppendLine();
        sb.AppendLine("Да = продолжить сборку без недостающих бинарников");
        sb.Append("Нет = отмена (добавите файлы и повторите)");
        return sb.ToString();
    }

    private static void EnsureTwoPhaseModules(PackageDefinition pkg, KapeCatalog catalog)
    {
        if (string.IsNullOrWhiteSpace(pkg.Phase1ModuleName))
            pkg.Phase1ModuleName = PackageDefinition.DefaultPhase1Module;

        void EnsureModule(string name)
        {
            var file = name.EndsWith(".mkape", StringComparison.OrdinalIgnoreCase) ? name : name + ".mkape";
            var bare = Path.GetFileNameWithoutExtension(file);
            if (pkg.Modules.Any(m =>
                    Path.GetFileNameWithoutExtension(m.Path ?? "")
                        .Equals(bare, StringComparison.OrdinalIgnoreCase) ||
                    m.Name.Equals(bare, StringComparison.OrdinalIgnoreCase)))
                return;

            var hit = catalog.Modules.FirstOrDefault(m =>
                m.Name.Equals(bare, StringComparison.OrdinalIgnoreCase) ||
                Path.GetFileNameWithoutExtension(m.RelativePath)
                    .Equals(bare, StringComparison.OrdinalIgnoreCase));
            if (hit is null)
            {
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
}
