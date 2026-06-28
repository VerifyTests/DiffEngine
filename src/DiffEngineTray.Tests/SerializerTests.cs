public class SerializerTests
{
    [Test]
    public async Task Deserialize_move_payload()
    {
        var result = Serializer.Deserialize<MovePayload>(
            """
            {
            "Type":"Move",
            "Temp":"theTemp",
            "Target":"theTarget",
            "CanKill":true,
            "Exe":"theExe",
            "Arguments":"theArgs",
            "ProcessId":10
            }
            """);

        await Assert.That(result.Temp).IsEqualTo("theTemp");
        await Assert.That(result.Target).IsEqualTo("theTarget");
        await Assert.That(result.Exe).IsEqualTo("theExe");
        await Assert.That(result.Arguments).IsEqualTo("theArgs");
        await Assert.That(result.CanKill).IsTrue();
        await Assert.That(result.ProcessId).IsEqualTo(10);
    }

    [Test]
    public async Task Deserialize_delete_payload()
    {
        var result = Serializer.Deserialize<DeletePayload>(
            """
            {
            "Type":"Delete",
            "File":"theFile"
            }
            """);

        await Assert.That(result.File).IsEqualTo("theFile");
    }

    [Test]
    public async Task Deserialize_invalid_payload_throws_with_payload_in_message()
    {
        const string payload = "this is not json";
        Exception? caught = null;
        try
        {
            Serializer.Deserialize<MovePayload>(payload);
        }
        catch (Exception exception)
        {
            caught = exception;
        }

        await Assert.That(caught).IsNotNull();
        await Assert.That(caught!.Message.Contains("Failed to Deserialize payload")).IsTrue();
        await Assert.That(caught.Message.Contains(payload)).IsTrue();
        await Assert.That(caught.InnerException).IsNotNull();
    }
}
