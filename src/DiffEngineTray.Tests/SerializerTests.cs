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
    public async Task Deserialize_payloads_naming_a_source()
    {
        var move = Serializer.Deserialize<MovePayload>(
            """
            {
            "Type":"Move",
            "Temp":"thePage",
            "Target":"theTarget",
            "CanKill":false,
            "Source":"theDocument"
            }
            """);
        var delete = Serializer.Deserialize<DeletePayload>(
            """
            {
            "Type":"Delete",
            "File":"theFile",
            "Source":"theDocument"
            }
            """);

        await Assert.That(move.Source).IsEqualTo("theDocument");
        await Assert.That(delete.Source).IsEqualTo("theDocument");
    }

    /// <summary>
    /// A payload from a library that predates the property has none, and reads as a file that
    /// stands alone.
    /// </summary>
    [Test]
    public async Task A_payload_with_no_source_has_none()
    {
        var move = Serializer.Deserialize<MovePayload>(
            """
            {
            "Type":"Move",
            "Temp":"theTemp",
            "Target":"theTarget",
            "CanKill":true
            }
            """);

        await Assert.That(move.Source).IsNull();
    }

    /// <summary>
    /// What makes a property the way to add to a payload: one this tray has no member for is
    /// skipped, as <c>Type</c> has been in every payload this tray has ever read. So a library
    /// newer than the tray can say more about a move without the tray losing the move.
    /// </summary>
    [Test]
    public async Task A_property_this_tray_does_not_know_is_skipped()
    {
        var result = Serializer.Deserialize<MovePayload>(
            """
            {
            "Type":"Move",
            "Temp":"theTemp",
            "Target":"theTarget",
            "CanKill":true,
            "SomethingALaterLibrarySays":"about the move"
            }
            """);

        await Assert.That(result.Temp).IsEqualTo("theTemp");
        await Assert.That(result.Target).IsEqualTo("theTarget");
        await Assert.That(result.CanKill).IsTrue();
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
