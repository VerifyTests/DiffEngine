using System.Runtime.InteropServices;

static partial class Program
{
    static int Main(string[] args)
    {
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

        // WdAlertLevel.wdAlertsNone = 0
        word.DisplayAlerts = 0;

        var doc1 = word.Documents.Open(path1, ReadOnly: true, AddToRecentFiles: false);
        var doc2 = word.Documents.Open(path2, ReadOnly: true, AddToRecentFiles: false);

        // WdCompareDestination.wdCompareDestinationNew = 2
        // WdGranularity.wdGranularityWordLevel = 1
        var comparedDoc = word.CompareDocuments(
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

        // Mark as saved so Word won't prompt to save on close
        comparedDoc.Saved = true;

        // Disable spelling and grammar checks
        comparedDoc.ShowSpellingErrors = false;
        comparedDoc.ShowGrammaticalErrors = false;

        word.Visible = true;

        // Get process from Word's window handle
        var hwnd = (IntPtr)word.ActiveWindow.Hwnd;
        GetWindowThreadProcessId(hwnd, out var processId);
        using var process = Process.GetProcessById(processId);
        process.WaitForExit();

        // Release COM objects
        Marshal.ReleaseComObject(comparedDoc);
        Marshal.ReleaseComObject(word);

        return 0;
    }

    [LibraryImport("user32.dll")]
    internal static partial uint GetWindowThreadProcessId(IntPtr hWnd, out int processId);
}
