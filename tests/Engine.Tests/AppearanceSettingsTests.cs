using OverlayMod.Host.Appearance;
using Xunit;

namespace OverlayMod.Engine.Tests;

/// <summary>
/// Appearance values arrive over HTTP and are written straight into CSS custom
/// properties, so nothing unvalidated may reach the page.
/// </summary>
public class AppearanceSettingsTests
{
    [Fact]
    public void DefaultsSurviveSanitising()
    {
        var d = AppearanceSettings.Default;
        Assert.Equal(d, d.Sanitised());
    }

    [Theory]
    [InlineData("red")]                 // named colours are not accepted
    [InlineData("#fff")]                // shorthand is not accepted
    [InlineData("#12345")]              // wrong length
    [InlineData("")]
    [InlineData("url(javascript:x)")]   // anything that is not a hex colour
    [InlineData("#00ff00; position: fixed")]
    public void AMalformedColourFallsBackToTheDefault(string colour)
    {
        var sanitised = (AppearanceSettings.Default with { Accent = colour }).Sanitised();

        Assert.Equal(AppearanceSettings.Default.Accent, sanitised.Accent);
    }

    [Fact]
    public void AValidColourIsKept()
    {
        var sanitised = (AppearanceSettings.Default with { Accent = "#AABBCC" }).Sanitised();
        Assert.Equal("#aabbcc", sanitised.Accent);
    }

    [Theory]
    [InlineData(0.1, 0.5)]      // below the floor
    [InlineData(99, 3.0)]       // above the ceiling
    [InlineData(1.5, 1.5)]      // in range
    public void ScaleIsClamped(double given, double expected)
    {
        Assert.Equal(expected, (AppearanceSettings.Default with { Scale = given }).Sanitised().Scale);
    }

    [Fact]
    public void NonFiniteNumbersFallBackToTheDefault()
    {
        var broken = AppearanceSettings.Default with { Scale = double.NaN, PlateOpacity = double.PositiveInfinity };
        var sanitised = broken.Sanitised();

        // Not clamped to the nearest bound: NaN and infinity carry no intent, so
        // the default is a better answer than an edge value the user never chose.
        Assert.Equal(AppearanceSettings.Default.Scale, sanitised.Scale);
        Assert.Equal(AppearanceSettings.Default.PlateOpacity, sanitised.PlateOpacity);
    }

    [Theory]
    [InlineData(0, 1)]      // one row is a legitimate minimal overlay
    [InlineData(500, 30)]   // enough for a whole All Bosses route at once
    [InlineData(25, 25)]
    public void VisibleSplitsIsClamped(int given, int expected)
    {
        Assert.Equal(expected, (AppearanceSettings.Default with { VisibleSplits = given }).Sanitised().VisibleSplits);
    }

    [Fact]
    public void FullyTransparentPanelIsAllowed()
    {
        // Zero opacity is how you get text floating straight on the capture.
        var sanitised = (AppearanceSettings.Default with { PlateOpacity = 0 }).Sanitised();
        Assert.Equal(0, sanitised.PlateOpacity);
    }
}
