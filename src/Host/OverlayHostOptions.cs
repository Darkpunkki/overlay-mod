namespace OverlayMod.Host;

/// <summary>Command-line configuration for the overlay host.</summary>
public sealed class OverlayHostOptions
{
    /// <summary>Loopback port the overlay and OBS connect to.</summary>
    public int Port { get; init; } = 8777;

    /// <summary>Poll rate for the engine loop, in Hz.</summary>
    public int PollHz { get; init; } = 30;

    /// <summary>Drive from the scripted demo run instead of a live game.</summary>
    public bool UseFake { get; init; }

    /// <summary>Where run history and the in-progress run checkpoint are kept.</summary>
    public string DataDirectory { get; init; } = "appdata";

    /// <summary>Skip registering global hotkeys, whatever the config file says.</summary>
    public bool NoHotkeys { get; init; }

    public string OverlayUrl => $"http://127.0.0.1:{Port}/overlay/";

    public string RecordsPath => Path.Combine(DataDirectory, "records.json");

    public string RunStatePath => Path.Combine(DataDirectory, "run-state.json");

    public string SettingsPath => Path.Combine(DataDirectory, "settings.json");

    public string RoutesDirectory => Path.Combine(DataDirectory, "routes");

    public string HotkeysPath => Path.Combine(DataDirectory, "hotkeys.json");

    public string ControlUrl => $"http://127.0.0.1:{Port}/control/";

    public static OverlayHostOptions Parse(string[] args)
    {
        var port = 8777;
        var pollHz = 30;
        var fake = false;
        var dataDirectory = "appdata";
        var noHotkeys = false;

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--fake":
                    fake = true;
                    break;
                case "--port" when i + 1 < args.Length && int.TryParse(args[i + 1], out var p):
                    port = p;
                    i++;
                    break;
                case "--poll-hz" when i + 1 < args.Length && int.TryParse(args[i + 1], out var hz):
                    pollHz = Math.Clamp(hz, 1, 120);
                    i++;
                    break;
                case "--data" when i + 1 < args.Length:
                    dataDirectory = args[i + 1];
                    i++;
                    break;
                case "--no-hotkeys":
                    noHotkeys = true;
                    break;
                case "--help" or "-h":
                    PrintUsage();
                    Environment.Exit(0);
                    break;
            }
        }

        return new OverlayHostOptions
        {
            Port = port,
            PollHz = pollHz,
            UseFake = fake,
            DataDirectory = dataDirectory,
            NoHotkeys = noHotkeys,
        };
    }

    private static void PrintUsage() => Console.WriteLine(
        """
        OverlayMod host - serves the overlay and streams live run state.

          --fake           Replay the scripted demo run; no game needed.
          --port <n>       Port to listen on (default 8777).
          --poll-hz <n>    Engine poll rate, 1-120 (default 30).
          --data <dir>     Run history and checkpoints (default ./appdata).
          --no-hotkeys     Do not register global hotkeys.
          --help           Show this message.
        """);
}
