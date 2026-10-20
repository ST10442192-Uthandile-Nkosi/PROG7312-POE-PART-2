namespace SmartX.Shared.Sensors;

public sealed class SensorProfile
{
    public string MacAddress { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Location { get; set; } = string.Empty;
    public SensorCategory Category { get; set; }
    public string Firmware { get; set; } = "ESP32-WROOM-32 2.0.14";
    public string ChipModel { get; set; } = "ESP32";
    public DateTimeOffset RegisteredAt { get; set; } = DateTimeOffset.UtcNow;
    public List<SensorAttachment> Attachments { get; set; } = [];
}

public sealed class SensorAttachment
{
    public string FileName { get; set; } = string.Empty;
    public string StoredName { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public DateTimeOffset UploadedAt { get; set; } = DateTimeOffset.UtcNow;
    public bool Encrypted { get; set; }
    public string Algorithm { get; set; } = string.Empty;
    public string ContentSha256 { get; set; } = string.Empty;
}

public sealed class SensorRegistrationRequest
{
    public string MacAddress { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Location { get; set; } = string.Empty;
    public SensorCategory Category { get; set; }
    public string Firmware { get; set; } = "ESP32-WROOM-32 2.0.14";
}
