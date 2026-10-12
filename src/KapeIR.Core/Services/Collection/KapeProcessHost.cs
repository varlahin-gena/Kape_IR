using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace KapeIR.Core.Services;

/// <summary>
/// Shared kape.exe process host (ArgumentList, redirected console).
/// Uses the OEM code page for redirect decode (kape.exe / Win32 tools print
/// Russian errors as CP866). <see cref="KapeConsoleLineSanitizer"/> recovers
/// UTF-8 Chainsaw/Hayabusa banners that arrive as OEM-misread bytes.
/// </summary>
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

            var oem = GetOemEncoding();
            var tcs = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
            var psi = new ProcessStartInfo
            {
                FileName = kape,
                WorkingDirectory = workDir,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                // OEM: Russian KAPE/Win32 errors. Sanitizer restores UTF-8 tool banners.
                StandardOutputEncoding = oem,
                StandardErrorEncoding = oem
            };
            foreach (var a in args)
                psi.ArgumentList.Add(a);

            proc = new Process { StartInfo = psi, EnableRaisingEvents = true };
            onStarted?.Invoke(proc);

            // Locals for async handlers — must not close over hold/proc (they are nulled on ownership transfer).
            var holdOwned = hold;
            var procOwned = proc;
            var oemEnc = oem;
            Action<string> emit = raw =>
            {
                var cleaned = KapeConsoleLineSanitizer.Sanitize(raw, oemEnc);
                if (cleaned.Length == 0) return;
                onLine(cleaned);
            };

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
                if (e.Data is not null) emit(e.Data);
            };
            procOwned.ErrorDataReceived += (_, e) =>
            {
                if (e.Data is not null) emit(e.Data);
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

    /// <summary>UTF-8 without BOM (helpers / non-redirect use).</summary>
    public static Encoding GetUtf8Encoding() => new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    /// <summary>OEM code page for console redirect and sanitizer banner recovery.</summary>
    public static Encoding GetOemEncoding()
    {
        try
        {
            return Encoding.GetEncoding(CultureInfo.CurrentCulture.TextInfo.OEMCodePage);
        }
        catch
        {
            return Encoding.Default;
        }
    }

    /// <summary>Redirect decode encoding (OEM).</summary>
    public static Encoding GetConsoleEncoding() => GetOemEncoding();
}
