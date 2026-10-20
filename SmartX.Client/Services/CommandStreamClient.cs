using System.Net.Http.Json;
using SmartX.Shared.Commands;
using SmartX.Shared.Runtime;

namespace SmartX.Client.Services;

/// <summary>
/// Typed calls to Part 2 runtime and command endpoints. Failures throw HttpRequestException with a short message.
/// </summary>
public sealed class CommandStreamClient
{
    private readonly HttpClient _http;

    public CommandStreamClient(HttpClient http)
    {
        _http = http;
    }

    public Task<ProcessingStatsDto?> GetStatsAsync(CancellationToken cancellationToken) =>
        _http.GetFromJsonAsync<ProcessingStatsDto>("/api/runtime/stats", cancellationToken);

    public async Task<IReadOnlyList<LiveDeviceState>> GetDevicesAsync(CancellationToken cancellationToken)
    {
        var list = await _http.GetFromJsonAsync<List<LiveDeviceState>>("/api/runtime/devices", cancellationToken);
        return list ?? [];
    }

    public async Task<IReadOnlyList<ProcessedReading>> GetHistoryAsync(string? mac, int take, CancellationToken cancellationToken)
    {
        var path = string.IsNullOrWhiteSpace(mac)
            ? $"/api/runtime/history?take={take}"
            : $"/api/runtime/history?take={take}&mac={Uri.EscapeDataString(mac)}";
        var list = await _http.GetFromJsonAsync<List<ProcessedReading>>(path, cancellationToken);
        return list ?? [];
    }

    public async Task<IReadOnlyList<string>> GetFaultsAsync(CancellationToken cancellationToken)
    {
        var list = await _http.GetFromJsonAsync<List<string>>("/api/runtime/faults", cancellationToken);
        return list ?? [];
    }

    public async Task<IReadOnlyList<ProcessingJournalEntry>> GetJournalAsync(CancellationToken cancellationToken)
    {
        var list = await _http.GetFromJsonAsync<List<ProcessingJournalEntry>>("/api/runtime/journal", cancellationToken);
        return list ?? [];
    }

    public async Task<IReadOnlyList<RecommendationDto>> GetRecommendationsAsync(CancellationToken cancellationToken)
    {
        var list = await _http.GetFromJsonAsync<List<RecommendationDto>>("/api/runtime/recommendations", cancellationToken);
        return list ?? [];
    }

    public async Task<IReadOnlyList<CommandRecord>> GetCommandsAsync(CancellationToken cancellationToken)
    {
        var list = await _http.GetFromJsonAsync<List<CommandRecord>>("/api/commands", cancellationToken);
        return list ?? [];
    }

    public Task<CommandResult> SendCommandAsync(CommandRequest request, CancellationToken cancellationToken) =>
        PostCommandAsync("/api/commands", request, cancellationToken);

    public Task<CommandResult> UndoAsync(CancellationToken cancellationToken) =>
        PostCommandAsync("/api/commands/undo", null, cancellationToken);

    public async Task ResolveFaultAsync(string code, string mac, CancellationToken cancellationToken)
    {
        var response = await _http.PostAsJsonAsync(
            "/api/runtime/faults/resolve",
            new FaultResolveRequest { Code = code, MacAddress = mac },
            cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new HttpRequestException($"Fault resolve failed ({(int)response.StatusCode}): {Trim(body)}");
        }
    }

    private async Task<CommandResult> PostCommandAsync(string path, CommandRequest? body, CancellationToken cancellationToken)
    {
        HttpResponseMessage response = body is null
            ? await _http.PostAsync(path, null, cancellationToken)
            : await _http.PostAsJsonAsync(path, body, cancellationToken);

        CommandResult? result = null;
        try
        {
            result = await response.Content.ReadFromJsonAsync<CommandResult>(cancellationToken);
        }
        catch
        {
            // non-JSON body
        }

        if (result is not null)
        {
            return result;
        }

        var text = await response.Content.ReadAsStringAsync(cancellationToken);
        return new CommandResult
        {
            Accepted = response.IsSuccessStatusCode,
            Outcome = string.IsNullOrWhiteSpace(text)
                ? $"HTTP {(int)response.StatusCode}"
                : Trim(text)
        };
    }

    private static string Trim(string text) =>
        text.Length > 280 ? text[..280] + "…" : text;
}
