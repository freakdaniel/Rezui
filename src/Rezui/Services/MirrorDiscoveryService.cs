using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using HdRezka;
using Serilog;

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

    Task<bool> IsRezkaMirrorAsync(
        string origin,
        CancellationToken cancellationToken = default);
}

public sealed class MirrorDiscoveryService : IMirrorDiscoveryService, IDisposable
{
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan ValidationTimeout = TimeSpan.FromSeconds(10);
    private const string ValidationLogin = "test@test.com";
    private const string ValidationPassword = "Testpass123!";
    private readonly HttpClient _httpClient;
    private readonly bool _ownsHttpClient;
    private readonly ILogger _logger;

    public MirrorDiscoveryService(
        HttpClient? httpClient = null,
        ILogger? logger = null)
    {
        _logger = logger ?? Log.ForContext<MirrorDiscoveryService>();
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
        var distinctOrigins = origins
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        _logger.Information("Probing {MirrorCount} HDRezka mirrors", distinctOrigins.Length);
        var probes = distinctOrigins
            .Select(origin => ProbeOneAsync(origin, cancellationToken));
        var results = await Task.WhenAll(probes);
        _logger.Information(
            "Mirror probing completed; available={AvailableCount}, unavailable={UnavailableCount}",
            results.Count(result => result.IsAvailable),
            results.Count(result => !result.IsAvailable));
        return results;
    }

    public async Task<bool> IsRezkaMirrorAsync(
        string origin,
        CancellationToken cancellationToken = default)
    {
        Uri normalized;
        try
        {
            normalized = RezkaClientService.NormalizeOrigin(origin);
        }
        catch (ArgumentException)
        {
            _logger.Debug("Mirror validation rejected malformed origin {Origin}", origin);
            return false;
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(ValidationTimeout);
        using var validationClient = new Client(normalized.AbsoluteUri.TrimEnd('/'));
        try
        {
            var authentication = await validationClient.LoginAsync(
                ValidationLogin,
                ValidationPassword,
                rememberMe: false,
                timeout.Token);
            return authentication.IsAuthenticated;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.Debug(exception, "Mirror validation failed for {OriginHost}", normalized.Host);
            return false;
        }
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

        var startedAt = Stopwatch.GetTimestamp();
        try
        {
            using var response = await _httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                timeout.Token);
            var available = response.IsSuccessStatusCode;
            var elapsed = Stopwatch.GetElapsedTime(startedAt);
            _logger.Debug(
                "Mirror probe {ProbeOutcome} for {OriginHost} in {DurationMs:0.0} ms with status {StatusCode}",
                available ? "succeeded" : "failed",
                normalized.Host,
                elapsed.TotalMilliseconds,
                (int)response.StatusCode);
            return new MirrorProbeResult(
                normalizedOrigin,
                normalized.Host,
                available ? Math.Max(1, (long)elapsed.TotalMilliseconds) : null,
                available);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.Debug("Mirror probe timed out for {OriginHost}", normalized.Host);
            return Unavailable(normalizedOrigin, normalized.Host);
        }
        catch (HttpRequestException exception)
        {
            _logger.Debug(exception, "Mirror probe failed for {OriginHost}", normalized.Host);
            return Unavailable(normalizedOrigin, normalized.Host);
        }
    }

    private static MirrorProbeResult Unavailable(string origin, string displayName) =>
        new(origin, displayName, null, false);
}
