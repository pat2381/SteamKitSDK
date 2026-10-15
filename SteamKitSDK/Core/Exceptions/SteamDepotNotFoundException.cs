using System;
using SteamKitSDK.Core.Structs;

namespace SteamKitSDK.ContentDelivery.Exceptions
{
    public class SteamDepotNotFoundException : Exception
    {
        public SteamDepotNotFoundException(DepotId depotId) : base($"Depot {depotId} not found.")
        {
        }

        public SteamDepotNotFoundException(string message) : base(message)
        {
        }
    }
}