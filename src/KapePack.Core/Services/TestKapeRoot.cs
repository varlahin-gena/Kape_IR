namespace KapePackBuilder.Services;

/// <summary>Resolves a KAPE root for tests (env KAPE_ROOT, then common candidates).</summary>
public static class TestKapeRoot
{
    public static string? TryGet()
    {
        var env = Environment.GetEnvironmentVariable("KAPE_ROOT");
        if (!string.IsNullOrWhiteSpace(env) && Directory.Exists(Path.Combine(env, "Targets")))
            return env;

        foreach (var c in AppSettings.CandidateRoots(null))
        {
            if (Directory.Exists(Path.Combine(c, "Targets")))
                return c;
        }

        // Walk up from test output dir toward a repo-adjacent KAPE tree.
        try
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            for (var i = 0; i < 8 && dir is not null; i++, dir = dir.Parent)
            {
                var candidate = dir.FullName;
                if (Directory.Exists(Path.Combine(candidate, "Targets")))
                    return candidate;
                var sibling = Path.Combine(dir.FullName, "Kape");
                if (Directory.Exists(Path.Combine(sibling, "Targets")))
                    return sibling;
            }
        }
        catch { /* ignore */ }

        return null;
    }
}
