using System.Runtime.CompilerServices;

static class ModuleInitializer
{
    /// <summary>
    /// Nothing here makes a window, and this is for the day something does. WinForms answers an
    /// exception thrown inside a window message with a dialog offering Continue and Quit, and a
    /// benchmark run would sit behind it until somebody clicked. Thrown instead, it ends the run.
    /// Here rather than in <c>Program</c> because it is refused once any window exists, and a
    /// module initializer runs before anything in the assembly can have made one. For the
    /// application, since the overload without threadScope sets it for the calling thread alone
    /// and a benchmark runs on whichever thread BenchmarkDotNet gives it.
    /// </summary>
    [ModuleInitializer]
    public static void Initialize() =>
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException, threadScope: false);
}
