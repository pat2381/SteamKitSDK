using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SteamKitSDK.Windows.Authentication;

/// <summary>
/// Persists authentication state in files encrypted with Windows DPAPI for the current user.
/// The encrypted files cannot be read by other Windows user profiles.
/// </summary>
public sealed class DpapiSteamAuthenticationStore : ISteamAuthenticationStore
{
    private const string AccessTokenKind = "access-token";
    private const string GuardDataKind = "guard-data";
    private const string FileExtension = ".steamkitsdk";
    private readonly string _storageDirectory;

    public DpapiSteamAuthenticationStore(string storageDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(storageDirectory);

        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("DPAPI authentication storage is only available on Windows.");
        }

        _storageDirectory = Path.GetFullPath(storageDirectory);
    }

    public Task SaveAccessTokenAsync(string accountName, string token, CancellationToken cancellationToken = default) =>
        SaveAsync(accountName, AccessTokenKind, token, cancellationToken);

    public Task<string?> GetAccessTokenAsync(string accountName, CancellationToken cancellationToken = default) =>
        GetAsync(accountName, AccessTokenKind, cancellationToken);

    public Task SaveGuardDataAsync(string accountName, string guardData, CancellationToken cancellationToken = default) =>
        SaveAsync(accountName, GuardDataKind, guardData, cancellationToken);

    public Task<string?> GetGuardDataAsync(string accountName, CancellationToken cancellationToken = default) =>
        GetAsync(accountName, GuardDataKind, cancellationToken);

    public Task DeleteAsync(string accountName, CancellationToken cancellationToken = default)
    {
        ValidateAccountName(accountName);
        cancellationToken.ThrowIfCancellationRequested();

        DeleteIfExists(GetFilePath(accountName, AccessTokenKind));
        DeleteIfExists(GetFilePath(accountName, GuardDataKind));
        return Task.CompletedTask;
    }

    private async Task SaveAsync(string accountName, string kind, string value, CancellationToken cancellationToken)
    {
        ValidateAccountName(accountName);
        ArgumentNullException.ThrowIfNull(value);
        cancellationToken.ThrowIfCancellationRequested();

        Directory.CreateDirectory(_storageDirectory);
        var encryptedValue = ProtectedData.Protect(
            Encoding.UTF8.GetBytes(value),
            GetEntropy(accountName, kind),
            DataProtectionScope.CurrentUser);

        var filePath = GetFilePath(accountName, kind);
        var temporaryPath = $"{filePath}.{Guid.NewGuid():N}.tmp";

        try
        {
            await File.WriteAllBytesAsync(temporaryPath, encryptedValue, cancellationToken).ConfigureAwait(false);
            File.Move(temporaryPath, filePath, true);
        }
        finally
        {
            DeleteIfExists(temporaryPath);
        }
    }

    private async Task<string?> GetAsync(string accountName, string kind, CancellationToken cancellationToken)
    {
        ValidateAccountName(accountName);
        cancellationToken.ThrowIfCancellationRequested();

        var filePath = GetFilePath(accountName, kind);
        if (!File.Exists(filePath))
        {
            return null;
        }

        var encryptedValue = await File.ReadAllBytesAsync(filePath, cancellationToken).ConfigureAwait(false);
        var value = ProtectedData.Unprotect(
            encryptedValue,
            GetEntropy(accountName, kind),
            DataProtectionScope.CurrentUser);

        return Encoding.UTF8.GetString(value);
    }

    private string GetFilePath(string accountName, string kind) =>
        Path.Combine(_storageDirectory, $"{GetStableIdentifier(accountName, kind)}{FileExtension}");

    private static byte[] GetEntropy(string accountName, string kind) =>
        SHA256.HashData(Encoding.UTF8.GetBytes($"SteamKitSDK.Windows|{kind}|{accountName}"));

    private static string GetStableIdentifier(string accountName, string kind) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{kind}|{accountName}"))).ToLowerInvariant();

    private static void ValidateAccountName(string accountName) =>
        ArgumentException.ThrowIfNullOrWhiteSpace(accountName);

    private static void DeleteIfExists(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }
}
