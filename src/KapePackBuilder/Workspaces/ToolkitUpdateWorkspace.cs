using System.Net.Http;
using KapePack.Core.Services;

namespace KapePackBuilder.Workspaces;

/// <summary>Check/apply toolkit updates for a bound KAPE root.</summary>
public sealed class ToolkitUpdateWorkspace
{
    private readonly IToolkitUpdateService _toolkit;

    public ToolkitUpdateWorkspace(IToolkitUpdateService? toolkit = null)
    {
        _toolkit = toolkit ?? new ToolkitUpdateService();
    }

    public Task<ToolkitCheckReport> CheckAsync(
        string kapeRoot,
        IProgress<string>? progress = null,
        CancellationToken ct = default,
        HttpMessageHandler? httpHandler = null)
        => _toolkit.CheckAsync(kapeRoot, progress, ct, httpHandler);

    public Task<ToolkitApplyResult> ApplyAsync(
        string kapeRoot,
        ToolkitApplyOptions options,
        IProgress<string>? progress = null,
        CancellationToken ct = default,
        HttpMessageHandler? httpHandler = null)
        => _toolkit.ApplyAsync(kapeRoot, options, progress, ct, httpHandler);
}
