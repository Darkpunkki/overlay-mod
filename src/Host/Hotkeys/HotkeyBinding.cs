namespace OverlayMod.Host.Hotkeys;

/// <summary>
/// A parsed global hotkey, e.g. "Ctrl+Alt+S". Modifier and key codes are the
/// Win32 values that <c>RegisterHotKey</c> expects.
/// </summary>
public sealed record HotkeyBinding(uint Modifiers, uint VirtualKey, string Text)
{
    // Win32 modifier flags.
    public const uint ModAlt = 0x0001;
    public const uint ModControl = 0x0002;
    public const uint ModShift = 0x0004;
    public const uint ModWin = 0x0008;

    /// <summary>Suppresses auto-repeat, so holding the key splits once rather than continuously.</summary>
    public const uint ModNoRepeat = 0x4000;

    /// <summary>
    /// Parse a combination like "Ctrl+Alt+S" or "F9". Returns false for anything
    /// unrecognised rather than throwing — this comes from a hand-edited config
    /// file, and one bad line should not stop the host from starting.
    /// </summary>
    public static bool TryParse(string? text, out HotkeyBinding binding)
    {
        binding = null!;
        if (string.IsNullOrWhiteSpace(text)) return false;

        uint modifiers = 0;
        uint? key = null;

        foreach (var raw in text.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            switch (raw.ToLowerInvariant())
            {
                case "ctrl" or "control": modifiers |= ModControl; break;
                case "alt": modifiers |= ModAlt; break;
                case "shift": modifiers |= ModShift; break;
                case "win" or "windows" or "meta": modifiers |= ModWin; break;
                default:
                    if (key is not null) return false;   // two non-modifier keys
                    if (!TryParseKey(raw, out var vk)) return false;
                    key = vk;
                    break;
            }
        }

        if (key is null) return false;

        binding = new HotkeyBinding(modifiers | ModNoRepeat, key.Value, Normalise(text));
        return true;
    }

    private static bool TryParseKey(string name, out uint virtualKey)
    {
        virtualKey = 0;

        if (name.Length == 1)
        {
            var c = char.ToUpperInvariant(name[0]);
            if (c is >= 'A' and <= 'Z') { virtualKey = c; return true; }
            if (c is >= '0' and <= '9') { virtualKey = c; return true; }
            return false;
        }

        // Function keys: VK_F1 is 0x70 and they run consecutively to F24.
        if (name.Length is 2 or 3 &&
            (name[0] is 'f' or 'F') &&
            int.TryParse(name[1..], out var n) &&
            n is >= 1 and <= 24)
        {
            virtualKey = (uint)(0x70 + n - 1);
            return true;
        }

        virtualKey = name.ToLowerInvariant() switch
        {
            "space" => 0x20,
            "enter" or "return" => 0x0D,
            "tab" => 0x09,
            "escape" or "esc" => 0x1B,
            "backspace" => 0x08,
            "insert" => 0x2D,
            "delete" or "del" => 0x2E,
            "home" => 0x24,
            "end" => 0x23,
            "pageup" => 0x21,
            "pagedown" => 0x22,
            "left" => 0x25,
            "up" => 0x26,
            "right" => 0x27,
            "down" => 0x28,
            "numpad0" => 0x60,
            "numpad1" => 0x61,
            "numpad2" => 0x62,
            "numpad3" => 0x63,
            "numpad4" => 0x64,
            "numpad5" => 0x65,
            "numpad6" => 0x66,
            "numpad7" => 0x67,
            "numpad8" => 0x68,
            "numpad9" => 0x69,
            _ => 0,
        };

        return virtualKey != 0;
    }

    /// <summary>Tidy casing so the control page shows "Ctrl+Alt+S" however it was typed.</summary>
    private static string Normalise(string text)
    {
        var parts = text.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        for (var i = 0; i < parts.Length; i++)
        {
            var p = parts[i];
            parts[i] = p.Length == 1
                ? p.ToUpperInvariant()
                : char.ToUpperInvariant(p[0]) + p[1..].ToLowerInvariant();
        }
        return string.Join("+", parts);
    }
}
