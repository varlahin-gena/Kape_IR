using System.IO;

namespace KapePackRunner;

/// <summary>Ready fixed/removable drives for CollectPack source picker.</summary>
public static class DriveInventory
{
    public sealed record DriveItem(string Root, string Label)
    {
        public override string ToString() => Label;
    }

    public static IReadOnlyList<DriveItem> ListReady(string? preferredRoot = null)
    {
        var items = new List<DriveItem>();
        try
        {
            foreach (var d in DriveInfo.GetDrives().Where(x => x.IsReady))
            {
                var root = d.Name.TrimEnd('\\');
                string label;
                try
                {
                    label = string.IsNullOrWhiteSpace(d.VolumeLabel)
                        ? root
                        : $"{root} ({d.VolumeLabel})";
                }
                catch
                {
                    label = root;
                }

                items.Add(new DriveItem(root, label));
            }
        }
        catch { /* ignore */ }

        if (items.Count == 0)
            items.Add(new DriveItem("C:", "C:"));

        return items;
    }

    public static int IndexOfPreferred(IReadOnlyList<DriveItem> items, string preferred)
    {
        string? preferredRoot = null;
        try
        {
            preferredRoot = Path.GetPathRoot(preferred.EndsWith(':') ? preferred + "\\" : preferred);
        }
        catch { /* ignore */ }

        if (preferredRoot is null) return 0;
        var needle = preferredRoot.TrimEnd('\\');
        for (var i = 0; i < items.Count; i++)
        {
            if (items[i].Root.StartsWith(needle, StringComparison.OrdinalIgnoreCase))
                return i;
        }

        return 0;
    }
}
