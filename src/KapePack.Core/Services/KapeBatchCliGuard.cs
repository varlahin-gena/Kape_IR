namespace KapePack.Core.Services;

/// <summary>
/// KAPE batch mode: if <c>_kape.cli</c> sits next to kape.exe, CLI args are ignored and
/// KAPE spawns one child per line ("Found '_kape.cli' file!…"). CollectPack always passes
/// explicit args (--sim / targets), so the file must be held aside for the duration of the run.
/// </summary>
public static class KapeBatchCliGuard
{
    public const string BatchFileName = "_kape.cli";
    public const string HoldSuffix = ".packhold";

    /// <summary>Rename <c>_kape.cli</c> → <c>_kape.cli.packhold</c> while disposed restores it.</summary>
    public static IDisposable HoldAside(string workDir, Action<string>? log = null)
    {
        if (string.IsNullOrWhiteSpace(workDir) || !Directory.Exists(workDir))
            return NoopDisposable.Instance;

        var cli = Path.Combine(workDir, BatchFileName);
        if (!File.Exists(cli))
            return NoopDisposable.Instance;

        var hold = cli + HoldSuffix;
        try
        {
            if (File.Exists(hold))
                File.Delete(hold);
            File.Move(cli, hold);
            log?.Invoke(
                $"Временно отключён {BatchFileName} (иначе KAPE игнорирует аргументы и запускает batch).");
        }
        catch (Exception ex)
        {
            log?.Invoke($"Не удалось отключить {BatchFileName}: {ex.Message}");
            return NoopDisposable.Instance;
        }

        return new RestoreHold(cli, hold, log);
    }

    private sealed class RestoreHold : IDisposable
    {
        private readonly string _cli;
        private readonly string _hold;
        private readonly Action<string>? _log;
        private bool _done;

        public RestoreHold(string cli, string hold, Action<string>? log)
        {
            _cli = cli;
            _hold = hold;
            _log = log;
        }

        public void Dispose()
        {
            if (_done) return;
            _done = true;
            try
            {
                if (!File.Exists(_hold))
                    return;
                // KAPE may have created a fresh _kape.cli; prefer restoring our fleet template.
                if (File.Exists(_cli))
                    File.Delete(_cli);
                File.Move(_hold, _cli);
            }
            catch (Exception ex)
            {
                _log?.Invoke($"Не удалось вернуть {BatchFileName}: {ex.Message}");
            }
        }
    }

    private sealed class NoopDisposable : IDisposable
    {
        public static readonly NoopDisposable Instance = new();
        public void Dispose() { }
    }
}
