using System.IO;
using System.Text;
using KapePack.Core.Services;

namespace KapePackRunner;

/// <summary>Headless collect path for EDR / automation.</summary>
internal static class SilentCollectionHost
{
    public static async Task<int> RunAsync(RunnerCliOptions opt)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var logPath = ResolveLogPath(opt.LogPath);
        var log = new StringBuilder();
        void Write(string msg)
        {
            var line = $"[{DateTime.Now:HH:mm:ss}] {msg}";
            log.AppendLine(line);
            try { Console.Error.WriteLine(line); } catch { /* no console */ }
        }

        try
        {
            if (opt.Errors.Count > 0)
            {
                foreach (var e in opt.Errors) Write(e);
                FlushLog(logPath, log);
                return 2;
            }

            using var cts = new CancellationTokenSource();
            Console.CancelKeyPress += (_, e) =>
            {
                e.Cancel = true;
                Write("Отмена (Ctrl+C)…");
                try { cts.Cancel(); } catch { /* disposed */ }
            };

            var self = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(self))
            {
                Write("Не удалось определить путь к EXE.");
                FlushLog(logPath, log);
                return 3;
            }

            Write($"Log: {logPath}");

            if (opt.VerifySha256 || File.Exists(self + ".sha256"))
            {
                var require = opt.VerifySha256;
                if (!FileHash.TryVerifySidecar(self, out var verifyMsg, requireSidecar: require))
                {
                    Write(verifyMsg);
                    FlushLog(logPath, log);
                    return 2;
                }

                Write(verifyMsg);
            }

            var prep = CollectPackPrepare.Prepare(
                self,
                tsourceOverride: opt.Tsource,
                requireTsource: true,
                log: Write);

            if (prep.ExitCode != 0)
            {
                Write(prep.Message);
                FlushLog(logPath, log);
                return prep.ExitCode;
            }

            var cfg = prep.Manifest!;
            var packageDir = prep.PackageDir!;
            var kape = prep.KapeExe!;

            var rt = new CollectionPlan.RuntimeOptions(
                cfg.Tsource,
                Simulate: opt.SimOnly,
                PhaseFilter: opt.Phase,
                SkipMemory: opt.SkipMemory,
                CaseIdOverride: opt.CaseId);

            Write(opt.SimOnly ? "Режим: оценка (--sim)" : "Режим: сбор");
            if (cfg.CollectionMode == KapePack.Core.Models.IrCollectionMode.TwoPhase)
                Write($"Двухфазный IR (phase={opt.Phase?.ToString() ?? "all"}, skip_memory={opt.SkipMemory})");

            var result = await CollectionRunner.RunAsync(
                packageDir,
                kape,
                cfg,
                rt,
                self,
                Write,
                (exe, args, wd, ct) => CollectionRunner.StartKapeProcessAsync(exe, args, wd, Write, cancellationToken: ct),
                cancellationToken: cts.Token);

            if (cts.IsCancellationRequested)
            {
                Write("Сбор отменён.");
                FlushLog(logPath, log);
                return 130;
            }

            if (opt.SimOnly)
            {
                Write(result.ExitCode == 0
                    ? "Оценка (--sim) завершена. Файлы не копировались."
                    : $"Оценка завершилась с кодом {result.ExitCode}.");
                FlushLog(logPath, log);
                return result.ExitCode == 0 ? 0 : result.ExitCode;
            }

            if (result.ResultsDir is not null && Directory.Exists(result.ResultsDir))
                Write($"Готово. RESULTS: {result.ResultsDir}");
            else
                Write(result.ExitCode == 0
                    ? "Папка RESULTS не создана — сбор не выполнен."
                    : $"Ошибка kape.exe (код {result.ExitCode}).");

            FlushLog(logPath, log);
            return result.ExitCode;
        }
        catch (OperationCanceledException)
        {
            Write("Сбор отменён.");
            FlushLog(logPath, log);
            return 130;
        }
        catch (Exception ex)
        {
            Write(ex.ToString());
            FlushLog(logPath, log);
            return 3;
        }
    }

    private static string ResolveLogPath(string? requested)
    {
        if (!string.IsNullOrWhiteSpace(requested))
            return Path.GetFullPath(requested);
        var dir = CollectPackPaths.ResolveLaunchDirectory();
        return Path.Combine(dir, $"kape_pack_silent_{DateTime.Now:yyyyMMdd_HHmmss}.log");
    }

    private static void FlushLog(string path, StringBuilder log)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, log.ToString(), Encoding.UTF8);
        }
        catch
        {
            /* ignore */
        }
    }
}
