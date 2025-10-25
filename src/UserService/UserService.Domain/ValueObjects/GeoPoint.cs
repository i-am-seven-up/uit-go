using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace UserService.Domain.ValueObjects
{
    public sealed record GeoPoint
    {
        private double _latitude;
        private double _longitude;

        public double Latitude
        {
            get => _latitude;
            init
            {
                if (value < -90 || value > 90)
                    throw new ArgumentOutOfRangeException(nameof(Latitude), "Latitude must be between -90 and 90 degrees.");
                _latitude = value;
            }
        }

        public double Longitude
        {
            get => _longitude;
            init
            {
                if (value < -180 || value > 180)
                    throw new ArgumentOutOfRangeException(nameof(Longitude), "Longitude must be between -180 and 180 degrees.");
                _longitude = value;
            }
        }

        public GeoPoint(double latitude, double longitude)
        {
            Latitude = latitude;
            Longitude = longitude;
        }
    }
}
