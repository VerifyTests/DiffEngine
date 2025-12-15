if (args.Length != 2)
{
    Console.Error.WriteLine("Usage: diffengine-word <path1> <path2>");
    return 1;
}

var path1 = Path.GetFullPath(args[0]);
var path2 = Path.GetFullPath(args[1]);

if (!File.Exists(path1))
{
    Console.Error.WriteLine($"File not found: {path1}");
    return 1;
}

if (!File.Exists(path2))
{
    Console.Error.WriteLine($"File not found: {path2}");
    return 1;
}

var wordType = Type.GetTypeFromProgID("Word.Application");
if (wordType == null)
{
    Console.Error.WriteLine("Microsoft Word is not installed");
    return 1;
}

dynamic word = Activator.CreateInstance(wordType)!;

var doc1 = word.Documents.Open(path1, ReadOnly: true, AddToRecentFiles: false);
var doc2 = word.Documents.Open(path2, ReadOnly: true, AddToRecentFiles: false);

// WdCompareDestination.wdCompareDestinationNew = 2
// WdGranularity.wdGranularityWordLevel = 1
word.CompareDocuments(
    doc1, doc2,
    Destination: 2,
    Granularity: 1,
    CompareFormatting: true,
    CompareCaseChanges: true,
    CompareWhitespace: true,
    CompareTables: true,
    CompareHeaders: true,
    CompareFootnotes: true,
    CompareTextboxes: true,
    CompareFields: true,
    CompareComments: true,
    CompareMoves: true,
    RevisedAuthor: "",
    IgnoreAllComparisonWarnings: true);

doc1.Close(SaveChanges: false);
doc2.Close(SaveChanges: false);

word.Visible = true;

return 0;
