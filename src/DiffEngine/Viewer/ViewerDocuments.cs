namespace DiffEngine;

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

    /// <summary>
    /// The same question of a viewer that has already been resolved, answered from what resolving
    /// it concluded rather than by looking at the disk again: it was given the routed extensions
    /// exactly when <see cref="Beside" /> held. Asked once per file derived from a document, which
    /// a test with a hundred pages asks a hundred times.
    /// <para>
    /// It is also the only way to ask about a file the viewer reaches as the text tool. An SVG is
    /// routed nowhere new, so the extension that resolved the viewer for it says nothing about
    /// whether that copy draws it.
    /// </para>
    /// </summary>
    public static bool ReadBy(ResolvedTool viewer) =>
        viewer.BinaryExtensions.Contains(DocumentExtensions.Paged[0]);
}
