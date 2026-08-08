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
            new DamageOverTimeOptions(true, 65, 6000));

        var reloaded = new TrackingSettingsStore(Path_);

        Assert.Equal(7.5, reloaded.FallDamage.DescentMetres);
        Assert.Equal(900, reloaded.FallDamage.WindowMs);
        Assert.Equal(65, reloaded.DamageOverTime.MaxTickDamage);
        Assert.Equal(6000, reloaded.DamageOverTime.MaxIntervalMs);
    }

    [Fact]
    public void EitherHalfCanBeUpdatedAlone()
    {
        var store = new TrackingSettingsStore(Path_);
        store.Update(new FallDamageOptions(true, 9.0, 800), null);
        store.Update(null, new DamageOverTimeOptions(false, 30, 3000));

        Assert.Equal(9.0, store.FallDamage.DescentMetres);
        Assert.False(store.DamageOverTime.Enabled);

        // Both edits reached the file, not just the last one.
        var reloaded = new TrackingSettingsStore(Path_);
        Assert.Equal(9.0, reloaded.FallDamage.DescentMetres);
        Assert.Equal(30, reloaded.DamageOverTime.MaxTickDamage);
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
        store.Update(null, new DamageOverTimeOptions(true, 45, 5000));

        var reloaded = new TrackingSettingsStore(Path_);
        Assert.False(reloaded.FallDamage.Enabled);
        Assert.Equal(6.5, reloaded.FallDamage.DescentMetres);
        Assert.Equal(45, reloaded.DamageOverTime.MaxTickDamage);
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
    [InlineData(0, 1)]
    [InlineData(9999, 500)]
    [InlineData(40, 40)]
    public void TickSizeIsClamped(int requested, int expected)
    {
        var store = new TrackingSettingsStore(Path_);
        var (_, overTime) = store.Update(null, new DamageOverTimeOptions(true, requested, 4000));

        Assert.Equal(expected, overTime.MaxTickDamage);
    }

    [Fact]
    public void TheIntervalCannotBeSetBelowTheDetectorsOwnFloor()
    {
        var store = new TrackingSettingsStore(Path_);
        var (_, overTime) = store.Update(null, new DamageOverTimeOptions(true, 40, 200));

        // A band whose top is below its bottom would classify nothing at all,
        // and would do it silently.
        Assert.Equal(1500, overTime.MaxIntervalMs);
    }
}
