/// <summary>
/// Holds the managed mirrors in <c>DeviewStructs.cs</c> against native/include/deview.h, which is
/// the thing they mirror.
/// <para>
/// The header is read rather than trusted because the two sides are edited separately, in
/// different languages, and a field added to one and not the other is not a compile error
/// anywhere: it is a frame decoded at the wrong offsets, on a platform this cannot be run on. The
/// version constant is checked for the same reason — bumping the header and forgetting
/// <see cref="Deview.ExpectedVersion"/> would ship a library that loads and then reads garbage.
/// </para>
/// <para>
/// Runs everywhere, including Windows, where there is no native renderer at all. That is the
/// point: the ABI is the one part of the native heads a machine with no toolchain can still check.
/// </para>
/// </summary>
public class DeviewStructTests
{
    [Test]
    public async Task VersionMatchesTheHeader()
    {
        var match = Regex.Match(Header(), @"^#define DEVIEW_VERSION (\d+)$", RegexOptions.Multiline);
        await Assert.That(match.Success).IsTrue();
        await Assert.That(int.Parse(match.Groups[1].Value)).IsEqualTo(Deview.ExpectedVersion);
    }

    [Test]
    [MethodDataSource(nameof(Structs))]
    public async Task FieldsMatchTheHeader(string name, Type managed)
    {
        var declared = Fields(name);
        var mirrored = managed
            .GetFields(BindingFlags.Public | BindingFlags.Instance)
            .Select(_ => _.Name)
            .ToList();

        // Joined rather than compared as collections, so the assertion is unambiguously ordered:
        // these are read by offset, and a field in the wrong place is as wrong as a missing one
        // while looking identical from the managed side.
        await Assert.That(string.Join(", ", Camel(mirrored))).IsEqualTo(string.Join(", ", declared));
    }

    /// <summary>
    /// A row kind crosses the ABI as a cast rather than through a mapping, so the two enums have to
    /// agree value for value. A kind added to one side only is drawn as whatever the other side has
    /// at that number, and nothing fails anywhere.
    /// </summary>
    [Test]
    public async Task RowKindsMatchTheHeader() =>
        await Assert.That(Managed<RowKind>()).IsEqualTo(Declared("DeviewRowKind", "DEVIEW_ROW_"));

    /// <summary>
    /// Keys are mapped rather than cast, because the shim reports only the ones a window can
    /// produce, but the numbers are still the wire format and both sides have to use the same.
    /// </summary>
    [Test]
    public async Task KeysMatchTheHeader() =>
        await Assert.That(Managed<DeviewKey>()).IsEqualTo(Declared("DeviewKey", "DEVIEW_KEY_"));

    public static IEnumerable<(string, Type)> Structs()
    {
        yield return ("DeviewRow", typeof(DeviewRow));
        yield return ("DeviewPane", typeof(DeviewPane));
        yield return ("DeviewButton", typeof(DeviewButton));
        yield return ("DeviewQueueItem", typeof(DeviewQueueItem));
        yield return ("DeviewMenuItem", typeof(DeviewMenuItem));
        yield return ("DeviewScreen", typeof(DeviewScreen));
        yield return ("DeviewInput", typeof(DeviewInput));
    }

    /// <summary>
    /// The header names fields in camel case and the mirrors in Pascal, which is the only
    /// difference the two are allowed to have.
    /// </summary>
    static List<string> Camel(IEnumerable<string> names) =>
        names
            .Select(_ => char.ToLowerInvariant(_[0]) + _[1..])
            .ToList();

    static List<string> Fields(string name)
    {
        var block = Regex.Match(
            Header(),
            $@"typedef struct {name} \{{(?<body>.*?)\}} {name};",
            RegexOptions.Singleline);
        if (!block.Success)
        {
            throw new($"{name} is not declared in deview.h.");
        }

        // Comments first: they carry braces, semicolons and the word const, and half of them
        // describe the very field they precede.
        var body = Regex.Replace(block.Groups["body"].Value, @"/\*.*?\*/", "", RegexOptions.Singleline);

        // A declaration is a type, an optional pointer star, and a name. The type is not compared:
        // int32_t against int and const T* against T* are the mapping, not a mismatch, and a
        // wrong one shows up as a size the runtime marshaller would reject anyway.
        return Regex
            .Matches(body, @"(?:const\s+)?\w+\s*\*?\s*(?<field>\w+)\s*;")
            .Select(_ => _.Groups["field"].Value)
            .ToList();
    }

    /// <summary>
    /// An enum as name and value pairs in value order, the names lower cased and run together so
    /// <c>ScrollUp</c> and <c>DEVIEW_KEY_SCROLL_UP</c> compare as the same member.
    /// </summary>
    static string Managed<T>()
        where T : struct, Enum =>
        string.Join(
            ", ",
            Enum.GetValues<T>()
                .OrderBy(_ => Convert.ToInt32(_))
                .Select(_ => $"{_.ToString().ToLowerInvariant()}={Convert.ToInt32(_)}"));

    static string Declared(string name, string prefix)
    {
        var block = Regex.Match(
            Header(),
            $@"enum {name} \{{(?<body>.*?)\}};",
            RegexOptions.Singleline);
        if (!block.Success)
        {
            throw new($"{name} is not declared in deview.h.");
        }

        var body = Regex.Replace(block.Groups["body"].Value, @"/\*.*?\*/", "", RegexOptions.Singleline);
        return string.Join(
            ", ",
            Regex
                .Matches(body, @"(?<member>\w+)\s*=\s*(?<value>\d+)")
                .Select(_ => (
                    Member: _.Groups["member"].Value[prefix.Length..].Replace("_", "").ToLowerInvariant(),
                    Value: int.Parse(_.Groups["value"].Value)))
                .OrderBy(_ => _.Value)
                .Select(_ => $"{_.Member}={_.Value}"));
    }

    static string Header()
    {
        // bin/{configuration}/{tfm} under this test project, so four up is src and five is the
        // repository. Matches the walk ManualViewer uses to find a head.
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        var repository = directory.Parent!.Parent!.Parent!.Parent!.Parent!;
        return File.ReadAllText(Path.Combine(repository.FullName, "native", "include", "deview.h"));
    }
}
