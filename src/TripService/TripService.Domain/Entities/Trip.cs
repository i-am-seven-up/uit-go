using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TripService.Domain.Entities
{
    public enum TripStatus { Pending, Searching, Accepted, InProgress, Completed, Canceled }

    public class Trip
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public Guid RiderId { get; set; }
        public Guid? DriverId { get; set; }
        public double StartLat { get; set; }
        public double StartLng { get; set; }
        public double EndLat { get; set; }
        public double EndLng { get; set; }
        public TripStatus Status { get; set; } = TripStatus.Pending;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
