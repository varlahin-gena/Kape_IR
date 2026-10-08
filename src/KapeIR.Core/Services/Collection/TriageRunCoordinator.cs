using KapeIR.Core.Models;

namespace KapeIR.Core.Services;

/// <summary>
/// Shared Triage prepare → collect → wrap-up pipeline for GUI and silent hosts.
/// UI hosts own progress/dialogs; this type owns verify, unpack, kape orchestration, exit mapping.
/// </summary>
public sealed class TriageRunCoordinator
{
    /// <summary>Prepared CollectPack ready for <see cref="RunAsync"/>.</summary>
    public sealed record Session(
        string ExePath,
        string PackageDir,
        string KapeExe,
        CollectionPlan.LaunchManifest Manifest,
        string? LaunchDir);

    public sealed record PrepareOptions(
        string ExePath,
        string? TsourceOverride = null,
        bool RequireTsource = false,
        /// <summary>When true, sidecar must exist and match (CLI --verify).</summary>
        bool RequireSha256 = false,
        /// <summary>When true and &lt;exe&gt;.sha256 exists, verify it (GUI / silent default).</summary>
        bool VerifyIfSidecarPresent = true);

    public sealed record PrepareResult(
        int ExitCode,
        string Message,
        Session? Session = null);

    public sealed record RunOptions(
        Session Session,
        CollectionPlan.RuntimeOptions Runtime,
        /// <summary>
        /// Optional process host. Null → <see cref="CollectionRunner.StartKapeProcessAsync"/>.
        /// GUI supplies a wrapper that also drives progress UI.
        /// </summary>
        Func<string, List<string>, string, CancellationToken, Task<int>>? RunKape = null);

    public sealed record RunResult(
        int ExitCode,
        string? ResultsDir,
        IReadOnlyList<string> PhaseSummaries,
        bool Simulate,
        /// <summary>Short operator-facing status (Russian, matches prior Runner copy).</summary>
        string StatusMessage,
        /// <summary>True when RESULTS folder exists after a non-sim run.</summary>
        bool HasResultsDirectory);

    /// <summary>Verify sidecar (optional) + unpack KAPEPACK + read package.json.</summary>
    public PrepareResult Prepare(
        PrepareOptions options,
        Action<string>? log = null,
        CancellationToken cancellationToken = default)
    {
        var exe = options.ExePath?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(exe) || !File.Exists(exe))
            return new PrepareResult(3, "Не удалось определить путь к EXE.");

        cancellationToken.ThrowIfCancellationRequested();

        var sidecar = exe + ".sha256";
        var shouldVerify = options.RequireSha256 ||
                           (options.VerifyIfSidecarPresent && File.Exists(sidecar));
        if (shouldVerify)
        {
            // Large field packs: hashing the whole EXE can take minutes — surface progress.
            log?.Invoke("Проверка SHA256…");
            var hashProgress = new Progress<double>(pct =>
                log?.Invoke($"Проверка SHA256… {pct:0}%"));
            if (!FileHash.TryVerifySidecar(
                    exe,
                    out var verifyMsg,
                    requireSidecar: options.RequireSha256,
                    progress: hashProgress,
                    cancellationToken: cancellationToken))
                return new PrepareResult(2, verifyMsg);
            log?.Invoke(verifyMsg);
        }

        log?.Invoke("Распаковка пакета…");
        var prep = CollectPackPrepare.Prepare(
            exe,
            tsourceOverride: options.TsourceOverride,
            requireTsource: options.RequireTsource,
            log: log,
            ct: cancellationToken);

        if (prep.ExitCode != 0)
            return new PrepareResult(prep.ExitCode, prep.Message);

        return new PrepareResult(
            0,
            "OK",
            new Session(
                exe,
                prep.PackageDir!,
                prep.KapeExe!,
                prep.Manifest!,
                prep.LaunchDir));
    }

    /// <summary>Run single- or two-phase collection via <see cref="CollectionRunner"/>.</summary>
    public async Task<RunResult> RunAsync(
        RunOptions options,
        Action<string> log,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(log);

        var session = options.Session;
        var rt = options.Runtime;
        var cfg = session.Manifest;

        log(rt.Simulate ? "Режим: оценка объёма (без копирования)" : "Режим: полный сбор");
        if (cfg.CollectionMode == IrCollectionMode.TwoPhase)
        {
            var phaseLabel = rt.PhaseFilter switch
            {
                1 => "только фаза 1",
                2 => "только фаза 2",
                _ => "обе фазы"
            };
            var memoryLabel = rt.SkipMemory ? "без дампа памяти" : "с дампом памяти";
            log($"Двухфазный сбор ({phaseLabel}, {memoryLabel})");
        }

        var runKape = options.RunKape
                      ?? ((exe, args, wd, ct) =>
                          CollectionRunner.StartKapeProcessAsync(exe, args, wd, log, cancellationToken: ct));

        var result = await CollectionRunner.RunAsync(
            session.PackageDir,
            session.KapeExe,
            cfg,
            rt,
            session.ExePath,
            log,
            runKape,
            cancellationToken).ConfigureAwait(false);

        return Interpret(result, rt.Simulate);
    }

    /// <summary>Map <see cref="CollectionRunner.Result"/> to operator status + exit code.</summary>
    public static RunResult Interpret(CollectionRunner.Result result, bool simulate)
    {
        var hasResults = result.ResultsDir is not null &&
                         Directory.Exists(result.ResultsDir);

        string status;
        if (simulate)
        {
            status = result.ExitCode == 0
                ? "Оценка объёма завершена. Файлы не копировались."
                : $"Оценка объёма завершилась с кодом {result.ExitCode}.";
        }
        else if (hasResults)
        {
            status = result.ExitCode == 0
                ? $"Готово. Папка RESULTS: {result.ResultsDir}"
                : $"Готово с ошибками (код {result.ExitCode}). Папка RESULTS: {result.ResultsDir}";
        }
        else
        {
            status = result.ExitCode == 0
                ? "Папка RESULTS не создана — сбор не выполнен."
                : $"Ошибка kape.exe (код {result.ExitCode}).";
        }

        return new RunResult(
            result.ExitCode,
            result.ResultsDir,
            result.PhaseSummaries,
            simulate,
            status,
            hasResults);
    }

    /// <summary>
    /// Build <see cref="CollectionPlan.RuntimeOptions"/> from GUI / CLI flags.
    /// Phase filter: null = all; 1 / 2 = single phase (two_phase packs only).
    /// </summary>
    public static CollectionPlan.RuntimeOptions BuildRuntime(
        string tsource,
        bool simulate = false,
        int? phaseFilter = null,
        bool skipMemory = false,
        string? caseIdOverride = null,
        string? resultsRoot = null)
        => new(
            tsource,
            Simulate: simulate,
            PhaseFilter: phaseFilter,
            SkipMemory: skipMemory,
            CaseIdOverride: caseIdOverride,
            ResultsRoot: resultsRoot);

    /// <summary>Default RESULTS path suggestion next to the unpacked package.</summary>
    public static string DefaultResultsRoot(string packageDir)
        => Path.Combine(packageDir, "RESULTS", Environment.MachineName);
}
