using System.Threading;
using System.Threading.Tasks;

namespace SteamKitSDK.Windows.Authentication;

/// <summary>
/// Provides the application-specific dialogs used during Steam authentication.
/// Implement this interface in a WPF or Windows Forms application and marshal UI work to its UI thread.
/// </summary>
public interface ISteamAuthenticationDialogService
{
    Task<string> RequestTwoFactorAuthenticationCodeAsync(
        bool previousCodeWasIncorrect,
        CancellationToken cancellationToken = default);

    Task<string> RequestEmailAuthenticationCodeAsync(
        string accountEmail,
        bool previousCodeWasIncorrect,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Asks the user to approve the Steam Mobile confirmation or to fall back to entering a code.
    /// </summary>
    Task<bool> RequestMobileConfirmationAsync(CancellationToken cancellationToken = default);
}
