using KapeIR.Core.Services;
using KapeIR.Builder.Services;
using KapeIR.Builder.ViewModels;
using KapeIR.Builder.Workspaces;
using KapeIR.Ui.Scheduling;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Extensions.Logging;

namespace KapeIR.Builder.Hosting;

/// <summary>WPF composition root: DI + Serilog file/debug sinks.</summary>
public static class AppComposition
{
    public sealed record LoggingSetup(ILoggerFactory Factory, string LogFilePath);

    public static LoggingSetup CreateLogging()
    {
        Directory.CreateDirectory(AppLog.LogDirectory);
        var logFilePath = Path.Combine(AppLog.LogDirectory, "kapeir-.log");

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .Enrich.WithProperty("Application", ProductIdentity.Builder)
            .Enrich.FromLogContext()
            .WriteTo.Debug()
            .WriteTo.File(
                logFilePath,
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 30,
                shared: true,
                outputTemplate:
                "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}")
            .CreateLogger();

        // Serilog rolling: kapeir-YYYYMMDD.log
        var todayPath = Path.Combine(AppLog.LogDirectory, $"kapeir-{DateTime.Now:yyyyMMdd}.log");
        SerilogLoggerFactory? factory = null;
        try
        {
            // dispose: false — Serilog root logger is closed by App via Log.CloseAndFlush.
            factory = new SerilogLoggerFactory(Log.Logger, dispose: false);
            var setup = new LoggingSetup(factory, todayPath);
            factory = null; // ownership transferred to caller (App / tests)
            return setup;
        }
        finally
        {
            factory?.Dispose();
        }
    }

    public static ServiceProvider BuildServices(ILoggerFactory loggerFactory)
    {
        var services = new ServiceCollection();

        services.AddSingleton(loggerFactory);
        services.AddLogging(builder =>
        {
            builder.ClearProviders();
            builder.AddSerilog(Log.Logger, dispose: false);
        });

        services.AddSingleton<IBuilderDialogService, WpfBuilderDialogService>();
        services.AddSingleton<IUiScheduler, WpfUiScheduler>();
        services.AddSingleton<IPackageExporterFactory, PackageExporterFactory>();
        services.AddSingleton<IPackageBuildFacade, PackageBuildFacade>();
        services.AddSingleton<ICatalogOpsFacade, CatalogOpsFacade>();
        services.AddSingleton<IToolkitUpdateService, ToolkitUpdateService>();
        services.AddSingleton<ToolkitUpdateWorkspace>();
        services.AddSingleton(_ => AppSettings.Load());
        // Per MainViewModel instance — owns suppress-depth for checkbox re-entrancy.
        services.AddTransient<CatalogSelectionCoordinator>();
        // Per MainViewModel — catalog rebinds when the UI KAPE root changes.
        services.AddTransient<CatalogWorkspace>();

        services.AddTransient<MainViewModel>();
        services.AddTransient<MainWindow>();

        return services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateScopes = true,
            ValidateOnBuild = true
        });
    }
}
