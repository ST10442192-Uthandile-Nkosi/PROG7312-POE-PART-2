using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using SmartX.Shared.Telemetry;

namespace SmartX.Tests;

public sealed class Part1ApiContractTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public Part1ApiContractTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Existing_ingest_float_contract_still_returns_200_when_accepted()
    {
        var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/telemetry/ingest-float", new TelemetryPacket<float>
        {
            DeviceId = "24:6F:28:A1:B2:C3",
            Timestamp = DateTimeOffset.UtcNow,
            Value = 51.2f,
            MetricName = "soil_moisture",
            Unit = "%VWC"
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<TelemetryPacket<float>>();
        Assert.NotNull(body);
        Assert.Equal(51.2f, body.Value);

        var recent = await client.GetFromJsonAsync<List<TelemetrySnapshot>>("/api/telemetry/recent?take=20");
        Assert.NotNull(recent);
        Assert.Contains(recent, s => s.DeviceId == "24:6F:28:A1:B2:C3" && s.DisplayValue.StartsWith("51.20"));
    }

    [Fact]
    public async Task Runtime_stats_endpoint_exposes_queue_counters()
    {
        var client = _factory.CreateClient();
        var stats = await client.GetFromJsonAsync<ProcessingStatsDto>("/api/runtime/stats");
        Assert.NotNull(stats);
        Assert.Equal(2048, stats.QueueCapacity);
        Assert.True(stats.ProcessedCount + stats.PendingCount >= 0);
    }

    [Fact]
    public async Task Recommendations_endpoint_returns_an_array()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/api/runtime/recommendations");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var recs = await response.Content.ReadFromJsonAsync<List<RecommendationDto>>();
        Assert.NotNull(recs);
    }
}
