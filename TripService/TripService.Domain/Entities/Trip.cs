using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TripService.Domain.Entities
{
    public enum TripStatus
    {
        Requested = 0,          // Passenger created trip
        FindingDriver = 1,      // System searching for driver
        DriverAssigned = 2,     // Driver found, offer sent (15s timer starts)
        DriverAccepted = 3,     // Driver accepted the trip
        DriverOnTheWay = 4,     // Driver heading to pickup
        DriverArrived = 5,      // Driver at pickup location
        InProgress = 6,         // Trip started (passenger in car)
        Completed = 7,          // Trip finished successfully
        Cancelled = 8,          // Trip cancelled by passenger/driver/system
        DriverDeclined = 9,     // Driver declined, retry with next driver
        NoDriverAvailable = 10  // No drivers found after retries
    }

    public class Trip
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public Guid PassengerId { get; set; }
        public Guid? AssignedDriverId { get; set; }
        public double StartLat { get; set; }
        public double StartLng { get; set; }
        public double EndLat { get; set; }
        public double EndLng { get; set; }
        public TripStatus Status { get; set; } = TripStatus.Requested;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        // State tracking fields
        public DateTime? DriverAssignedAt { get; set; }
        public DateTime? DriverAcceptedAt { get; set; }
        public DateTime? DriverArrivedAt { get; set; }
        public DateTime? TripStartedAt { get; set; }
        public DateTime? TripCompletedAt { get; set; }
        public DateTime? CancelledAt { get; set; }
        public string? CancellationReason { get; set; }
        public int DriverRetryCount { get; set; } = 0;
        public DateTime? LastStatusChangeAt { get; set; }

        // State transition validation
        private static readonly Dictionary<TripStatus, List<TripStatus>> AllowedTransitions = new()
        {
            [TripStatus.Requested] = new() { TripStatus.FindingDriver, TripStatus.Cancelled },
            [TripStatus.FindingDriver] = new() { TripStatus.DriverAssigned, TripStatus.NoDriverAvailable, TripStatus.Cancelled },
            [TripStatus.DriverAssigned] = new() { TripStatus.DriverAccepted, TripStatus.DriverDeclined, TripStatus.FindingDriver, TripStatus.Cancelled },
            [TripStatus.DriverDeclined] = new() { TripStatus.FindingDriver, TripStatus.NoDriverAvailable, TripStatus.Cancelled },
            [TripStatus.DriverAccepted] = new() { TripStatus.DriverOnTheWay, TripStatus.Cancelled },
            [TripStatus.DriverOnTheWay] = new() { TripStatus.DriverArrived, TripStatus.Cancelled },
            [TripStatus.DriverArrived] = new() { TripStatus.InProgress, TripStatus.Cancelled },
            [TripStatus.InProgress] = new() { TripStatus.Completed, TripStatus.Cancelled },
            [TripStatus.Completed] = new() { },
            [TripStatus.Cancelled] = new() { },
            [TripStatus.NoDriverAvailable] = new() { TripStatus.Cancelled }
        };

        public bool CanTransitionTo(TripStatus newStatus)
        {
            return AllowedTransitions.ContainsKey(Status) &&
                   AllowedTransitions[Status].Contains(newStatus);
        }

        public void TransitionTo(TripStatus newStatus)
        {
            if (!CanTransitionTo(newStatus))
            {
                throw new InvalidOperationException(
                    $"Cannot transition from {Status} to {newStatus}");
            }

            Status = newStatus;
            LastStatusChangeAt = DateTime.UtcNow;
        }

        // State transition methods
        public void StartFindingDriver()
        {
            TransitionTo(TripStatus.FindingDriver);
        }

        public void AssignDriver(Guid driverId)
        {
            AssignedDriverId = driverId;
            DriverAssignedAt = DateTime.UtcNow;
            DriverRetryCount++;
            TransitionTo(TripStatus.DriverAssigned);
        }

        public void DriverAccept()
        {
            DriverAcceptedAt = DateTime.UtcNow;
            TransitionTo(TripStatus.DriverAccepted);
        }

        public void DriverDecline()
        {
            AssignedDriverId = null;
            DriverAssignedAt = null;
            TransitionTo(TripStatus.DriverDeclined);
        }

        public void DriverOnTheWay()
        {
            TransitionTo(TripStatus.DriverOnTheWay);
        }

        public void DriverArrived()
        {
            DriverArrivedAt = DateTime.UtcNow;
            TransitionTo(TripStatus.DriverArrived);
        }

        public void StartTrip()
        {
            TripStartedAt = DateTime.UtcNow;
            TransitionTo(TripStatus.InProgress);
        }

        public void CompleteTrip()
        {
            TripCompletedAt = DateTime.UtcNow;
            TransitionTo(TripStatus.Completed);
        }

        public void Cancel(string reason)
        {
            CancelledAt = DateTime.UtcNow;
            CancellationReason = reason;
            TransitionTo(TripStatus.Cancelled);
        }

        public void MarkNoDriverAvailable()
        {
            TransitionTo(TripStatus.NoDriverAvailable);
        }
    }
}
