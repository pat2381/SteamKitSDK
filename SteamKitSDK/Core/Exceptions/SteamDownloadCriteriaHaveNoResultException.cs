using System;

namespace SteamKitSDK.ContentDelivery.Exceptions
{
    public class SteamDownloadCriteriaHaveNoResultException : Exception
    {
        public SteamDownloadCriteriaHaveNoResultException(string message) : base(message)
        {
        }
    }
}