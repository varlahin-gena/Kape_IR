using System.Net.Http;
using KapePack.Core.Models;

namespace KapePack.Core.Services;

/// <summary>Check/apply KapeFiles + EZ Tools + Chainsaw updates for a KAPE root.</summary>
public interface IToolkitUpdateService
{
    Task<ToolkitCheckReport> CheckAsync(
        string kapeRoot,
        IProgress<string>? progress = null,
        CancellationToken ct = default,
        HttpMessageHandler? httpHandler = null);

    Task<ToolkitApplyResult> ApplyAsync(
        string kapeRoot,
        ToolkitApplyOptions options,
        IProgress<string>? progress = null,
        CancellationToken ct = default,
        HttpMessageHandler? httpHandler = null);
}

/// <summary>Default implementation — delegates to <see cref="ToolkitUpdateCoordinator"/>.</summary>
public sealed class ToolkitUpdateService : IToolkitUpdateService
{
    public Task<ToolkitCheckReport> CheckAsync(
        string kapeRoot,
        IProgress<string>? progress = null,
        CancellationToken ct = default,
        HttpMessageHandler? httpHandler = null)
        => ToolkitUpdateCoordinator.CheckAsync(kapeRoot, progress, ct, httpHandler);

    public Task<ToolkitApplyResult> ApplyAsync(
        string kapeRoot,
        ToolkitApplyOptions options,
        IProgress<string>? progress = null,
        CancellationToken ct = default,
        HttpMessageHandler? httpHandler = null)
        => ToolkitUpdateCoordinator.ApplyAsync(kapeRoot, options, progress, ct, httpHandler);
}
