using System.Runtime.InteropServices;
using DynamicIsland.Settings;
using Xunit;

namespace DynamicIsland.Tests;

/// <summary>The settings app's Preview buttons talk to the island with WM_COPYDATA; only our tagged messages count.</summary>
public class IslandMessagesTests
{
    [Fact]
    public void Reads_a_tagged_message()
    {
        Assert.Equal("preview:Battery", WithCopyData(IslandMessages.Tag, "preview:Battery\0", IslandMessages.Read));
    }

    [Fact]
    public void Ignores_messages_from_other_programs()
    {
        Assert.Null(WithCopyData(1234, "preview:Battery\0", IslandMessages.Read));
        Assert.Null(IslandMessages.Read(IntPtr.Zero));
    }

    [Fact]
    public void Dark_is_the_default_theme()
    {
        Assert.False(IslandTheme.IsLight(new AppSettings().Theme));
        Assert.True(IslandTheme.IsLight(IslandTheme.Light));
        Assert.False(IslandTheme.IsLight("something else"));
    }

    /// <summary>Lays out a COPYDATASTRUCT (dwData, cbData, lpData) the way Windows hands it to the island.</summary>
    private static string? WithCopyData(int tag, string text, Func<IntPtr, string?> read)
    {
        var bytes = System.Text.Encoding.Unicode.GetBytes(text);
        var payload = Marshal.AllocHGlobal(bytes.Length);
        var header = Marshal.AllocHGlobal(IntPtr.Size * 3);
        try
        {
            Marshal.Copy(bytes, 0, payload, bytes.Length);
            Marshal.WriteIntPtr(header, 0, tag);
            Marshal.WriteInt32(header, IntPtr.Size, bytes.Length);
            Marshal.WriteIntPtr(header, IntPtr.Size * 2, payload);
            return read(header);
        }
        finally
        {
            Marshal.FreeHGlobal(header);
            Marshal.FreeHGlobal(payload);
        }
    }
}
