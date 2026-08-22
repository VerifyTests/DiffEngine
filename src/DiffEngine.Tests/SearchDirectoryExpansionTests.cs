/// <summary>
/// Search directories are written in the one syntax that gets expanded.
/// <para>
/// WildcardFileFinder expands with Environment.ExpandEnvironmentVariables, which only understands
/// %NAME%. A directory written shell style with $HOME was passed through untouched, so it named a
/// literal directory called "$HOME" and never matched - which is why a globally installed
/// DiffEngineViewer was invisible on Linux and macOS whenever the bundled copy was absent.
/// </para>
/// </summary>
public class SearchDirectoryExpansionTests
{
    [Test]
    public async Task NoDefinitionUsesShellStyleExpansion()
    {
        var shellStyle = Definitions.Tools
            .SelectMany(
                definition => Directories(definition)
                    .Where(_ => _.Contains('$'))
                    .Select(_ => $"{definition.Tool}: {_}"))
            .ToList();

        await Assert.That(shellStyle).IsEmpty();
    }

    static IEnumerable<string> Directories(Definition definition)
    {
        var support = definition.OsSupport;
        foreach (var settings in new[] { support.Windows, support.Linux, support.Osx })
        {
            if (settings == null)
            {
                continue;
            }

            foreach (var directory in settings.SearchDirectories)
            {
                yield return directory;
            }
        }
    }
}
