/// <summary>
/// Whether a resolved DiffEngineViewer carries its documents folder, which is what lets it read
/// PDFs and Office files. The dotnet tool and the copy inside DiffEngineTray do; the copy bundled
/// in DiffEngine does not, to stay small, and neither does any copy from before there was one.
/// <para>
/// Decided per resolved executable rather than by which kind of copy it is, so a tool installed
/// before the folder existed is not offered documents it would show as text.
/// </para>
/// </summary>
static class ViewerDocuments
{
    const string assembly = "DiffEngineViewer.Documents.dll";

    public static bool Beside(string executable)
    {
        var directory = Path.GetDirectoryName(executable);
        if (directory == null)
        {
            return false;
        }

        // Beside the viewer is the tool package's layout once it is unpacked. One up is the tray's,
        // which shares one folder between the copy it bundles for each RID.
        if (File.Exists(Path.Combine(directory, "documents", assembly)) ||
            File.Exists(Path.Combine(directory, "..", "documents", assembly)))
        {
            return true;
        }

        // A global or tool-path install resolves to the shim, and the package it runs is unpacked
        // under .store beside it.
        return WildcardFileFinder.TryFind(
            Path.Combine(directory, ".store", "diffengineviewer.*", "*", "diffengineviewer.*", "*", "tools", "*", "any", "documents", assembly),
            out _);
    }
}
