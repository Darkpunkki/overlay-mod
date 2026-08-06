using System.Diagnostics;
using OverlayMod.Engine.Tracking;

namespace OverlayMod.Engine.GameState;

/// <summary>
/// One scripted change in a fake run. Any field left null keeps whatever value
/// the previous keyframe left in place, so a keyframe only states what changes.
/// </summary>
public sealed record FakeKeyframe(
    int AtMs,
    int? Hp = null,
    bool? PlayerLoaded = null,
    bool? IsLoading = null,
    bool? BossFightActive = null,
    uint? SetFlag = null);

/// <summary>
/// Replays a scripted run with no game attached, so the server and overlay can
/// be developed and tested without launching Dark Souls III.
///
/// The script is a piecewise-constant timeline: state at time <c>t</c> is the
/// accumulation of every keyframe at or before <c>t</c>. IGT is derived by
/// integrating non-loading time, which reproduces the real game's behaviour of
/// pausing the timer during loading screens.
///
/// Time comes from a wall clock by default (so a browser sees a run unfold in
/// real time) or from an injected function, which makes tests deterministic.
/// </summary>
public sealed class FakeSnapshotSource : ISnapshotSource, IFlagSource
{
    private readonly IReadOnlyList<FakeKeyframe> _script;
    private readonly int _loopMs;
    private readonly Func<int> _elapsedMs;
    private readonly int _maxHp;

    public FakeSnapshotSource(
        IReadOnlyList<FakeKeyframe>? script = null,
        int? loopMs = null,
        Func<int>? elapsedMs = null,
        int maxHp = 1050)
    {
        _script = script ?? DemoRun;
        _loopMs = loopMs ?? DemoRunLoopMs;
        _maxHp = maxHp;

        if (_script.Count == 0) throw new ArgumentException("Script must have at least one keyframe.", nameof(script));
        if (_loopMs <= 0) throw new ArgumentOutOfRangeException(nameof(loopMs), "Loop length must be positive.");

        if (elapsedMs != null)
        {
            _elapsedMs = elapsedMs;
        }
        else
        {
            var clock = Stopwatch.StartNew();
            _elapsedMs = () => (int)clock.ElapsedMilliseconds;
        }
    }

    public string Description => "fake source (scripted demo run, no game required)";

    public bool Attached => true;

    /// <summary>How many times the script has looped; drives the run reset between passes.</summary>
    public int Generation => _elapsedMs() / _loopMs;

    public bool Attach() => true;

    public IFlagSource Flags => this;

    /// <summary>Position within the current pass of the script.</summary>
    private int ScriptTimeMs => _elapsedMs() % _loopMs;

    public GameSnapshot TakeSnapshot()
    {
        var t = ScriptTimeMs;

        var hp = 0;
        var loaded = false;
        var loading = false;
        var boss = false;

        foreach (var k in _script)
        {
            if (k.AtMs > t) break;
            if (k.Hp is { } h) hp = h;
            if (k.PlayerLoaded is { } l) loaded = l;
            if (k.IsLoading is { } ld) loading = ld;
            if (k.BossFightActive is { } b) boss = b;
        }

        return new GameSnapshot
        {
            Attached = true,
            IgtMs = IgtAt(t),
            IsLoading = loading,
            PlayerLoaded = loaded,
            Hp = loaded ? hp : 0,
            MaxHp = loaded ? _maxHp : 0,
            BossFightActive = boss,
            X = 0,
            Y = 0,
            Z = 0,
        };
    }

    /// <summary>
    /// A flag is set once the script passes a keyframe that sets it, within the
    /// current pass. Flags clear when the script loops, so each pass is a fresh run.
    /// </summary>
    public bool IsEventFlagSet(uint flagId)
    {
        var t = ScriptTimeMs;
        foreach (var k in _script)
        {
            if (k.AtMs > t) break;
            if (k.SetFlag == flagId) return true;
        }
        return false;
    }

    /// <summary>
    /// In-game time at script position <paramref name="t"/>: elapsed time minus
    /// every loading window, since IGT pauses on loading screens.
    /// </summary>
    private int IgtAt(int t)
    {
        var igt = 0;
        var cursor = 0;
        var loading = false;

        foreach (var k in _script)
        {
            if (k.AtMs >= t) break;
            if (!loading) igt += k.AtMs - cursor;
            cursor = k.AtMs;
            if (k.IsLoading is { } ld) loading = ld;
        }

        if (!loading) igt += t - cursor;
        return igt;
    }

    public void Dispose() { }

    // --- The built-in demo run ---

    /// <summary>Length of one pass of <see cref="DemoRun"/>, including the tail pause.</summary>
    public const int DemoRunLoopMs = 110_000;

    /// <summary>
    /// A ~100 second scripted run over three boss splits, exercising everything
    /// the tracker and overlay need to display: loading screens, approach damage,
    /// estus heals, the approach-to-boss transition, a death and reload mid-run,
    /// a retry, and boss-defeat flags driving auto-splits through to a finish.
    ///
    /// Expected totals for one pass: 9 hits, 1 death, all three splits completed.
    /// </summary>
    public static readonly IReadOnlyList<FakeKeyframe> DemoRun = new[]
    {
        new FakeKeyframe(0,      Hp: 0,    PlayerLoaded: false, IsLoading: false, BossFightActive: false),
        new FakeKeyframe(1_000,  IsLoading: true),
        new FakeKeyframe(3_500,  Hp: 1050, PlayerLoaded: true,  IsLoading: false),

        // Split 1 - Iudex Gundyr: approach, then the fight.
        new FakeKeyframe(9_000,  Hp: 880),                       // hit
        new FakeKeyframe(14_000, Hp: 1050),                      // estus
        new FakeKeyframe(18_000, BossFightActive: true),
        new FakeKeyframe(22_000, Hp: 790),                       // hit
        new FakeKeyframe(27_000, Hp: 540),                       // hit
        new FakeKeyframe(31_000, SetFlag: 14000800),             // defeated -> auto-split

        // Split 2 - Vordt: approach, a death, then a successful retry.
        new FakeKeyframe(32_000, BossFightActive: false),
        new FakeKeyframe(38_000, Hp: 300),                       // hit
        new FakeKeyframe(43_000, Hp: 1050),                      // estus
        new FakeKeyframe(48_000, BossFightActive: true),
        new FakeKeyframe(52_000, Hp: 600),                       // hit
        new FakeKeyframe(57_000, Hp: 0),                         // hit + death
        new FakeKeyframe(59_000, PlayerLoaded: false, IsLoading: true),
        new FakeKeyframe(63_000, Hp: 1050, PlayerLoaded: true, IsLoading: false, BossFightActive: false),
        new FakeKeyframe(68_000, BossFightActive: true),
        new FakeKeyframe(72_000, Hp: 700),                       // hit
        new FakeKeyframe(77_000, SetFlag: 13000800),             // defeated -> auto-split

        // Split 3 - Curse-rotted Greatwood, cleanly.
        new FakeKeyframe(78_000, BossFightActive: false),
        new FakeKeyframe(84_000, Hp: 520),                       // hit
        new FakeKeyframe(90_000, BossFightActive: true),
        new FakeKeyframe(95_000, Hp: 310),                       // hit
        new FakeKeyframe(100_000, SetFlag: 13100800),            // defeated -> run finished
    };
}
