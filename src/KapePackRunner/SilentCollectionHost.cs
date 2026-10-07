using System.IO;
using System.Text;
using KapeIR.Core.Services;

namespace KapeIR.Triage;

/// <summary>Headless collect path for EDR / automation — thin host over <see cref="TriageRunCoordinator"/>.</summary>
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

            var coord = new TriageRunCoordinator();
            var prep = coord.Prepare(
                new TriageRunCoordinator.PrepareOptions(
                    self,
                    TsourceOverride: opt.Tsource,
                    RequireTsource: true,
                    RequireSha256: opt.VerifySha256,
                    VerifyIfSidecarPresent: true),
                log: Write,
                cancellationToken: cts.Token);

            if (prep.ExitCode != 0 || prep.Session is null)
            {
                Write(prep.Message);
                FlushLog(logPath, log);
                return prep.ExitCode;
            }

            var session = prep.Session;
            var rt = TriageRunCoordinator.BuildRuntime(
                session.Manifest.Tsource,
                simulate: opt.SimOnly,
                phaseFilter: opt.Phase,
                skipMemory: opt.SkipMemory,
                caseIdOverride: opt.CaseId);

            var result = await coord.RunAsync(
                new TriageRunCoordinator.RunOptions(session, rt),
                Write,
                cts.Token);

            if (cts.IsCancellationRequested)
            {
                Write("Сбор отменён.");
                FlushLog(logPath, log);
                return 130;
            }

            Write(result.StatusMessage);
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
