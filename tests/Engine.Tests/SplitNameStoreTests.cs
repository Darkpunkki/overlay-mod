using OverlayMod.Engine.Persistence;
using OverlayMod.Engine.Routes;
using Xunit;

namespace OverlayMod.Engine.Tests;

/// <summary>
/// Renaming a split for the overlay. The point of doing it here rather than in
/// the route file is that personal bests are keyed on the route's own name, so
/// this cannot orphan them — which only holds if the store really is a view and
/// never writes back.
/// </summary>
public class SplitNameStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "overlaymod-tests", Guid.NewGuid().ToString("N"));

    private string Path_ => Path.Combine(_dir, "names.json");

    public void Dispose()
    {
        try { if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    private static Dictionary<string, string> Map(params (string Canonical, string Label)[] pairs)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (canonical, label) in pairs) map[canonical] = label;
        return map;
    }

    [Fact]
    public void NothingIsRenamedByDefault()
    {
        var store = new SplitNameStore(Path_);

        Assert.Empty(store.All);
        Assert.Null(store.Label("Soul of Cinder"));
    }

    [Fact]
    public void ARenameIsRememberedAndSurvivesARestart()
    {
        new SplitNameStore(Path_).Update(Map(("Soul of Cinder", "Cinder")));

        Assert.Equal("Cinder", new SplitNameStore(Path_).Label("Soul of Cinder"));
    }

    [Fact]
    public void UpdatingReplacesRatherThanMerges()
    {
        // Clearing a box on the control page has to mean "stop renaming this",
        // which a merge cannot express.
        var store = new SplitNameStore(Path_);
        store.Update(Map(("Soul of Cinder", "Cinder"), ("Crystal Sage", "Sage")));
        store.Update(Map(("Crystal Sage", "Sage")));

        Assert.Null(store.Label("Soul of Cinder"));
        Assert.Equal("Sage", store.Label("Crystal Sage"));
    }

    [Fact]
    public void ALabelIdenticalToTheNameIsDropped()
    {
        // An entry that changes nothing is a row to scroll past on the control
        // page and a line in a file nobody wanted.
        var store = new SplitNameStore(Path_);
        store.Update(Map(("Crystal Sage", "Crystal Sage")));

        Assert.Empty(store.All);
    }

    [Fact]
    public void BlankAndWhitespaceEntriesAreDropped()
    {
        var store = new SplitNameStore(Path_);
        store.Update(Map(("Crystal Sage", "   "), ("   ", "Sage"), ("Yhorm the Giant", " Yhorm ")));

        Assert.Single(store.All);
        Assert.Equal("Yhorm", store.Label("Yhorm the Giant"));
    }

    [Fact]
    public void AnOverlongLabelIsCutRatherThanLettingItIntoEveryFrame()
    {
        var store = new SplitNameStore(Path_);
        store.Update(Map(("Crystal Sage", new string('x', 500))));

        Assert.Equal(40, store.Label("Crystal Sage")!.Length);
    }

    [Fact]
    public void ShortNamesFillInEveryBossThisBuildKnowsAbout()
    {
        var store = new SplitNameStore(Path_);
        var applied = store.ApplyShortNames();

        Assert.Equal("Cinder", applied["Soul of Cinder"]);
        Assert.Equal("Twin Princes", applied["Lothric, Younger Prince"]);
        Assert.Equal("Anri", applied["Anri of Astora"]);

        // Every boss the editor can offer is either shortened or already short.
        foreach (var split in BuiltInRoutes.Catalogue)
            Assert.True(applied.ContainsKey(split.Name) || split.Name.Length <= 14, split.Name);
    }

    [Fact]
    public void ShortNamesKeepARenameAlreadySet()
    {
        var store = new SplitNameStore(Path_);
        store.Update(Map(("Iudex Gundyr", "Tutorial")));

        var applied = store.ApplyShortNames();

        // A name chosen by hand outranks the preset, which covers this boss too.
        Assert.Equal("Tutorial", applied["Iudex Gundyr"]);
        Assert.Equal("Cinder", applied["Soul of Cinder"]);
    }

    [Fact]
    public void ClearingRemovesEverything()
    {
        var store = new SplitNameStore(Path_);
        store.ApplyShortNames();
        store.Clear();

        Assert.Empty(store.All);
        Assert.Empty(new SplitNameStore(Path_).All);
    }

    [Fact]
    public void ACorruptFileShowsTheCanonicalNamesRatherThanNone()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path_, "{ not json at all");

        Assert.Empty(new SplitNameStore(Path_).All);
    }
}
