using System.Security.Cryptography;

namespace ComicStudio.Api.Auth;

/// <summary>
/// Provides the JWT signing key. Uses the configured value when present,
/// otherwise generates one and persists it beside the Data Protection keyring
/// so local development needs no committed secrets.
/// </summary>
public sealed class SigningKeyProvider
{
    private const int KeySizeBytes = 48;

    public SigningKeyProvider(string keyFilePath, string? configuredKey)
    {
        if (!string.IsNullOrWhiteSpace(configuredKey))
        {
            Key = Convert.FromBase64String(configuredKey);
            return;
        }

        var directory = Path.GetDirectoryName(keyFilePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        if (File.Exists(keyFilePath))
        {
            Key = Convert.FromBase64String(File.ReadAllText(keyFilePath).Trim());
            return;
        }

        Key = RandomNumberGenerator.GetBytes(KeySizeBytes);
        File.WriteAllText(keyFilePath, Convert.ToBase64String(Key));

        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(keyFilePath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
    }

    public byte[] Key { get; }
}
