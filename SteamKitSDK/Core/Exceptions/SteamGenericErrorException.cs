using System;

namespace SteamKitSDK.ContentDelivery.Exceptions
{
    public class SteamGenericErrorException : Exception
    {
        public SteamGenericErrorException() : base("An error occurred.")
        {
        }
    }
}
