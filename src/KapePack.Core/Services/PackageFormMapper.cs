using KapePack.Core.Models;

namespace KapePack.Core.Services;

/// <summary>Maps package definition ↔ Builder form fields.</summary>
public static class PackageFormMapper
{
    public sealed record FormSnapshot(
        string Name,
        string Description,
        string Author,
        string Version,
        string Tsource,
        bool ZipOutput,
        bool Vss,
        string Notes,
        bool TwoPhase,
        string CaseId);

    public static FormSnapshot FromPackage(PackageDefinition pkg) => new(
        pkg.Name,
        pkg.Description,
        pkg.Author,
        pkg.Version,
        pkg.Tsource,
        pkg.ZipOutput,
        pkg.Vss,
        pkg.Notes,
        pkg.IsTwoPhase,
        pkg.CaseId ?? "");

    public static void ApplyToPackage(PackageDefinition pkg, FormSnapshot form)
    {
        pkg.Name = string.IsNullOrWhiteSpace(form.Name) ? "WindowsTriage" : form.Name.Trim();
        pkg.Description = form.Description.Trim();
        pkg.Author = form.Author.Trim();
        pkg.Version = string.IsNullOrWhiteSpace(form.Version) ? "1.0" : form.Version.Trim();
        pkg.Tsource = string.IsNullOrWhiteSpace(form.Tsource) ? "C:" : form.Tsource.Trim();
        pkg.ZipOutput = form.ZipOutput;
        pkg.Flush = false;
        pkg.Vss = form.Vss;
        pkg.Notes = form.Notes.Trim();
        pkg.CollectionMode = form.TwoPhase ? IrCollectionMode.TwoPhase : IrCollectionMode.Single;
        pkg.CaseId = form.CaseId?.Trim() ?? "";
        if (pkg.IsTwoPhase && string.IsNullOrWhiteSpace(pkg.Phase1ModuleName))
            pkg.Phase1ModuleName = PackageDefinition.DefaultPhase1Module;
        if (string.IsNullOrWhiteSpace(pkg.PackageId))
            pkg.PackageId = Guid.NewGuid().ToString();
    }
}
