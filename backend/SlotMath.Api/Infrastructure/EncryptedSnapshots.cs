using System.Security.Cryptography;
using System.Text.Json;
using SlotMath.Core;

namespace SlotMath.Api.Infrastructure;

/// <summary>Atomic encrypted snapshots for a single API instance.</summary>
public sealed class EncryptedSnapshots
{
    private readonly string _directory;
    private readonly byte[] _key;
    public EncryptedSnapshots(string directory, string base64Key)
    {
        _directory = directory;
        _key = Convert.FromBase64String(base64Key);
        if (_key.Length != 32) throw new InvalidOperationException("Storage key must contain 32 bytes.");
        Directory.CreateDirectory(directory);
    }
    public T? Read<T>(string name)
    {
        var path = Path.Combine(_directory, name + ".bin");
        if (!File.Exists(path)) return default;
        var bytes = File.ReadAllBytes(path);
        if (bytes.Length < 28) throw new InvalidDataException("Snapshot is truncated.");
        var plain = new byte[bytes.Length - 28];
        using var aes = new AesGcm(_key, 16);
        aes.Decrypt(bytes.AsSpan(0, 12), bytes.AsSpan(28), bytes.AsSpan(12, 16), plain);
        return JsonSerializer.Deserialize<T>(plain, JsonOptions.Default);
    }
    public void Write<T>(string name, T value)
    {
        var plain = JsonSerializer.SerializeToUtf8Bytes(value, JsonOptions.Default);
        var bytes = new byte[plain.Length + 28];
        RandomNumberGenerator.Fill(bytes.AsSpan(0, 12));
        using var aes = new AesGcm(_key, 16);
        aes.Encrypt(bytes.AsSpan(0, 12), plain, bytes.AsSpan(28), bytes.AsSpan(12, 16));
        var path = Path.Combine(_directory, name + ".bin");
        var temp = path + ".tmp";
        using (var file = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            file.Write(bytes); file.Flush(flushToDisk: true);
        }
        File.Move(temp, path, overwrite: true);
    }
}
