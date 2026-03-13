using System.Security.Cryptography;
using System.Text;

namespace DataVault.API.Services;

/// <summary>
/// Handles all cryptographic operations for the vault.
/// Strategy:
///   - Each user has a Master Key (AES-256), stored encrypted in DB
///     (encrypted with a server-side KEK derived from user password + server secret)
///   - Each file gets its own unique AES-256 File Key
///   - The File Key is encrypted with the user's Master Key before DB storage
///   - Files on disk are encrypted with their unique File Key + random IV
/// </summary>
public interface IEncryptionService
{
    // Master key operations
    string GenerateMasterKey();
    string EncryptMasterKey(string masterKeyBase64, string password);
    string DecryptMasterKey(string encryptedMasterKey, string password);

    // File key operations  
    (string fileKeyBase64, string ivBase64) GenerateFileKey();
    string EncryptFileKey(string fileKeyBase64, string masterKeyBase64);
    string DecryptFileKey(string encryptedFileKey, string masterKeyBase64);

    // File content operations
    Task<byte[]> EncryptFileAsync(Stream plainStream, string fileKeyBase64, string ivBase64);
    Task EncryptFileToStreamAsync(Stream plainStream, Stream cipherStream, string fileKeyBase64, string ivBase64);
    Task DecryptFileToStreamAsync(Stream cipherStream, Stream plainStream, string fileKeyBase64, string ivBase64);

    // Integrity
    string ComputeHash(byte[] data);
    Task<string> ComputeHashAsync(Stream stream);
    string GenerateSecureToken(int byteLength = 32);
}

public class EncryptionService : IEncryptionService
{
    private readonly IConfiguration _config;

    public EncryptionService(IConfiguration config)
    {
        _config = config;
    }

    // ── Master Key ────────────────────────────────────────────────────────

    public string GenerateMasterKey()
    {
        var key = RandomNumberGenerator.GetBytes(32); // 256-bit
        return Convert.ToBase64String(key);
    }

    public string EncryptMasterKey(string masterKeyBase64, string password)
    {
        // Derive KEK from password + server secret using PBKDF2
        var serverSecret = _config["Vault:ServerSecret"] ?? "DefaultServerSecretChangeInProd!";
        var salt = SHA256.HashData(Encoding.UTF8.GetBytes(password + serverSecret));
        var kek = Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(password + serverSecret),
            salt, 100_000, HashAlgorithmName.SHA256, 32);

        var masterKeyBytes = Convert.FromBase64String(masterKeyBase64);
        var iv = RandomNumberGenerator.GetBytes(16);

        using var aes = Aes.Create();
        aes.Key = kek;
        aes.IV = iv;
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;

        using var encryptor = aes.CreateEncryptor();
        var encrypted = encryptor.TransformFinalBlock(masterKeyBytes, 0, masterKeyBytes.Length);

        // Store as IV:EncryptedKey (both base64)
        return $"{Convert.ToBase64String(iv)}:{Convert.ToBase64String(encrypted)}";
    }

    public string DecryptMasterKey(string encryptedMasterKey, string password)
    {
        var serverSecret = _config["Vault:ServerSecret"] ?? "DefaultServerSecretChangeInProd!";
        var salt = SHA256.HashData(Encoding.UTF8.GetBytes(password + serverSecret));
        var kek = Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(password + serverSecret),
            salt, 100_000, HashAlgorithmName.SHA256, 32);

        var parts = encryptedMasterKey.Split(':');
        var iv = Convert.FromBase64String(parts[0]);
        var encryptedBytes = Convert.FromBase64String(parts[1]);

        using var aes = Aes.Create();
        aes.Key = kek;
        aes.IV = iv;
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;

        using var decryptor = aes.CreateDecryptor();
        var masterKeyBytes = decryptor.TransformFinalBlock(encryptedBytes, 0, encryptedBytes.Length);
        return Convert.ToBase64String(masterKeyBytes);
    }

    // ── File Key ─────────────────────────────────────────────────────────

    public (string fileKeyBase64, string ivBase64) GenerateFileKey()
    {
        var key = RandomNumberGenerator.GetBytes(32); // AES-256
        var iv = RandomNumberGenerator.GetBytes(16);  // AES block size
        return (Convert.ToBase64String(key), Convert.ToBase64String(iv));
    }

    public string EncryptFileKey(string fileKeyBase64, string masterKeyBase64)
    {
        var fileKeyBytes = Convert.FromBase64String(fileKeyBase64);
        var masterKey = Convert.FromBase64String(masterKeyBase64);
        var iv = RandomNumberGenerator.GetBytes(16);

        using var aes = Aes.Create();
        aes.Key = masterKey;
        aes.IV = iv;
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;

        using var encryptor = aes.CreateEncryptor();
        var encrypted = encryptor.TransformFinalBlock(fileKeyBytes, 0, fileKeyBytes.Length);
        return $"{Convert.ToBase64String(iv)}:{Convert.ToBase64String(encrypted)}";
    }

    public string DecryptFileKey(string encryptedFileKey, string masterKeyBase64)
    {
        var masterKey = Convert.FromBase64String(masterKeyBase64);
        var parts = encryptedFileKey.Split(':');
        var iv = Convert.FromBase64String(parts[0]);
        var encryptedBytes = Convert.FromBase64String(parts[1]);

        using var aes = Aes.Create();
        aes.Key = masterKey;
        aes.IV = iv;
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;

        using var decryptor = aes.CreateDecryptor();
        var fileKeyBytes = decryptor.TransformFinalBlock(encryptedBytes, 0, encryptedBytes.Length);
        return Convert.ToBase64String(fileKeyBytes);
    }

    // ── File Content Encryption ──────────────────────────────────────────

    public async Task<byte[]> EncryptFileAsync(Stream plainStream, string fileKeyBase64, string ivBase64)
    {
        using var ms = new MemoryStream();
        await EncryptFileToStreamAsync(plainStream, ms, fileKeyBase64, ivBase64);
        return ms.ToArray();
    }

    public async Task EncryptFileToStreamAsync(Stream plainStream, Stream cipherStream,
        string fileKeyBase64, string ivBase64)
    {
        var key = Convert.FromBase64String(fileKeyBase64);
        var iv = Convert.FromBase64String(ivBase64);

        using var aes = Aes.Create();
        aes.Key = key;
        aes.IV = iv;
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;

        using var encryptor = aes.CreateEncryptor();
        using var cs = new CryptoStream(cipherStream, encryptor, CryptoStreamMode.Write, leaveOpen: true);
        await plainStream.CopyToAsync(cs);
        await cs.FlushFinalBlockAsync();
    }

    public async Task DecryptFileToStreamAsync(Stream cipherStream, Stream plainStream,
        string fileKeyBase64, string ivBase64)
    {
        var key = Convert.FromBase64String(fileKeyBase64);
        var iv = Convert.FromBase64String(ivBase64);

        using var aes = Aes.Create();
        aes.Key = key;
        aes.IV = iv;
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;

        using var decryptor = aes.CreateDecryptor();
        using var cs = new CryptoStream(cipherStream, decryptor, CryptoStreamMode.Read, leaveOpen: true);
        await cs.CopyToAsync(plainStream);
    }

    // ── Integrity & Tokens ────────────────────────────────────────────────

    public string ComputeHash(byte[] data)
    {
        var hash = SHA256.HashData(data);
        return Convert.ToHexString(hash).ToLower();
    }

    public async Task<string> ComputeHashAsync(Stream stream)
    {
        var hash = await SHA256.HashDataAsync(stream);
        stream.Position = 0;
        return Convert.ToHexString(hash).ToLower();
    }

    public string GenerateSecureToken(int byteLength = 32)
    {
        var bytes = RandomNumberGenerator.GetBytes(byteLength);
        return Convert.ToBase64String(bytes)
            .Replace("+", "-").Replace("/", "_").Replace("=", ""); // URL-safe
    }
}
