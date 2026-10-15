using System.Threading;
using System.Threading.Tasks;

namespace SteamKitSDK.Windows.Authentication;

/// <summary>
/// Stores Steam access tokens and guard data for one local Windows user profile.
/// </summary>
public interface ISteamAuthenticationStore
{
    Task SaveAccessTokenAsync(string accountName, string token, CancellationToken cancellationToken = default);

    Task<string?> GetAccessTokenAsync(string accountName, CancellationToken cancellationToken = default);

    Task SaveGuardDataAsync(string accountName, string guardData, CancellationToken cancellationToken = default);

    Task<string?> GetGuardDataAsync(string accountName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes all locally persisted authentication state for the account.
    /// Call this when the user explicitly signs out.
    /// </summary>
    Task DeleteAsync(string accountName, CancellationToken cancellationToken = default);
}
