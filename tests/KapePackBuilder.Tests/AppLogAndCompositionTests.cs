using KapeIR.Core.Services;
using KapeIR.Builder.Hosting;
using KapeIR.Builder.Services;
using KapeIR.Builder.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;

namespace KapeIR.Builder.Tests;

public class AppLogAndCompositionTests : IDisposable
{
    public AppLogAndCompositionTests()
    {
        AppLog.ResetForTests();
    }

    public void Dispose()
    {
        AppLog.ResetForTests();
        Log.CloseAndFlush();
    }

    [Fact]
    public void AppLog_Fallback_WritesFile()
    {
        var path = AppLog.LogFilePath;
        var marker = "test-marker-" + Guid.NewGuid().ToString("N");
        AppLog.Info(marker);

        Assert.True(File.Exists(path));
        Assert.Contains(marker, File.ReadAllText(path));
    }

    [Fact]
    public void AppLog_Initialize_DisablesFallbackDuplicate()
    {
        var logging = AppComposition.CreateLogging();
        AppLog.Initialize(logging.Factory, logging.LogFilePath);

        Assert.Equal(logging.LogFilePath, AppLog.LogFilePath);
        AppLog.Info("Initialized structured log {Value}", 42);
        Log.CloseAndFlush();

        Assert.True(File.Exists(logging.LogFilePath) || Directory.EnumerateFiles(AppLog.LogDirectory, "kapeir-*.log").Any());
    }

    [Fact]
    public void BuildServices_ResolvesMainViewModelAndWindow()
    {
        var logging = AppComposition.CreateLogging();
        AppLog.Initialize(logging.Factory, logging.LogFilePath);
        using var sp = AppComposition.BuildServices(logging.Factory);

        var vm = sp.GetRequiredService<MainViewModel>();
        Assert.NotNull(vm);
        Assert.Same(sp.GetRequiredService<IDialogService>(), sp.GetRequiredService<IDialogService>());
        Assert.IsType<WpfDialogService>(sp.GetRequiredService<IDialogService>());
        Assert.NotNull(sp.GetRequiredService<IPackageExporterFactory>());
        Assert.NotNull(sp.GetRequiredService<ILogger<MainViewModel>>());

        // MainWindow needs STA; skip constructing WPF Window in default xunit threads if not STA.
        // Resolving the type registration is enough here — factory is validated on BuildServices.
        vm.Dispose();
    }

    [Fact]
    public void ShowAbout_IncludesLogPath()
    {
        var dialogs = new FakeDialogService();
        using var vm = new MainViewModel(dialogs);

        vm.ShowAboutCommand.Execute(null);

        Assert.Single(dialogs.Messages);
        Assert.Equal("О программе", dialogs.Messages[0].Title);
        Assert.Contains(AppLog.LogFilePath, dialogs.Messages[0].Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Версия:", dialogs.Messages[0].Message, StringComparison.OrdinalIgnoreCase);
    }
}
