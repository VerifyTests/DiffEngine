public class AiCliDetectorTest(ITestOutputHelper output) :
    XunitContextBase(output)
{
    [Fact]
    public void Props()
    {
        // ReSharper disable UnusedVariable

        #region AiCliDetectorProps

        var isCopilot = AiCliDetector.IsCopilot;
        var isAider = AiCliDetector.IsAider;
        var isClaudeCode = AiCliDetector.IsClaudeCode;

        #endregion

        // ReSharper restore UnusedVariable
    }
}
