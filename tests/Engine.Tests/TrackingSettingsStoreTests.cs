using OverlayMod.Engine.Persistence;
using OverlayMod.Engine.Tracking;
using Xunit;

namespace OverlayMod.Engine.Tests;

public class TrackingSettingsStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "overlaymod-tests", Guid.NewGuid().ToString("N"));

    private string Path_ => Path.Combine(_dir, "tracking.json");

    public void Dispose()
    {
        try { if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    private void WriteFile(string json)
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path_, json);
    }

    [Fact]
    public void MissingFile_UsesTheDefaults()
    {
        var store = new TrackingSettingsStore(Path_);

        Assert.Equal(FallDamageOptions.Default, store.FallDamage);
        Assert.Equal(DamageOverTimeOptions.Default, store.DamageOverTime);
    }

    [Fact]
    public void SettingsSurviveAReload()
    {
        new TrackingSettingsStore(Path_).Update(
            new FallDamageOptions(true, 7.5, 900),
            new DamageOverTimeOptions(true, 6.5, 6000));

        var reloaded = new TrackingSettingsStore(Path_);

        Assert.Equal(7.5, reloaded.FallDamage.DescentMetres);
        Assert.Equal(900, reloaded.FallDamage.WindowMs);
        Assert.Equal(6.5, reloaded.DamageOverTime.MaxTickPercent);
        Assert.Equal(6000, reloaded.DamageOverTime.MaxIntervalMs);
    }

    [Fact]
    public void EitherHalfCanBeUpdatedAlone()
    {
        var store = new TrackingSettingsStore(Path_);
        store.Update(new FallDamageOptions(true, 9.0, 800), null);
        store.Update(null, new DamageOverTimeOptions(false, 3.0, 3000));

        Assert.Equal(9.0, store.FallDamage.DescentMetres);
        Assert.False(store.DamageOverTime.Enabled);

        // Both edits reached the file, not just the last one.
        var reloaded = new TrackingSettingsStore(Path_);
        Assert.Equal(9.0, reloaded.FallDamage.DescentMetres);
        Assert.Equal(3.0, reloaded.DamageOverTime.MaxTickPercent);
    }

    [Fact]
    public void TheFileWrittenBy_0_2_1_IsStillRead()
    {
        // 0.2.1 wrote the fall options at the root with no wrapper around them.
        // Anyone who had tuned their thresholds against a real route should keep
        // them across the upgrade rather than silently going back to 3 metres.
        WriteFile("""{ "enabled": true, "descentMetres": 6.5, "windowMs": 750 }""");

        var store = new TrackingSettingsStore(Path_);

        Assert.Equal(6.5, store.FallDamage.DescentMetres);
        Assert.Equal(750, store.FallDamage.WindowMs);
        Assert.Equal(DamageOverTimeOptions.Default, store.DamageOverTime);
    }

    [Fact]
    public void ReadingALegacyFileThenSavingWritesTheCurrentShape()
    {
        WriteFile("""{ "enabled": false, "descentMetres": 6.5, "windowMs": 750 }""");

        var store = new TrackingSettingsStore(Path_);
        store.Update(null, new DamageOverTimeOptions(true, 4.5, 5000));

        var reloaded = new TrackingSettingsStore(Path_);
        Assert.False(reloaded.FallDamage.Enabled);
        Assert.Equal(6.5, reloaded.FallDamage.DescentMetres);
        Assert.Equal(4.5, reloaded.DamageOverTime.MaxTickPercent);
    }

    [Fact]
    public void TheFileWrittenBy_0_2_2_DoesNotLeaveAnImpossibleCeiling()
    {
        // 0.2.2 stored the ceiling as an amount of health under a different name.
        // Read naively, the percentage would come back as zero and be clamped up
        // to the smallest legal value — handing that user a tighter ceiling than
        // any real tick, which is the same silent no-op all over again.
        WriteFile("""
            {
              "fallDamage": { "enabled": true, "descentMetres": 3, "windowMs": 500 },
              "damageOverTime": { "enabled": true, "maxTickDamage": 40, "maxIntervalMs": 4000 }
            }
            """);

        var store = new TrackingSettingsStore(Path_);

        Assert.Equal(DamageOverTimeOptions.Default.MaxTickPercent, store.DamageOverTime.MaxTickPercent);
        Assert.Equal(4000, store.DamageOverTime.MaxIntervalMs);
        Assert.True(store.DamageOverTime.Enabled);
    }

    [Fact]
    public void NonsenseFallsBackToTheDefaults()
    {
        WriteFile("not json at all");

        var store = new TrackingSettingsStore(Path_);

        Assert.Equal(FallDamageOptions.Default, store.FallDamage);
        Assert.Equal(DamageOverTimeOptions.Default, store.DamageOverTime);
    }

    [Theory]
    [InlineData(0, 0.5)]
    [InlineData(9999, 50.0)]
    [InlineData(8, 8.0)]
    public void TickSizeIsClamped(double requested, double expected)
    {
        var store = new TrackingSettingsStore(Path_);
        var (_, overTime) = store.Update(null, new DamageOverTimeOptions(true, requested, 2500));

        Assert.Equal(expected, overTime.MaxTickPercent);
    }

    [Fact]
    public void TheIntervalCannotBeSetBelowTheDetectorsOwnFloor()
    {
        var store = new TrackingSettingsStore(Path_);
        var (_, overTime) = store.Update(null, new DamageOverTimeOptions(true, 8, 50));

        // A band whose top is below its bottom would classify nothing at all,
        // and would do it silently.
        Assert.Equal(600, overTime.MaxIntervalMs);
    }

    [Fact]
    public void TheCeilingIsNeverZero_WhateverTheHealthScale()
    {
        // A ceiling of zero means nothing is ever small enough to be a tick, and
        // the classifier is off without saying so. Percentages of a health scale
        // that reads badly are the obvious way to arrive at one.
        Assert.True(DamageOverTimeOptions.Default.CeilingFor(0) >= 1);
        Assert.True(DamageOverTimeOptions.Default.CeilingFor(-5) >= 1);

        var tiny = new DamageOverTimeOptions(true, 0.5, 2500);
        Assert.True(tiny.CeilingFor(1) >= 1);
    }

    [Fact]
    public void TheCeilingTracksTheCharacter()
    {
        var options = new DamageOverTimeOptions(true, 8.0, 2500);

        Assert.Equal(36, options.CeilingFor(450));    // a fresh character
        Assert.Equal(128, options.CeilingFor(1600));  // a late one
    }
}
