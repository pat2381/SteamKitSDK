using System;

namespace SteamKitSDK.Core.Exceptions
{
    public class SteamAuthenticatorRequiredException : Exception
    {
        public SteamAuthenticatorRequiredException() : base(
            "An instance of SteamAuthenticator is required for authenticated Steam accounts. " +
            $"Use {nameof(ConsoleSteamAuthenticator)} for console applications or a desktop authenticator for UI applications.")
        {
        }
    }
}
