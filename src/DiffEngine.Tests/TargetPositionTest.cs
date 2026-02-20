public class TargetPositionTest
{
    [Test]
    [Arguments("true", true)]
    [Arguments("TRUE", true)]
    [Arguments("True", true)]
    [Arguments("tRuE", true)]
    [Arguments("false", false)]
    [Arguments("FALSE", false)]
    [Arguments("False", false)]
    [Arguments("fAlSe", false)]
    public async Task ParseTargetOnLeft_IsCaseInsensitive(string input, bool expected)
    {
        var result = TargetPosition.ParseTargetOnLeft(input);
        await Assert.That(result).IsEqualTo(expected);
    }

    [Test]
    public async Task ParseTargetOnLeft_NullReturnsNull()
    {
        var result = TargetPosition.ParseTargetOnLeft(null);
        await Assert.That(result).IsNull();
    }

    [Test]
    [Arguments("yes")]
    [Arguments("no")]
    [Arguments("1")]
    [Arguments("0")]
    [Arguments("")]
    [Arguments("invalid")]
    public async Task ParseTargetOnLeft_InvalidValueThrows(string input)
    {
        var exception = Assert.Throws<Exception>(() => TargetPosition.ParseTargetOnLeft(input));
        await Assert.That(exception.Message).Contains("Unable to parse Position");
        await Assert.That(exception.Message).Contains(input);
    }
}
