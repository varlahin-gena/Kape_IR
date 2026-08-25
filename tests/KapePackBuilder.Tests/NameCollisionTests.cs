using KapePackBuilder.Models;
using KapePackBuilder.Services;

namespace KapePackBuilder.Tests;

public class NameCollisionTests
{
    [Fact]
    public void Prefer_LeafInApps_Over_LeafInCompound()
    {
        var apps = new CatalogItem
        {
            Kind = ItemKind.Target,
            Name = "YandexDisk",
            RelativePath = "Targets/Apps/YandexDisk.tkape",
            AbsolutePath = @"D:\k\Targets\Apps\YandexDisk.tkape",
            IsCompound = false
        };
        var compound = new CatalogItem
        {
            Kind = ItemKind.Target,
            Name = "YandexDisk",
            RelativePath = "Targets/Compound/YandexDisk.tkape",
            AbsolutePath = @"D:\k\Targets\Compound\YandexDisk.tkape",
            IsCompound = false
        };
        Assert.Equal(apps.RelativePath, NameCollisionFixer.Prefer(new[] { compound, apps }).RelativePath);
    }

    [Fact]
    public void FixCollisions_MovesLoserToQuarantine()
    {
        var root = Path.Combine(Path.GetTempPath(), "kape_dup_" + Guid.NewGuid().ToString("N"));
        var apps = Path.Combine(root, "Targets", "Apps");
        var compound = Path.Combine(root, "Targets", "Compound");
        Directory.CreateDirectory(apps);
        Directory.CreateDirectory(compound);
        var appsFile = Path.Combine(apps, "YandexDisk.tkape");
        var compoundFile = Path.Combine(compound, "YandexDisk.tkape");
        File.WriteAllText(appsFile, "Description: apps\n");
        File.WriteAllText(compoundFile, "Description: compound\n");

        var items = new[]
        {
            new CatalogItem
            {
                Kind = ItemKind.Target, Name = "YandexDisk", IsCompound = false,
                RelativePath = "Targets/Apps/YandexDisk.tkape", AbsolutePath = appsFile
            },
            new CatalogItem
            {
                Kind = ItemKind.Target, Name = "YandexDisk", IsCompound = false,
                RelativePath = "Targets/Compound/YandexDisk.tkape", AbsolutePath = compoundFile
            }
        };

        try
        {
            var result = NameCollisionFixer.FixCollisions(root, items);
            Assert.Equal(1, result.Groups);
            Assert.Equal(1, result.Removed);
            Assert.True(File.Exists(appsFile));
            Assert.False(File.Exists(compoundFile));
            Assert.True(Directory.EnumerateFiles(result.QuarantineDir, "*", SearchOption.AllDirectories).Any());
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { /* ignore */ }
        }
    }
}
