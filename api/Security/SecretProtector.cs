using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;

namespace ComicStudio.Api.Security;

/// <summary>
/// Encrypts per-user NovelAI tokens at rest. The protector is scoped to the
/// user id so a ciphertext copied between accounts cannot be decrypted.
/// </summary>
public class SecretProtector(IDataProtectionProvider provider)
{
    private const string Purpose = "ComicStudio.NovelAiToken";

    public string Protect(string userId, string plaintext) =>
        protectorFor(userId).Protect(plaintext);

    public string? Unprotect(string userId, string? ciphertext)
    {
        if (string.IsNullOrEmpty(ciphertext))
        {
            return null;
        }

        try
        {
            return protectorFor(userId).Unprotect(ciphertext);
        }
        catch (CryptographicException)
        {
            // Keyring rotated or the value was tampered with; treat as "not connected".
            return null;
        }
    }

    private IDataProtector protectorFor(string userId) =>
        provider.CreateProtector(Purpose).CreateProtector(userId);
}
