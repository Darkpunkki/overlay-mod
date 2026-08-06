using OverlayMod.Host.Hotkeys;
using Xunit;

namespace OverlayMod.Engine.Tests;

/// <summary>
/// Hotkey strings come from a hand-edited config file, so parsing has to be
/// forgiving about formatting and firm about nonsense.
/// </summary>
public class HotkeyBindingTests
{
    private const uint VkS = 0x53;
    private const uint VkF9 = 0x78;

    [Fact]
    public void ParsesModifiersAndKey()
    {
        Assert.True(HotkeyBinding.TryParse("Ctrl+Alt+S", out var b));

        Assert.Equal(VkS, b.VirtualKey);
        Assert.True((b.Modifiers & HotkeyBinding.ModControl) != 0);
        Assert.True((b.Modifiers & HotkeyBinding.ModAlt) != 0);
        Assert.False((b.Modifiers & HotkeyBinding.ModShift) != 0);
    }

    [Fact]
    public void AlwaysSuppressesAutoRepeat()
    {
        Assert.True(HotkeyBinding.TryParse("F9", out var b));

        // Holding the key down must split once, not continuously.
        Assert.True((b.Modifiers & HotkeyBinding.ModNoRepeat) != 0);
    }

    [Theory]
    [InlineData("f9")]
    [InlineData("F9")]
    [InlineData(" F9 ")]
    public void IsForgivingAboutCasingAndSpacing(string text)
    {
        Assert.True(HotkeyBinding.TryParse(text, out var b));
        Assert.Equal(VkF9, b.VirtualKey);
    }

    [Fact]
    public void NormalisesTheDisplayText()
    {
        Assert.True(HotkeyBinding.TryParse("ctrl+alt+s", out var b));
        Assert.Equal("Ctrl+Alt+S", b.Text);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("Ctrl")]              // modifier with no key
    [InlineData("Ctrl+A+B")]          // two real keys
    [InlineData("Ctrl+Nonsense")]
    [InlineData("F25")]               // past the end of the function keys
    public void RejectsWhatItCannotBind(string? text)
    {
        Assert.False(HotkeyBinding.TryParse(text, out _));
    }

    [Theory]
    [InlineData("Space", 0x20u)]
    [InlineData("Numpad0", 0x60u)]
    [InlineData("Delete", 0x2Eu)]
    [InlineData("F24", 0x87u)]
    [InlineData("7", 0x37u)]
    public void UnderstandsNamedAndNumericKeys(string text, uint expected)
    {
        Assert.True(HotkeyBinding.TryParse(text, out var b));
        Assert.Equal(expected, b.VirtualKey);
    }
}
