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
        Action<Process>? onStarted = null,
        CancellationToken cancellationToken = default)
    {
        // Explicit argv must win: quarantine fleet batch file for this invocation.
        IDisposable? hold = null;
        Process? proc = null;
        CancellationTokenRegistration reg = default;
        try
        {
            hold = KapeBatchCliGuard.HoldAside(workDir, onLine);

            if (cancellationToken.IsCancellationRequested)
                return Task.FromCanceled<int>(cancellationToken);

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

            proc = new Process { StartInfo = psi, EnableRaisingEvents = true };
            onStarted?.Invoke(proc);

            // Locals for async handlers — must not close over hold/proc (they are nulled on ownership transfer).
            var holdOwned = hold;
            var procOwned = proc;

            if (cancellationToken.CanBeCanceled)
            {
                reg = cancellationToken.Register(() =>
                {
                    try
                    {
                        if (!procOwned.HasExited)
                            procOwned.Kill(entireProcessTree: true);
                    }
                    catch { /* ignore kill races */ }

                    tcs.TrySetCanceled(cancellationToken);
                });
            }

            procOwned.OutputDataReceived += (_, e) =>
            {
                if (e.Data is not null) onLine(e.Data);
            };
            procOwned.ErrorDataReceived += (_, e) =>
            {
                if (e.Data is not null) onLine(e.Data);
            };
            procOwned.Exited += (_, _) =>
            {
                try
                {
                    reg.Dispose();
                    // Prefer cancel if token fired; otherwise surface exit code.
                    if (cancellationToken.IsCancellationRequested)
                        tcs.TrySetCanceled(cancellationToken);
                    else
                        tcs.TrySetResult(procOwned.ExitCode);
                }
                finally
                {
                    try { holdOwned.Dispose(); }
                    catch { /* ignore restore errors */ }
                    procOwned.Dispose();
                }
            };

            if (!procOwned.Start())
            {
                try { reg.Dispose(); } catch { /* ignore */ }
                tcs.TrySetResult(-1);
                return tcs.Task; // finally disposes hold + proc
            }

            // Start succeeded: Exited owns disposal — clear locals so finally is a no-op.
            hold = null;
            proc = null;

            procOwned.BeginOutputReadLine();
            procOwned.BeginErrorReadLine();
            return tcs.Task;
        }
        catch
        {
            try { reg.Dispose(); } catch { /* ignore */ }
            throw;
        }
        finally
        {
            // Failed before successful Start (or early cancel): restore hold and free Process.
            hold?.Dispose();
            proc?.Dispose();
        }
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
            return Encoding.Default;
        }
    }
}
