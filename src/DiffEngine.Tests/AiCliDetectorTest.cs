public class AiCliDetectorTest(ITestOutputHelper output) :
    XunitContextBase(output)
{
    [Fact]
    public void Props()
    {
        // ReSharper disable UnusedVariable

        #region AiCliDetectorProps

        var isCopilotCli = AiCliDetector.IsCopilotCli;
        var isAider = AiCliDetector.IsAider;
        var isClaudeCode = AiCliDetector.IsClaudeCode;

        #endregion

        // ReSharper restore UnusedVariable
    }
}
