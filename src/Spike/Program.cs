using System.Globalization;
using OverlayMod.Engine.GameState;

// Milestone 1 spike: attach to a running (offline, EAC-disabled) Dark Souls III
// and stream the values the overlay will be built on. This is the de-risking
// gate - if these track reality on your machine, the memory foundation is sound.
//
// Usage: launch DS3 offline, then run this. Ctrl+C to quit.
// Verify while it runs:
//   * IGT ticks up during play and pauses on loading screens.
//   * PlayerLoaded flips true in a level, false at the main menu.
//   * HP drops when you take a hit (confirms the seeded HP chain).
//   * Position changes as you move.

Console.OutputEncoding = System.Text.Encoding.UTF8;
Console.WriteLine("OverlayMod - DS3 memory spike");
Console.WriteLine("Waiting for Dark Souls III (run it offline with EAC disabled)...");
Console.WriteLine("Press Ctrl+C to exit.\n");

using var reader = new Ds3Reader();
var quit = false;
Console.CancelKeyPress += (_, e) => { e.Cancel = true; quit = true; };

var wasAttached = false;

while (!quit)
{
    if (!reader.Attached)
    {
        if (!reader.Attach())
        {
            if (wasAttached)
            {
                Console.WriteLine("\nLost the game process. Waiting for it to come back...");
                wasAttached = false;
            }
            Thread.Sleep(1000);
            continue;
        }

        wasAttached = true;
        Console.WriteLine($"Attached. Game version {reader.Version} "
                          + $"(IGT@+0x{reader.Version.IgtOffset:X}, moduleBag@+0x{reader.Version.ModuleBagOffset:X}).");
        Console.WriteLine($"  WorldChrMan = 0x{reader.WorldChrMan:X}");
        Console.WriteLine($"  PlayerIns   = 0x{reader.PlayerInsAddress:X}\n");
        Console.WriteLine("   IGT          load  player   HP         position (x, y, z)              IudexGundyr");
        Console.WriteLine(new string('-', 92));
    }

    var s = reader.TakeSnapshot();
    var iudexDead = reader.ReadEventFlag(14000800); // Iudex Gundyr defeat flag
    var line = string.Format(
        CultureInfo.InvariantCulture,
        "{0,-11}  {1,-4}  {2,-6}  {3,-9}  {4,8:0.0}, {5,8:0.0}, {6,8:0.0}    {7}",
        FormatIgt(s.IgtMs),
        s.IsLoading ? "yes" : "no",
        s.PlayerLoaded ? "yes" : "no",
        s.PlayerLoaded ? $"{s.Hp}/{s.MaxHp}" : "-",
        s.X, s.Y, s.Z,
        iudexDead ? "DEAD" : "alive");

    if (Console.IsOutputRedirected)
    {
        // Piped/redirected: no cursor control - emit plain lines, slower cadence.
        Console.WriteLine(line);
        Thread.Sleep(1000);
    }
    else
    {
        var width = SafeWidth();
        Console.Write("\r" + (line.Length > width ? line[..width] : line.PadRight(width)));
        Thread.Sleep(250);
    }
}

Console.WriteLine("\nBye.");
return;

static int SafeWidth()
{
    try { return Console.WindowWidth > 1 ? Console.WindowWidth - 1 : 80; }
    catch { return 80; }
}

static string FormatIgt(int ms)
{
    if (ms <= 0) return "0:00.000";
    var t = TimeSpan.FromMilliseconds(ms);
    return t.Hours > 0
        ? $"{t.Hours}:{t.Minutes:00}:{t.Seconds:00}.{t.Milliseconds:000}"
        : $"{t.Minutes}:{t.Seconds:00}.{t.Milliseconds:000}";
}
