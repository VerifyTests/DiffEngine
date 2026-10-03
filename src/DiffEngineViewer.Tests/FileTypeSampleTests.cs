/// <summary>
/// The pairs <see cref="FileTypeLaunchTests"/> puts in front of a person, checked on every run:
/// those tests are explicit, so a sample that stopped being what its extension says would otherwise
/// only be found by someone looking at a window that shows nothing.
/// </summary>
public class FileTypeSampleTests :
    IDisposable
{
    [Test]
    [Arguments(".png", "Png", 240, 160)]
    [Arguments(".bmp", "Bmp", 240, 160)]
    [Arguments(".gif", "Gif", 240, 160)]
    [Arguments(".ico", "Ico", 64, 64)]
    [Arguments(".jpg", "Jpeg", 240, 160)]
    [Arguments(".webp", "Webp", 240, 160)]
    public async Task AnImageIsWhatItsExtensionSays(string extension, string format, int width, int height)
    {
        var red = SampleImages.Build(extension, 220, 40, 40);
        var blue = SampleImages.Build(extension, 40, 80, 220);
        await Assert.That(ImageHeader.TryRead(red, out var header)).IsTrue();
        await Assert.That(header).IsEqualTo(new(Enum.Parse<ImageFormat>(format), width, height));
        await Assert.That(red).IsNotEquivalentTo(blue);
    }

    [Test]
    [Arguments(".docx", "Hello World!")]
    [Arguments(".xlsx", "Dulce")]
    [Arguments(".pptx", "Hello, PowerPoint!")]
    public async Task AnOfficeSampleReadsWithItsChange(string extension, string text)
    {
        var path = Write($"edited{extension}", FileTypeLaunchTests.Edited(extension, text, $"{text} CHANGED"));
        await Assert.That(Plugin.Text(path)).Contains($"{text} CHANGED");
    }

    [Test]
    [Arguments(".geojson")]
    [Arguments(".topojson")]
    [Arguments(".kml")]
    [Arguments(".gpx")]
    [Arguments(".wkt")]
    [Arguments(".kmz")]
    [Arguments(".wkb")]
    [Arguments(".fgb")]
    [Arguments(".geoparquet")]
    public async Task AMapDrawsDifferentlyOnceMoved(string extension)
    {
        var moved = Render(Write($"moved{extension}", FileTypeLaunchTests.MapOf(extension, moved: true)));
        var still = Render(Write($"still{extension}", FileTypeLaunchTests.MapOf(extension, moved: false)));
        await Assert.That(moved).IsNotEquivalentTo(still);
    }

    static DocumentPlugin Plugin { get; } = DocumentPlugin.Find(Path.Combine(AppContext.BaseDirectory, "documents"))!;

    byte[] Render(string path)
    {
        var pages = Directory.CreateDirectory(Path.Combine(directory, $"{Path.GetFileName(path)}-pages")).FullName;
        var landed = new List<string>();
        Plugin.Render(path, pages, landed.Add);
        return File.ReadAllBytes(landed.Single());
    }

    string Write(string name, byte[] content)
    {
        var path = Path.Combine(directory, name);
        File.WriteAllBytes(path, content);
        return path;
    }

    readonly string directory = Directory.CreateTempSubdirectory("deview-file-types-").FullName;

    public void Dispose() =>
        Directory.Delete(directory, true);
}
