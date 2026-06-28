// One headless session for the whole assembly; the headless platform can only be initialized once.
public static class AvaloniaSession
{
    public static readonly HeadlessUnitTestSession Instance =
        HeadlessUnitTestSession.StartNew(typeof(TestApp));
}
