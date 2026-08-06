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

    public string OverlayUrl => $"http://127.0.0.1:{Port}/overlay/";

    public static OverlayHostOptions Parse(string[] args)
    {
        var port = 8777;
        var pollHz = 30;
        var fake = false;

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
                case "--help" or "-h":
                    PrintUsage();
                    Environment.Exit(0);
                    break;
            }
        }

        return new OverlayHostOptions { Port = port, PollHz = pollHz, UseFake = fake };
    }

    private static void PrintUsage() => Console.WriteLine(
        """
        OverlayMod host - serves the overlay and streams live run state.

          --fake           Replay the scripted demo run; no game needed.
          --port <n>       Port to listen on (default 8777).
          --poll-hz <n>    Engine poll rate, 1-120 (default 30).
          --help           Show this message.
        """);
}
