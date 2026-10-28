using System.Buffers.Binary;

public class IconPixelsTests
{
    /// <summary>
    /// A bitmap in an icon is stored from the bottom row up, blue first, and a panel takes the
    /// pixels from the top row down, alpha first. Four pixels that all differ, so a row or a
    /// channel in the wrong place shows. The tray's own icons are symmetrical, and could not.
    /// </summary>
    [Test]
    public async Task RowsFromTheTopAndAlphaFirst()
    {
        byte[] stored =
        [
            // The bottom row: blue, then green
            255, 0, 0, 255, 0, 255, 0, 255,
            // The top row: red, then white half seen through
            0, 0, 255, 255, 255, 255, 255, 128
        ];

        byte[] expected =
        [
            // Red, then white half seen through
            255, 255, 0, 0, 128, 255, 255, 255,
            // Blue, then green
            255, 0, 0, 255, 255, 0, 255, 0
        ];

        var pixels = IconPixels.FromIco(Ico(2, stored), 2)!;

        // As text, so the order counts and a failure says where
        await Assert.That(Convert.ToHexString(pixels)).IsEqualTo(Convert.ToHexString(expected));
    }

    /// <summary>
    /// The tray's two icons, each a 48 pixel bitmap, by the colour they are mostly: the active one
    /// is red, which a red and blue swapped would make blue.
    /// </summary>
    [Test]
    [Arguments("default.ico", "#FFFFFF")]
    [Arguments("active.ico", "#CC0000")]
    public async Task TheTraysIcons(string name, string mostSeen)
    {
        var pixels = IconPixels.FromIco(await File.ReadAllBytesAsync(Image(name)), 48)!;

        await Assert.That(pixels.Length).IsEqualTo(48 * 48 * 4);
        await Assert.That(MostSeen(pixels)).IsEqualTo(mostSeen);
    }

    /// <summary>
    /// The icons have no other size, and a panel scales the one they have.
    /// </summary>
    [Test]
    public async Task NoOtherSize() =>
        await Assert.That(IconPixels.FromIco(await File.ReadAllBytesAsync(Image("default.ico")), 16)).IsNull();

    static string MostSeen(byte[] pixels) =>
        pixels
            .Chunk(4)
            .Where(_ => _[0] == 255)
            .Select(_ => $"#{_[1]:X2}{_[2]:X2}{_[3]:X2}")
            .GroupBy(_ => _)
            .MaxBy(_ => _.Count())!
            .Key;

    /// <summary>
    /// An icon of one 32 bit bitmap, with only what <see cref="IconPixels" /> reads filled in.
    /// </summary>
    static byte[] Ico(int size, byte[] bgra)
    {
        const int directory = 6 + 16;
        const int header = 40;
        var mask = (size + 31) / 32 * 4 * size;
        var ico = new byte[directory + header + bgra.Length + mask];
        BinaryPrimitives.WriteUInt16LittleEndian(ico.AsSpan(2), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(ico.AsSpan(4), 1);
        ico[6] = (byte) size;
        ico[7] = (byte) size;
        BinaryPrimitives.WriteInt32LittleEndian(ico.AsSpan(6 + 12), directory);
        BinaryPrimitives.WriteInt32LittleEndian(ico.AsSpan(directory), header);
        BinaryPrimitives.WriteInt32LittleEndian(ico.AsSpan(directory + 4), size);
        BinaryPrimitives.WriteInt32LittleEndian(ico.AsSpan(directory + 8), size * 2);
        BinaryPrimitives.WriteUInt16LittleEndian(ico.AsSpan(directory + 12), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(ico.AsSpan(directory + 14), 32);
        bgra.CopyTo(ico, directory + header);
        return ico;
    }

    static string Image(string name, [CallerFilePath] string source = "") =>
        Path.Combine(Path.GetDirectoryName(source)!, "..", "DiffEngineTray", "Images", name);
}
