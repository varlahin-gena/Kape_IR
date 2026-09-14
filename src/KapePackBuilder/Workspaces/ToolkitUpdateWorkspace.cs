using System.Net.Http;
using KapePack.Core.Services;

namespace KapePackBuilder.Workspaces;

/// <summary>Check/apply toolkit updates for a bound KAPE root.</summary>
public sealed class ToolkitUpdateWorkspace
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
