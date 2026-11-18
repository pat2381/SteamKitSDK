using System.Threading;
using System.Threading.Tasks;
using SteamKit2.Authentication;

namespace SteamKitSDK.Core
{
    public class SteamAuthenticatorAdapter : IAuthenticator
    {
        private readonly SteamAuthenticator _publicAuthenticator;
        private readonly CancellationToken _cancellationToken;

        public SteamAuthenticatorAdapter(SteamAuthenticator publicAuthenticator) : this(publicAuthenticator, default)
        {
        }

        internal SteamAuthenticatorAdapter(SteamAuthenticator publicAuthenticator, CancellationToken cancellationToken)
        {
            _publicAuthenticator = publicAuthenticator;
            _cancellationToken = cancellationToken;
        }

        public async Task<string> GetDeviceCodeAsync(bool previousCodeWasIncorrect)
        {
            return await _publicAuthenticator.GetTwoFactorAuthenticationCodeAsync(previousCodeWasIncorrect, _cancellationToken).ConfigureAwait(false);
        }

        public async Task<string> GetEmailCodeAsync(string email, bool previousCodeWasIncorrect)
        {
            return await _publicAuthenticator.GetEmailAuthenticationCodeAsync(email, previousCodeWasIncorrect, _cancellationToken).ConfigureAwait(false);
        }

        public async Task<bool> AcceptDeviceConfirmationAsync()
        {
            return await _publicAuthenticator.NotifyMobileNotificationAsync(_cancellationToken).ConfigureAwait(false);
        }
    }
}
