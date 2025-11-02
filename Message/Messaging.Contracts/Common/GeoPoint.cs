using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Messaging.Contracts.Common
{
    public readonly record struct GeoPoint(double Lat, double Lng)
    {
        public override string ToString() => $"({Lat},{Lng})";
    }
}
