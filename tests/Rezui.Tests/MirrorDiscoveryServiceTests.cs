using System.Net;
using Rezui.Services;
using Xunit;

namespace Rezui.Tests;

public sealed class MirrorDiscoveryServiceTests
{
    [Fact]
    public async Task ProbeDistinguishesReachableAndServerFailureResponses()
    {
        using var handler = new ProbeHandler();
        using var httpClient = new HttpClient(handler);
        using var service = new MirrorDiscoveryService(httpClient);

        var results = await service.ProbeAsync(
            new[]
            {
                "reachable.example.com",
                "failing.example.com"
            },
            TestContext.Current.CancellationToken);

        var reachable = Assert.Single(results, item => item.DisplayName == "reachable.example.com");
        Assert.True(reachable.IsAvailable);
        Assert.NotNull(reachable.LatencyMilliseconds);

        var failing = Assert.Single(results, item => item.DisplayName == "failing.example.com");
        Assert.False(failing.IsAvailable);
        Assert.Null(failing.LatencyMilliseconds);
    }

    private sealed class ProbeHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var status = request.RequestUri?.Host == "reachable.example.com"
                ? HttpStatusCode.NoContent
                : HttpStatusCode.ServiceUnavailable;
            return Task.FromResult(new HttpResponseMessage(status));
        }
    }
}
