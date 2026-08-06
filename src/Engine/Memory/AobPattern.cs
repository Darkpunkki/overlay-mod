namespace OverlayMod.Engine.Memory;

/// <summary>
/// An array-of-bytes signature with wildcard support, e.g. "48 8B 0D ?? ?? ?? ??".
/// "?" or "??" tokens are wildcards. Used to locate code that references the
/// static structures we care about, so we survive ASLR and minor relocations.
/// </summary>
public sealed class AobPattern
{
    private readonly byte[] _bytes;
    private readonly bool[] _compare; // true = must match, false = wildcard

    public int Length => _bytes.Length;
    public string Text { get; }

    public AobPattern(string pattern)
    {
        Text = pattern;
        var tokens = pattern.Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        _bytes = new byte[tokens.Length];
        _compare = new bool[tokens.Length];
        for (var i = 0; i < tokens.Length; i++)
        {
            var t = tokens[i];
            if (t is "?" or "??")
            {
                _compare[i] = false;
                _bytes[i] = 0;
            }
            else
            {
                _compare[i] = true;
                _bytes[i] = Convert.ToByte(t, 16);
            }
        }
    }

    /// <summary>Index of the first match in <paramref name="data"/>, or -1 if not found.</summary>
    public int IndexIn(ReadOnlySpan<byte> data, int start = 0)
    {
        var n = _bytes.Length;
        if (n == 0) return -1;
        var last = data.Length - n;
        for (var i = start; i <= last; i++)
        {
            var ok = true;
            for (var j = 0; j < n; j++)
            {
                if (_compare[j] && data[i + j] != _bytes[j])
                {
                    ok = false;
                    break;
                }
            }
            if (ok) return i;
        }
        return -1;
    }
}
