namespace DiffEngine.Tests;

public class MaxInstanceTest :
    XunitContextBase
{
    const int ExpectedDefaultValue = 5;

    public MaxInstanceTest(ITestOutputHelper output) : base(output)
    {
    }

    [Fact]
    public void MaxInstancesToLaunch_ShouldReturnDefaultValue_WhenEnvironmentVariableAndAppDomainAreNotSet()
    {
        // arrange
        ClearDiffEngineMaxInstancesEnvironmentValue();

        // act
        var result = MaxInstance.MaxInstancesToLaunch;

        // assert
        Assert.Equal(ExpectedDefaultValue, result);
    }

    [Theory]
    [InlineData(7)]
    [InlineData(42)]
    public void MaxInstancesToLaunch_ShouldReturnAppDomainValue_WhenEnvironmentVariableIsNotSet(int appDomainValue)
    {
        // arrange
        ClearDiffEngineMaxInstancesEnvironmentValue();
        MaxInstance.SetForAppDomain(appDomainValue);

        // act
        var result = MaxInstance.MaxInstancesToLaunch;

        // assert
        Assert.Equal(appDomainValue, result);
    }

    [Theory]
    [InlineData(7)]
    [InlineData(42)]
    public void MaxInstancesToLaunch_ShouldReturnEnvironmentVariableValue_WhenSet(int envVariableValue)
    {
        // arrange
        SetDiffEngineMaxInstancesEnvironmentValue(envVariableValue);
        MaxInstance.SetForAppDomain(99);

        // act
        var result = MaxInstance.MaxInstancesToLaunch;

        // assert
        Assert.Equal(envVariableValue, result);
    }

    private static void SetDiffEngineMaxInstancesEnvironmentValue(int value) =>
        Environment.SetEnvironmentVariable("DiffEngine_MaxInstances", value.ToString());

    private static void ClearDiffEngineMaxInstancesEnvironmentValue() =>
        Environment.SetEnvironmentVariable("DiffEngine_MaxInstances", null);
}