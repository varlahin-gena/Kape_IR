namespace KapePack.Core.Services;

/// <summary>CLI contract for CollectPack / KapePackRunner (silent + GUI).</summary>
public sealed class CollectPackCliOptions
{
    public bool Silent { get; init; }
    public bool ShowHelp { get; init; }
    public bool SimOnly { get; init; }
    public string? Tsource { get; init; }
    public string? LogPath { get; init; }
    public string? CaseId { get; init; }
    /// <summary>1, 2, or null (all).</summary>
    public int? Phase { get; init; }
    public bool SkipMemory { get; init; }
    /// <summary>Require sibling CollectPack.exe.sha256 and match before collect.</summary>
    public bool VerifySha256 { get; init; }
    public List<string> Errors { get; init; } = new();

    public static CollectPackCliOptions Parse(string[] args)
    {
        var errors = new List<string>();
        var silent = false;
        var help = false;
        var simOnly = false;
        var skipMemory = false;
        var verify = false;
        string? tsource = null;
        string? log = null;
        string? caseId = null;
        int? phase = null;

        for (var i = 0; i < args.Length; i++)
        {
            var a = args[i];
            if (a is "-h" or "--help" or "/?")
            {
                help = true;
                continue;
            }

            if (a is "--silent" or "-s" or "/silent")
            {
                silent = true;
                continue;
            }

            if (a is "--sim-only" or "--sim")
            {
                simOnly = true;
                continue;
            }

            if (a is "--skip-memory" or "--no-memory")
            {
                skipMemory = true;
                continue;
            }

            if (a is "--verify" or "--verify-sha" or "--verify-sha256")
            {
                verify = true;
                continue;
            }

            if (a is "--tsource" or "-t")
            {
                if (i + 1 >= args.Length)
                    errors.Add("--tsource требует значение (например C:)");
                else
                    tsource = args[++i];
                continue;
            }

            if (a is "--log" or "-l")
            {
                if (i + 1 >= args.Length)
                    errors.Add("--log требует путь к файлу");
                else
                    log = args[++i];
                continue;
            }

            if (a is "--case-id" or "--case")
            {
                if (i + 1 >= args.Length)
                    errors.Add("--case-id требует значение");
                else
                    caseId = args[++i];
                continue;
            }

            if (a is "--phase" or "-p")
            {
                if (i + 1 >= args.Length)
                    errors.Add("--phase требует 1 или 2");
                else if (!int.TryParse(args[++i], out var ph) || ph is not (1 or 2))
                    errors.Add("--phase должен быть 1 или 2");
                else
                    phase = ph;
                continue;
            }

            if (a.StartsWith("--tsource=", StringComparison.OrdinalIgnoreCase))
            {
                tsource = a["--tsource=".Length..];
                continue;
            }

            if (a.StartsWith("--log=", StringComparison.OrdinalIgnoreCase))
            {
                log = a["--log=".Length..];
                continue;
            }

            if (a.StartsWith("--case-id=", StringComparison.OrdinalIgnoreCase))
            {
                caseId = a["--case-id=".Length..];
                continue;
            }

            if (a.StartsWith("--phase=", StringComparison.OrdinalIgnoreCase))
            {
                if (!int.TryParse(a["--phase=".Length..], out var ph) || ph is not (1 or 2))
                    errors.Add("--phase должен быть 1 или 2");
                else
                    phase = ph;
                continue;
            }

            errors.Add("Неизвестный аргумент: " + a);
        }

        return new CollectPackCliOptions
        {
            Silent = silent || simOnly,
            ShowHelp = help,
            SimOnly = simOnly,
            Tsource = tsource,
            LogPath = log,
            CaseId = caseId,
            Phase = phase,
            SkipMemory = skipMemory,
            VerifySha256 = verify,
            Errors = errors
        };
    }

    public static string HelpText =>
        """
        KAPE Pack Runner

          GUI (по умолчанию):
            CollectPack.exe

          Silent / EDR:
            CollectPack.exe --silent --tsource C:
            CollectPack.exe --silent --tsource C: --log C:\Windows\Temp\kape_pack.log
            CollectPack.exe --sim-only --tsource C:
            CollectPack.exe --silent --tsource C: --verify

          Двухфазный IR (package.json collection_mode=two_phase):
            CollectPack.exe --silent --tsource C: --case-id IR-2026-001
            CollectPack.exe --silent --tsource C: --phase 1
            CollectPack.exe --silent --tsource C: --phase 2 --skip-memory

          Параметры:
            --silent, -s     Без GUI: распаковка + kape.exe + код выхода
            --sim-only       Только оценка KAPE --sim (без копирования); подразумевает silent
            --tsource, -t    Источник (обязателен в silent; по умолчанию из package.json в GUI)
            --log, -l        Файл журнала (silent; иначе рядом с EXE)
            --case-id        ID дела для chain_of_custody.txt
            --phase 1|2      Только фаза 1 (volatile) или 2 (disk)
            --skip-memory    Фаза 1 без WinPmem (VolatileFirst_NoMemory)
            --verify         Требовать и проверить CollectPack.exe.sha256 перед сбором
            --help, -h       Эта справка

          Коды выхода: 0=OK, 1=сбой сбора, 2=аргументы/payload/verify, 3=ошибка подготовки
        """;
}
