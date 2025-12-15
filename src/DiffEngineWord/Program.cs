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
word.Visible = true;

var doc1 = word.Documents.Open(path1);
var doc2 = word.Documents.Open(path2);

var window1 = doc1.ActiveWindow;
var window2 = doc2.ActiveWindow;

window2.Activate();
word.Windows.CompareSideBySideWith(window1);

return 0;
