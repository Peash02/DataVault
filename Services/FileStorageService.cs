namespace DataVault.MVC.Services;

public interface IFileStorageService
{
    Task<string> SaveEncryptedFileAsync(Stream encryptedStream, string extension);
    Task<Stream> OpenEncryptedFileAsync(string storedFileName);
    Task DeleteFileAsync(string storedFileName);
    long GetFileSize(string storedFileName);
}

public class LocalFileStorageService : IFileStorageService
{
    private readonly string _basePath;
    private readonly ILogger<LocalFileStorageService> _logger;

    public LocalFileStorageService(IConfiguration config, ILogger<LocalFileStorageService> logger)
    {
        _basePath = config["Vault:StoragePath"] ?? Path.Combine(AppContext.BaseDirectory, "vault_storage");
        _logger = logger;
        Directory.CreateDirectory(_basePath);
    }

    public async Task<string> SaveEncryptedFileAsync(Stream encryptedStream, string extension)
    {
        var storedName = $"{Guid.NewGuid()}.enc";
        var fullPath   = Path.Combine(_basePath, storedName);

        await using var fs = new FileStream(fullPath, FileMode.Create, FileAccess.Write, FileShare.None);
        await encryptedStream.CopyToAsync(fs);

        _logger.LogInformation("Stored encrypted file: {Name}", storedName);
        return storedName;
    }

    public Task<Stream> OpenEncryptedFileAsync(string storedFileName)
    {
        var fullPath = Path.Combine(_basePath, storedFileName);
        if (!File.Exists(fullPath))
            throw new FileNotFoundException($"Encrypted file not found: {storedFileName}");

        Stream stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        return Task.FromResult(stream);
    }

    public Task DeleteFileAsync(string storedFileName)
    {
        var fullPath = Path.Combine(_basePath, storedFileName);
        if (File.Exists(fullPath)) File.Delete(fullPath);
        return Task.CompletedTask;
    }

    public long GetFileSize(string storedFileName)
    {
        var fullPath = Path.Combine(_basePath, storedFileName);
        return File.Exists(fullPath) ? new FileInfo(fullPath).Length : 0;
    }
}
