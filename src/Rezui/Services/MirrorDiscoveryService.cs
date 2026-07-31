using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;

namespace Rezui.Services;

public sealed record MirrorProbeResult(
    string Origin,
    string DisplayName,
    long? LatencyMilliseconds,
    bool IsAvailable);

public interface IMirrorDiscoveryService
{
    Task<IReadOnlyList<MirrorProbeResult>> ProbeAsync(
        IEnumerable<string> origins,
        CancellationToken cancellationToken = default);
}

public sealed class MirrorDiscoveryService : IMirrorDiscoveryService, IDisposable
{
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(5);
    private readonly HttpClient _httpClient;
    private readonly bool _ownsHttpClient;

    public MirrorDiscoveryService(HttpClient? httpClient = null)
    {
        _ownsHttpClient = httpClient is null;
        _httpClient = httpClient ?? new HttpClient(
            new HttpClientHandler
            {
                AllowAutoRedirect = true,
                AutomaticDecompression = DecompressionMethods.All
            });
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(
            "Mozilla/5.0 (Rezui mirror probe)");
    }

    public async Task<IReadOnlyList<MirrorProbeResult>> ProbeAsync(
        IEnumerable<string> origins,
        CancellationToken cancellationToken = default)
    {
        var probes = origins
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(origin => ProbeOneAsync(origin, cancellationToken));
        return await Task.WhenAll(probes);
    }

    public void Dispose()
    {
        if (_ownsHttpClient)
        {
            _httpClient.Dispose();
        }
    }

    private async Task<MirrorProbeResult> ProbeOneAsync(
        string origin,
        CancellationToken cancellationToken)
    {
        Uri normalized;
        try
        {
            normalized = RezkaClientService.NormalizeOrigin(origin);
        }
        catch (ArgumentException)
        {
            return Unavailable(origin, origin.Trim());
        }

        var normalizedOrigin = normalized.AbsoluteUri.TrimEnd('/');
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(ProbeTimeout);
        using var request = new HttpRequestMessage(HttpMethod.Get, normalized);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/html"));
        request.Headers.Range = new RangeHeaderValue(0, 0);

        var stopwatch = Stopwatch.StartNew();
        try
        {
            using var response = await _httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                timeout.Token);
            stopwatch.Stop();
            var available = response.IsSuccessStatusCode;
            return new MirrorProbeResult(
                normalizedOrigin,
                normalized.Host,
                available ? Math.Max(1, stopwatch.ElapsedMilliseconds) : null,
                available);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return Unavailable(normalizedOrigin, normalized.Host);
        }
        catch (HttpRequestException)
        {
            return Unavailable(normalizedOrigin, normalized.Host);
        }
    }

    private static MirrorProbeResult Unavailable(string origin, string displayName) =>
        new(origin, displayName, null, false);
}
