using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Shared
{
    public static class GeoUtils
    {
        private const double CellKm = 0.5;
        public static (int x, int y) Cell(double lat, double lng)
            => ((int)Math.Floor(lat / CellKm), (int)Math.Floor(lng / CellKm));

        public static IEnumerable<(int, int)> NeighborCells((int x, int y) c, double radiusKm)
        {
            int d = (int)Math.Ceiling(radiusKm / CellKm);
            for (int i = -d; i <= d; i++)
                for (int j = -d; j <= d; j++)
                    yield return (c.x + i, c.y + j);
        }
    }

}
