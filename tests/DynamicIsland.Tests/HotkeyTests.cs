using DynamicIsland.Settings;
using Xunit;

namespace DynamicIsland.Tests;

public class HotkeyTests
{
    [Theory]
    [InlineData("Ctrl+Alt+I", Hotkey.MOD_CONTROL | Hotkey.MOD_ALT, 0x49u)]
    [InlineData("ctrl + shift + F5", Hotkey.MOD_CONTROL | Hotkey.MOD_SHIFT, 0x74u)]
    [InlineData("Win+Num1", Hotkey.MOD_WIN, 0x61u)]
    [InlineData("Alt+Space", Hotkey.MOD_ALT, 0x20u)]
    [InlineData("Ctrl+0x7B", Hotkey.MOD_CONTROL, 0x7Bu)]
    public void Parses_valid_shortcuts(string text, uint modifiers, uint key)
    {
        Assert.True(Hotkey.TryParse(text, out var m, out var k));
        Assert.Equal(modifiers, m);
        Assert.Equal(key, k);
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("I")]              // no modifier
    [InlineData("Ctrl+Alt")]       // no key
    [InlineData("Ctrl+A+B")]       // two keys
    [InlineData("Ctrl+NotAKey")]
    public void Rejects_invalid_shortcuts(string? text) => Assert.False(Hotkey.TryParse(text, out _, out _));

    [Theory]
    [InlineData("Ctrl+Alt+I")]
    [InlineData("Ctrl+Shift+F12")]
    [InlineData("Win+Num7")]
    [InlineData("Alt+Space")]
    [InlineData("Ctrl+0xE2")] // a key without a friendly name survives the round trip
    public void Format_and_parse_round_trip(string text)
    {
        Assert.True(Hotkey.TryParse(text, out var m, out var k));
        var formatted = Hotkey.Format(m, k);
        Assert.True(Hotkey.TryParse(formatted, out var m2, out var k2));
        Assert.Equal((m, k), (m2, k2));
    }

    [Fact]
    public void Formats_modifiers_in_a_fixed_order() =>
        Assert.Equal("Ctrl+Alt+Shift+Win+K", Hotkey.Format(Hotkey.MOD_WIN | Hotkey.MOD_SHIFT | Hotkey.MOD_ALT | Hotkey.MOD_CONTROL, 0x4B));
}
