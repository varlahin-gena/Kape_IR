using System.Text;
using KapeIR.Core.Models;

namespace KapeIR.Core.Services;

/// <summary>Generates run_collection.bat / .ps1 and fleet <c>_kape.cli</c> for exported packages.</summary>
public static class PackageScriptRenderer
{
    /// <summary>UTF-8 with BOM — needed so cmd.exe + chcp 65001 / PowerShell show Russian correctly.</summary>
    public static Encoding BatEncoding { get; } = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true);

    public static string RenderRunBat(PackageDefinition pkg, string kapeRel = ".\\kape.exe")
    {
        // Thin wrapper: all args live in PowerShell where "!" in names is safe.
        _ = kapeRel;
        _ = pkg;
        return string.Join("\r\n", new[]
        {
            "@echo off",
            "setlocal",
            "chcp 65001 >nul",
            "cd /d \"%~dp0\"",
            "echo [*] Запуск через PowerShell…",
            "powershell.exe -NoProfile -ExecutionPolicy Bypass -File \"%~dp0run_collection.ps1\"",
            "set \"KAPE_ERR=%ERRORLEVEL%\"",
            "if not \"%KAPE_ERR%\"==\"0\" echo [!] Код выхода: %KAPE_ERR%",
            "pause",
            ""
        });
    }

    public static string RenderKapeCli(PackageDefinition pkg)
    {
        if (!pkg.IsTwoPhase)
            return KapeCliArgs.RenderCliLine(KapeCliArgs.FromPackage(pkg, fleetCliVars: true)) + "\r\n";

        var manifest = CollectionPlan.FromPackage(pkg);
        var phases = CollectionPlan.BuildPhases(manifest, new CollectionPlan.RuntimeOptions(pkg.Tsource));
        var sb = new StringBuilder();
        sb.AppendLine("# two_phase IR — KapeIR.Triage runs both phases; fleet may use one line at a time");
        foreach (var phase in phases)
        {
            var fleetOpts = phase.Options with { FleetCliVars = true };
            // Rewrite paths for fleet %%d / %%m
            if (phase.Name == "1")
            {
                fleetOpts = fleetOpts with
                {
                    Mdest = @"%%d\RESULTS\%%m\Phase1_Volatile",
                    FleetCliVars = true
                };
            }
            else if (phase.Name == "2")
            {
                fleetOpts = fleetOpts with
                {
                    Tdest = @"%%d\RESULTS\%%m\Phase2_Disk",
                    Mdest = @"%%d\RESULTS\%%m\Phase2_Disk\ModuleOutput",
                    FleetCliVars = true
                };
            }

            sb.AppendLine("# " + phase.Label);
            sb.AppendLine(KapeCliArgs.RenderCliLine(fleetOpts));
        }

        return sb.ToString();
    }

    public static string RenderRunPs1(PackageDefinition pkg)
    {
        if (pkg.IsTwoPhase)
            return RenderRunPs1TwoPhase(pkg);

        var module = pkg.ModuleCompoundName;
        if (module is null && pkg.Modules.Count > 0)
            module = string.Join(",", pkg.Modules.Select(m => Path.GetFileNameWithoutExtension(m.Path)));

        var args = KapeCliArgs.Build(new KapeCliArgs.Options(
            pkg.Tsource,
            pkg.TargetCompoundName,
            module,
            pkg.ZipOutput,
            pkg.Flush,
            pkg.Vss,
            FleetCliVars: false));

        var argLiteral = string.Join(", ", args.Select(a => $"'{a.Replace("'", "''")}'"));
        return string.Join("\r\n", new[]
        {
            "$ErrorActionPreference = 'Continue'",
            "try { [Console]::OutputEncoding = [System.Text.UTF8Encoding]::new($false) } catch {}",
            "Set-Location -LiteralPath $PSScriptRoot",
            $"Write-Host '[*] Package: {pkg.Name}'",
            $"Write-Host '[*] Target:  {pkg.TargetCompoundName}'",
            "$kape = Join-Path $PSScriptRoot 'kape.exe'",
            "if (-not (Test-Path -LiteralPath $kape)) {",
            "  Write-Host '[!] kape.exe не найден рядом со скриптом' -ForegroundColor Red",
            "  exit 2",
            "}",
            // Active _kape.cli forces KAPE batch mode and ignores @kapeArgs.
            "$cli = Join-Path $PSScriptRoot '_kape.cli'",
            "$cliHold = $cli + '.packhold'",
            "if (Test-Path -LiteralPath $cli) {",
            "  if (Test-Path -LiteralPath $cliHold) { Remove-Item -LiteralPath $cliHold -Force }",
            "  Move-Item -LiteralPath $cli -Destination $cliHold -Force",
            "  Write-Host '[i] _kape.cli временно отключён (иначе batch mode)'",
            "}",
            "$code = 0",
            "try {",
            $"  $kapeArgs = @({argLiteral})",
            "  Write-Host ('[*] Command: kape.exe ' + ($kapeArgs -join ' '))",
            "  & $kape @kapeArgs",
            "  $code = $LASTEXITCODE",
            "} finally {",
            "  if (Test-Path -LiteralPath $cliHold) {",
            "    if (Test-Path -LiteralPath $cli) { Remove-Item -LiteralPath $cli -Force }",
            "    Move-Item -LiteralPath $cliHold -Destination $cli -Force",
            "  }",
            "}",
            "$results = Join-Path $PSScriptRoot ('RESULTS\\' + $env:COMPUTERNAME)",
            "if (Test-Path -LiteralPath $results) {",
            "  Write-Host \"[*] Готово. Результаты: $results\" -ForegroundColor Green",
            "} else {",
            "  Write-Host '[!] Папка RESULTS не создана — сбор не выполнен или упал.' -ForegroundColor Red",
            "  if ($code -eq 0) { $code = 1 }",
            "}",
            "exit $code",
            ""
        });
    }

    private static string RenderRunPs1TwoPhase(PackageDefinition pkg)
    {
        var manifest = CollectionPlan.FromPackage(pkg);
        var phases = CollectionPlan.BuildPhases(manifest, new CollectionPlan.RuntimeOptions(pkg.Tsource));
        var lines = new List<string>
        {
            "$ErrorActionPreference = 'Continue'",
            "try { [Console]::OutputEncoding = [System.Text.UTF8Encoding]::new($false) } catch {}",
            "Set-Location -LiteralPath $PSScriptRoot",
            $"Write-Host '[*] Package: {pkg.Name} (two_phase)'",
            "$kape = Join-Path $PSScriptRoot 'kape.exe'",
            "if (-not (Test-Path -LiteralPath $kape)) {",
            "  Write-Host '[!] kape.exe не найден рядом со скриптом' -ForegroundColor Red",
            "  exit 2",
            "}",
            "$cli = Join-Path $PSScriptRoot '_kape.cli'",
            "$cliHold = $cli + '.packhold'",
            "if (Test-Path -LiteralPath $cli) {",
            "  if (Test-Path -LiteralPath $cliHold) { Remove-Item -LiteralPath $cliHold -Force }",
            "  Move-Item -LiteralPath $cli -Destination $cliHold -Force",
            "  Write-Host '[i] _kape.cli временно отключён (иначе batch mode)'",
            "}",
            "$code = 0",
            "try {"
        };

        foreach (var phase in phases)
        {
            var argLiteral = string.Join(", ",
                KapeCliArgs.Build(phase.Options).Select(a => $"'{a.Replace("'", "''")}'"));
            lines.Add($"  Write-Host '[*] {phase.Label.Replace("'", "''")}'");
            lines.Add($"  $kapeArgs = @({argLiteral})");
            lines.Add("  Write-Host ('[*] Command: kape.exe ' + ($kapeArgs -join ' '))");
            lines.Add("  & $kape @kapeArgs");
            lines.Add("  if ($LASTEXITCODE -ne 0) { $code = $LASTEXITCODE }");
        }

        lines.AddRange(new[]
        {
            "} finally {",
            "  if (Test-Path -LiteralPath $cliHold) {",
            "    if (Test-Path -LiteralPath $cli) { Remove-Item -LiteralPath $cli -Force }",
            "    Move-Item -LiteralPath $cliHold -Destination $cli -Force",
            "  }",
            "}",
            "$results = Join-Path $PSScriptRoot ('RESULTS\\' + $env:COMPUTERNAME)",
            "if (Test-Path -LiteralPath $results) {",
            "  Write-Host \"[*] Готово. Результаты: $results\" -ForegroundColor Green",
            "} else {",
            "  Write-Host '[!] Папка RESULTS не создана — сбор не выполнен или упал.' -ForegroundColor Red",
            "  if ($code -eq 0) { $code = 1 }",
            "}",
            "exit $code",
            ""
        });
        return string.Join("\r\n", lines);
    }
}
