using AF.Blood.Application.Domain;
using Xunit;

namespace AF.Blood.UnitTests.Domain;

public class GeoGeneralizerTests
{
    [Fact]
    public void Coordinates_are_rounded_to_two_decimals()
    {
        var (lat, lon) = GeoGeneralizer.Generalize(31.52041234, 74.35871234);
        Assert.Equal(31.52, lat);
        Assert.Equal(74.36, lon);
    }

    [Fact]
    public void Generalization_is_idempotent()
    {
        var first = GeoGeneralizer.Generalize(31.518912, 74.312345);
        var second = GeoGeneralizer.Generalize(first.Latitude, first.Longitude);
        Assert.Equal(first, second);
    }

    [Fact]
    public void Out_of_range_coordinates_are_rejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => GeoGeneralizer.Generalize(91, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => GeoGeneralizer.Generalize(0, -181));
    }

    [Fact]
    public void Two_generalized_points_one_step_apart_are_about_one_kilometre()
    {
        var a = GeoGeneralizer.Generalize(31.52, 74.35);
        var b = GeoGeneralizer.Generalize(31.53, 74.35);
        var metres = GeoGeneralizer.ApproxDistanceMetres(a.Latitude, a.Longitude, b.Latitude, b.Longitude);
        Assert.InRange(metres, 1000, 1200);
    }

    [Fact]
    public void Grid_size_is_within_privacy_target()
    {
        // ADR-0005: ~1.1 km grid. One unit in the 2nd decimal of latitude is
        // ~1.11 km, so two donors up to 1.1 km apart can collide onto one cell —
        // that is the intended privacy behaviour.
        var a = GeoGeneralizer.Generalize(31.521, 74.351);
        var b = GeoGeneralizer.Generalize(31.524, 74.354);
        Assert.Equal(a, b);
    }
}
