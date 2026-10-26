namespace AF.Blood.Application.Domain;

/// <summary>
/// Generalizes coordinates for storage and exposure (ADR-0005): 2 decimal
/// places ≈ 1.1 km grid. Precise coordinates are never persisted, so matching,
/// display, and leaks all see only the generalized point.
/// </summary>
public static class GeoGeneralizer
{
    public const int DecimalPlaces = 2;
    public const double LatitudeMin = -90;
    public const double LatitudeMax = 90;
    public const double LongitudeMin = -180;
    public const double LongitudeMax = 180;

    public static (double Latitude, double Longitude) Generalize(double latitude, double longitude)
    {
        if (latitude is < LatitudeMin or > LatitudeMax)
        {
            throw new ArgumentOutOfRangeException(nameof(latitude), "Latitude out of range.");
        }

        if (longitude is < LongitudeMin or > LongitudeMax)
        {
            throw new ArgumentOutOfRangeException(nameof(longitude), "Longitude out of range.");
        }

        var factor = Math.Pow(10, DecimalPlaces);
        return (
            Math.Round(latitude, DecimalPlaces, MidpointRounding.ToEven) * factor / factor,
            Math.Round(longitude, DecimalPlaces, MidpointRounding.ToEven) * factor / factor);
    }

    /// <summary>Distance in metres between two (generalized) points, using the
    /// spherical law of cosines approximation — only used for display rounding;
    /// authoritative distances come from PostGIS.</summary>
    public static double ApproxDistanceMetres(double lat1, double lon1, double lat2, double lon2)
    {
        const double earthRadiusMetres = 6_371_000;
        var phi1 = lat1 * Math.PI / 180.0;
        var phi2 = lat2 * Math.PI / 180.0;
        var deltaLambda = (lon2 - lon1) * Math.PI / 180.0;
        var c = Math.Acos(
            Math.Min(1.0, Math.Sin(phi1) * Math.Sin(phi2) + Math.Cos(phi1) * Math.Cos(phi2) * Math.Cos(deltaLambda)));
        return earthRadiusMetres * c;
    }
}
