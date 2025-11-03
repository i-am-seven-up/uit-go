using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Messaging.Contracts.Routing
{
    public static class Routing
    {
        public const string Exchange = "uitgo.events";

        public static class Keys
        {
            // Trips
            public const string TripCreated  = "trip.created";  
            public const string TripAssigned = "trip.assigned";
            public const string TripRequested = "trip.requested";
            public const string TripCanceled = "trip.canceled";
            public const string TripAccepted = "trip.accepted"; 
            public const string TripOffered = "trip.offered";
            public const string TripOfferDeclined = "trip.offer.declined";

            // Drivers
            public const string DriverStatusChanged = "driver.status.changed";
            public const string DriverLocationUpdated = "driver.location.updated";
            public const string DriverAssignedToTrip  = "driver.assignedToTrip";
        }
    }

}
