public class TargetPositionTest
{
    [Theory]
    [InlineData("true", true)]
    [InlineData("TRUE", true)]
    [InlineData("True", true)]
    [InlineData("tRuE", true)]
    [InlineData("false", false)]
    [InlineData("FALSE", false)]
    [InlineData("False", false)]
    [InlineData("fAlSe", false)]
    public void ParseTargetOnLeft_IsCaseInsensitive(string input, bool expected)
    {
        var result = TargetPosition.ParseTargetOnLeft(input);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void ParseTargetOnLeft_NullReturnsNull()
    {
        var result = TargetPosition.ParseTargetOnLeft(null);
        Assert.Null(result);
    }

    [Theory]
    [InlineData("yes")]
    [InlineData("no")]
    [InlineData("1")]
    [InlineData("0")]
    [InlineData("")]
    [InlineData("invalid")]
    public void ParseTargetOnLeft_InvalidValueThrows(string input)
    {
        var exception = Assert.Throws<Exception>(() => TargetPosition.ParseTargetOnLeft(input));
        Assert.Contains("Unable to parse Position", exception.Message);
        Assert.Contains(input, exception.Message);
    }
}
