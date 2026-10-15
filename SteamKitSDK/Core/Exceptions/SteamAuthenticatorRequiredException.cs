using System;

namespace SteamKitSDK.Core.Exceptions
{
    public class SteamAuthenticatorRequiredException : Exception
    {
        public SteamAuthenticatorRequiredException() : base(
            "An instance of SteamAuthenticator is required to be passed. Use " + nameof(ConsoleSteamAuthenticator) +
            " for a console authenticator.")
        {
        }
    }
}