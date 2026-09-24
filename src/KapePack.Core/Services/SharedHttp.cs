using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;

namespace KapePack.Core.Services;

/// <summary>
/// Long-lived <see cref="SocketsHttpHandler"/> + shared <see cref="HttpClient"/> for production downloads.
/// Tests pass an <see cref="HttpMessageHandler"/> override; that path returns an owned client (dispose via lease).
/// </summary>
internal static class SharedHttp
{
    private static readonly string UserAgent = BuildUserAgent();

    private static readonly SocketsHttpHandler Handler = new()
    {
        AllowAutoRedirect = true,
        AutomaticDecompression = DecompressionMethods.All,
        PooledConnectionLifetime = TimeSpan.FromMinutes(10),
    };

    /// <summary>Max among callers (EZ Tools up to 30 min). Shorter ops should cancel via <see cref="CancellationToken"/>.</summary>
    private static readonly HttpClient SharedClient = CreateClient(
        Handler,
        disposeHandler: false,
        timeout: TimeSpan.FromMinutes(30),
        userAgent: UserAgent);

    internal static string DefaultUserAgent => UserAgent;

    private static string BuildUserAgent()
    {
        var ver = Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion;
        if (string.IsNullOrWhiteSpace(ver))
            ver = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.0.0";
        var plus = ver.IndexOf('+', StringComparison.Ordinal);
        if (plus >= 0)
            ver = ver[..plus];
        return $"Kape_IR/{ver} (+https://github.com/EricZimmerman/KapeFiles)";
    }

    private static HttpClient CreateClient(
        HttpMessageHandler handler,
        bool disposeHandler,
        TimeSpan timeout,
        string userAgent)
    {
        var client = new HttpClient(handler, disposeHandler) { Timeout = timeout };
        client.DefaultRequestHeaders.UserAgent.Clear();
        client.DefaultRequestHeaders.UserAgent.ParseAdd(userAgent);
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("*/*"));
        return client;
    }

    /// <summary>
    /// Returns a lease. When <paramref name="overrideHandler"/> is null, uses the process-wide client (do not dispose the client).
    /// Otherwise wraps the override (tests) and disposes the temporary <see cref="HttpClient"/> on lease dispose (handler is not disposed).
    /// Callers should enforce shorter deadlines via linked <see cref="CancellationToken"/> (<c>CancelAfter</c>).
    /// </summary>
    public static Lease Acquire(HttpMessageHandler? overrideHandler)
    {
        if (overrideHandler is null)
            return new Lease(SharedClient, ownsClient: false);

        var client = CreateClient(
            overrideHandler,
            disposeHandler: false,
            timeout: TimeSpan.FromMinutes(30),
            userAgent: UserAgent);
        return new Lease(client, ownsClient: true);
    }

    internal readonly struct Lease : IDisposable
    {
        private readonly bool _ownsClient;
        public HttpClient Client { get; }

        public Lease(HttpClient client, bool ownsClient)
        {
            Client = client;
            _ownsClient = ownsClient;
        }

        public void Dispose()
        {
            if (_ownsClient)
                Client.Dispose();
        }
    }
}
