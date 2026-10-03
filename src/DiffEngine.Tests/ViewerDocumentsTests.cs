/// <summary>
/// Which copies of the viewer DiffEngine offers documents to: those carrying their documents
/// folder, wherever the layout they were installed with puts it, and no other.
/// </summary>
[NotInParallel]
public class ViewerDocumentsTests :
    IDisposable
{
    /// <summary>
    /// A dotnet tool package's layout once unpacked, and a viewer run from a build's bin.
    /// </summary>
    [Test]
    public async Task Beside()
    {
        var viewer = Viewer("bin");
        Documents(Path.Combine(root, "bin", "documents"));
        await Assert.That(ViewerDocuments.Beside(viewer)).IsTrue();
    }

    /// <summary>
    /// The tray's layout: one folder shared by the copy it bundles for each RID.
    /// </summary>
    [Test]
    public async Task OneUp()
    {
        var viewer = Viewer(Path.Combine("viewer", "win-x64"));
        Documents(Path.Combine(root, "viewer", "documents"));
        await Assert.That(ViewerDocuments.Beside(viewer)).IsTrue();
    }

    /// <summary>
    /// A global install resolves to the shim, and the package it runs is unpacked under .store.
    /// </summary>
    [Test]
    public async Task InTheToolStore()
    {
        var viewer = Viewer("tools");
        Documents(Path.Combine(root, "tools", ".store", "diffengineviewer.windows", "20.6.0", "diffengineviewer.windows", "20.6.0", "tools", "net10.0", "any", "documents"));
        await Assert.That(ViewerDocuments.Beside(viewer)).IsTrue();
    }

    /// <summary>
    /// The copy bundled in DiffEngine, and any copy from before the folder existed.
    /// </summary>
    [Test]
    public async Task Nowhere()
    {
        var viewer = Viewer(Path.Combine("tools", "viewer", "win-x64"));
        await Assert.That(ViewerDocuments.Beside(viewer)).IsFalse();
    }

    [Test]
    [Arguments(".pdf")]
    [Arguments(".geojson")]
    [Arguments(".fgb")]
    public async Task DocumentsAreOfferedToACopyThatCanReadThem(string extension)
    {
        var viewer = Viewer("bin");
        Documents(Path.Combine(root, "bin", "documents"));

        await Assert.That(Detected(viewer, extension)).IsTrue();
    }

    [Test]
    [Arguments(".pdf")]
    [Arguments(".geojson")]
    [Arguments(".fgb")]
    public async Task DocumentsAreNotOfferedToACopyThatCannot(string extension)
    {
        var viewer = Viewer("bin");

        await Assert.That(Detected(viewer, extension)).IsFalse();
    }

    /// <summary>
    /// Through the resolution every launch takes, with the viewer found where the environment
    /// variable that overrides its location says.
    /// </summary>
    static bool Detected(string viewer, string extension)
    {
        Environment.SetEnvironmentVariable("DiffEngine_DiffEngineViewer", Path.GetDirectoryName(viewer));
        try
        {
            DiffTools.UseOrder(DiffTool.DiffEngineViewer);
            return DiffTools.IsDetectedForExtension(DiffTool.DiffEngineViewer, extension);
        }
        finally
        {
            Environment.SetEnvironmentVariable("DiffEngine_DiffEngineViewer", null);
        }
    }

    string Viewer(string directory)
    {
        var full = Path.Combine(root, directory);
        Directory.CreateDirectory(full);
        var name = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "DiffEngineViewer.exe" : "DiffEngineViewer";
        var path = Path.Combine(full, name);
        File.WriteAllText(path, "");
        return path;
    }

    static void Documents(string directory)
    {
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "DiffEngineViewer.Documents.dll"), "");
    }

    readonly string root = Path.Combine(Path.GetTempPath(), $"viewer-documents-{Guid.NewGuid():N}");

    public void Dispose()
    {
        DiffTools.Reset();
        if (Directory.Exists(root))
        {
            Directory.Delete(root, true);
        }
    }
}
