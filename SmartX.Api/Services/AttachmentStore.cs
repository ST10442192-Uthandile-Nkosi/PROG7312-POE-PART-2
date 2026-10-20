using System.Security.Cryptography;
using SmartX.Shared.Sensors;

namespace SmartX.Api.Services;

public sealed class AttachmentStore
{
    private readonly string _root;
    private readonly byte[] _key; // AES-256 key from appsettings

    public AttachmentStore(IWebHostEnvironment environment, IConfiguration configuration)
    {
        _root = Path.Combine(environment.ContentRootPath, "Uploads");
        Directory.CreateDirectory(_root);
        var key = configuration["AttachmentEncryption:Key"]
                  ?? throw new InvalidOperationException("AttachmentEncryption:Key is missing.");
        _key = Convert.FromBase64String(key);
        if (_key.Length is not 16 and not 24 and not 32)
        {
            throw new InvalidOperationException("Attachment encryption key must be 16, 24, or 32 bytes.");
        }
    }

    public async Task<SensorAttachment> SaveAsync(string mac, IFormFile file, CancellationToken cancellationToken)
    {
        if (file.Length == 0)
        {
            throw new InvalidOperationException("Empty file.");
        }

        var safeMac = SensorRegistry.NormalizeMac(mac).Replace(':', '-');
        var folder = Path.Combine(_root, safeMac);
        Directory.CreateDirectory(folder);

        await using var incoming = file.OpenReadStream();
        using var buffer = new MemoryStream();
        await incoming.CopyToAsync(buffer, cancellationToken);
        var plain = buffer.ToArray();
        var hash = Convert.ToHexString(SHA256.HashData(plain));

        // Ciphertext is IV + AES-CBC payload.

        var stored = $"{Guid.NewGuid():N}.aes";
        var path = Path.Combine(folder, stored);
        await EncryptAsync(plain, path, cancellationToken);

        return new SensorAttachment
        {
            FileName = file.FileName,
            StoredName = stored,
            ContentType = file.ContentType,
            SizeBytes = file.Length,
            UploadedAt = DateTimeOffset.UtcNow,
            Encrypted = true,
            Algorithm = "AES-256-CBC",
            ContentSha256 = hash
        };
    }

    private async Task EncryptAsync(byte[] plain, string path, CancellationToken cancellationToken)
    {
        using var aes = Aes.Create();
        aes.Key = _key;
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;
        aes.GenerateIV();

        await using var output = File.Create(path);
        await output.WriteAsync(aes.IV, cancellationToken);
        await using var crypto = new CryptoStream(output, aes.CreateEncryptor(), CryptoStreamMode.Write);
        await crypto.WriteAsync(plain, cancellationToken);
        await crypto.FlushFinalBlockAsync(cancellationToken);
    }
}
