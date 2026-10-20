using System.Collections.Concurrent;
using SmartX.Shared.Sensors;

namespace SmartX.Api.Services;

public sealed class SensorRegistry
{
    private readonly ConcurrentDictionary<string, SensorProfile> _sensors = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyCollection<SensorProfile> All() =>
        _sensors.Values.OrderBy(s => s.Location).ThenBy(s => s.MacAddress).ToList();

    public SensorProfile? Get(string mac) =>
        _sensors.TryGetValue(NormalizeMac(mac), out var profile) ? profile : null;

    public SensorProfile Register(SensorRegistrationRequest request)
    {
        var mac = NormalizeMac(request.MacAddress);
        if (string.IsNullOrWhiteSpace(mac) || mac.Length < 11)
        {
            throw new InvalidOperationException("MAC / unique identifier is required (ESP32 STA MAC, e.g. 24:6F:28:A1:B2:C3).");
        }

        var profile = new SensorProfile
        {
            MacAddress = mac,
            DisplayName = string.IsNullOrWhiteSpace(request.DisplayName) ? mac : request.DisplayName.Trim(),
            Location = request.Location.Trim(),
            Category = request.Category,
            Firmware = request.Firmware,
            RegisteredAt = DateTimeOffset.UtcNow
        };

        _sensors.AddOrUpdate(mac, profile, (_, existing) =>
        {
            profile.Attachments = existing.Attachments;
            return profile;
        });

        return profile;
    }

    public void AddAttachment(string mac, SensorAttachment attachment)
    {
        var profile = Get(mac) ?? throw new InvalidOperationException($"Unknown sensor {mac}.");
        profile.Attachments.Add(attachment);
    }

    public void Seed(IEnumerable<SensorProfile> profiles)
    {
        foreach (var profile in profiles)
        {
            _sensors[NormalizeMac(profile.MacAddress)] = profile;
        }
    }

    public static string NormalizeMac(string mac) =>
        mac.Trim().Replace('-', ':').ToUpperInvariant();
}
