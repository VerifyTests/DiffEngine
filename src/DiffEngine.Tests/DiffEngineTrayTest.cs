[TestFixture]
public class DiffEngineTrayTest
{
    [Test]
    public void IsRunning() =>
        False(DiffEngineTray.IsRunning);
}