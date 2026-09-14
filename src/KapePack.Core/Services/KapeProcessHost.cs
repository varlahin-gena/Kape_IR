using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace KapePack.Core.Services;

/// <summary>Shared kape.exe process host (ArgumentList, redirected OEM console).</summary>
public static class KapeProcessHost
{
    public static Task<int> StartAsync(
        string kape,
        IReadOnlyList<string> args,
        string workDir,
        Action<string> onLine,
        Action<Process>? onStarted = null)
    {
        // Explicit argv must win: quarantine fleet batch file for this invocation.
        var hold = KapeBatchCliGuard.HoldAside(workDir, onLine);

        var enc = GetConsoleEncoding();
        var tcs = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var psi = new ProcessStartInfo
        {
            FileName = kape,
            WorkingDirectory = workDir,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = enc,
            StandardErrorEncoding = enc
        };
        foreach (var a in args)
            psi.ArgumentList.Add(a);

        var proc = new Process { StartInfo = psi, EnableRaisingEvents = true };
        onStarted?.Invoke(proc);
        proc.OutputDataReceived += (_, e) =>
        {
            if (e.Data is not null) onLine(e.Data);
        };
        proc.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is not null) onLine(e.Data);
        };
        proc.Exited += (_, _) =>
        {
            try
            {
                tcs.TrySetResult(proc.ExitCode);
            }
            finally
            {
                try { hold.Dispose(); }
                catch { /* ignore restore errors */ }
                proc.Dispose();
            }
        };

        if (!proc.Start())
        {
            try { hold.Dispose(); } catch { /* ignore */ }
            tcs.TrySetResult(-1);
            return tcs.Task;
        }

        proc.BeginOutputReadLine();
        proc.BeginErrorReadLine();
        return tcs.Task;
    }

    public static Encoding GetConsoleEncoding()
    {
        try
        {
            var oem = CultureInfo.CurrentCulture.TextInfo.OEMCodePage;
            return Encoding.GetEncoding(oem);
        }
        catch
        {
            try { return Encoding.GetEncoding(866); }
            catch { return Encoding.Default; }
        }
    }
}
