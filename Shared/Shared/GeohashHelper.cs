using NGeoHash;

namespace Shared;

/// <summary>
/// Provides geohash-based partitioning for Redis GEO operations to eliminate hotspots.
/// Uses precision 5 (~4.9km × 4.9km cells) for optimal distribution.
/// </summary>
public static class GeohashHelper
{
    private const int PRECISION = 5; // ~4.9km × 4.9km cells

    /// <summary>
    /// Gets the Redis partition key for a given location.
    /// </summary>
    /// <param name="lat">Latitude</param>
    /// <param name="lng">Longitude</param>
    /// <returns>Redis key in format "drivers:online:{geohash}"</returns>
    public static string GetPartitionKey(double lat, double lng)
    {
        var geohash = CalculateGeohash(lat, lng, PRECISION);
        return $"drivers:online:{geohash}";
    }

    /// <summary>
    /// Gets all neighbor partition keys (center + 8 neighbors) for a given location.
    /// Used for searching nearby drivers across partition boundaries.
    /// </summary>
    /// <param name="lat">Latitude</param>
    /// <param name="lng">Longitude</param>
    /// <returns>List of Redis keys including center and neighbors</returns>
    public static List<string> GetNeighborPartitions(double lat, double lng)
    {
        var center = CalculateGeohash(lat, lng, PRECISION);
        var neighbors = GetAdjacentGeohashes(center);

        // Include center + 8 neighbors (9 total partitions to query)
        var allPartitions = new List<string> { $"drivers:online:{center}" };
        allPartitions.AddRange(neighbors.Select(g => $"drivers:online:{g}"));

        return allPartitions;
    }

    /// <summary>
    /// Calculates geohash for given coordinates at specified precision.
    /// </summary>
    private static string CalculateGeohash(double lat, double lng, int precision)
    {
        return GeoHash.Encode(lat, lng, precision);
    }

    /// <summary>
    /// Gets all 8 adjacent geohashes (N, NE, E, SE, S, SW, W, NW).
    /// </summary>
    private static List<string> GetAdjacentGeohashes(string geohash)
    {
        var neighbors = new List<string>();

        // Neighbor lookup tables for geohash
        var neighborMap = new Dictionary<string, Dictionary<string, string>>
        {
            ["right"] = new Dictionary<string, string>
            {
                ["even"] = "bc01fg45238967deuvhjyznpkmstqrwx",
                ["odd"] = "p0r21436x8zb9dcf5h7kjnmqesgutwvy"
            },
            ["left"] = new Dictionary<string, string>
            {
                ["even"] = "238967debc01fg45kmstqrwxuvhjyznp",
                ["odd"] = "14365h7k9dcfesgujnmqp0r2twvyx8zb"
            },
            ["top"] = new Dictionary<string, string>
            {
                ["even"] = "p0r21436x8zb9dcf5h7kjnmqesgutwvy",
                ["odd"] = "bc01fg45238967deuvhjyznpkmstqrwx"
            },
            ["bottom"] = new Dictionary<string, string>
            {
                ["even"] = "14365h7k9dcfesgujnmqp0r2twvyx8zb",
                ["odd"] = "238967debc01fg45kmstqrwxuvhjyznp"
            }
        };

        var borderMap = new Dictionary<string, Dictionary<string, string>>
        {
            ["right"] = new Dictionary<string, string>
            {
                ["even"] = "bcfguvyz",
                ["odd"] = "prxz"
            },
            ["left"] = new Dictionary<string, string>
            {
                ["even"] = "0145hjnp",
                ["odd"] = "028b"
            },
            ["top"] = new Dictionary<string, string>
            {
                ["even"] = "prxz",
                ["odd"] = "bcfguvyz"
            },
            ["bottom"] = new Dictionary<string, string>
            {
                ["even"] = "028b",
                ["odd"] = "0145hjnp"
            }
        };

        try
        {
            // Calculate all 8 neighbors
            var north = GetNeighbor(geohash, "top", neighborMap, borderMap);
            var south = GetNeighbor(geohash, "bottom", neighborMap, borderMap);
            var east = GetNeighbor(geohash, "right", neighborMap, borderMap);
            var west = GetNeighbor(geohash, "left", neighborMap, borderMap);

            // Diagonal neighbors
            var northeast = GetNeighbor(north, "right", neighborMap, borderMap);
            var northwest = GetNeighbor(north, "left", neighborMap, borderMap);
            var southeast = GetNeighbor(south, "right", neighborMap, borderMap);
            var southwest = GetNeighbor(south, "left", neighborMap, borderMap);

            neighbors.AddRange(new[] { north, south, east, west, northeast, northwest, southeast, southwest });
        }
        catch
        {
            // Edge case: near poles or date line, return empty list
        }

        return neighbors.Where(n => !string.IsNullOrEmpty(n)).ToList();
    }

    /// <summary>
    /// Gets a single neighbor in the specified direction.
    /// </summary>
    private static string GetNeighbor(
        string geohash,
        string direction,
        Dictionary<string, Dictionary<string, string>> neighborMap,
        Dictionary<string, Dictionary<string, string>> borderMap)
    {
        if (string.IsNullOrEmpty(geohash))
            return string.Empty;

        var lastChar = geohash[^1];
        var type = geohash.Length % 2 == 0 ? "even" : "odd";
        var parent = geohash[..^1];

        // Check if we're at a border
        if (borderMap[direction][type].Contains(lastChar) && parent.Length > 0)
        {
            parent = GetNeighbor(parent, direction, neighborMap, borderMap);
        }

        // Look up the neighbor character
        var neighborChars = "0123456789bcdefghjkmnpqrstuvwxyz";
        var currentIndex = neighborChars.IndexOf(lastChar);
        if (currentIndex == -1)
            return string.Empty;

        var neighborIndex = neighborMap[direction][type].IndexOf(lastChar);
        if (neighborIndex == -1)
            return string.Empty;

        return parent + neighborChars[neighborIndex];
    }
}
