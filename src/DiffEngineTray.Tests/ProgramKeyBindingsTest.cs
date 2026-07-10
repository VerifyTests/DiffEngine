public class ProgramKeyBindingsTest
{
    [Test]
    public async Task Each_configured_hotkey_uses_a_distinct_binding_id()
    {
        var settings = new Settings
        {
            DiscardAllHotKey = new() { Key = "A" },
            AcceptAllHotKey = new() { Key = "B" },
            AcceptOpenHotKey = new() { Key = "C" },
        };
        await using var tracker = new RecordingTracker();

        var ids = Program.BuildKeyBindings(settings, tracker)
            .Select(_ => _.Id)
            .ToList();

        await Assert.That(ids.Count).IsEqualTo(3);
        // A duplicate id would make KeyRegister unregister and overwrite an earlier hot key.
        await Assert.That(ids.Distinct().Count()).IsEqualTo(3);
        await Assert.That(ids.Contains(KeyBindingIds.DiscardAll)).IsTrue();
        await Assert.That(ids.Contains(KeyBindingIds.AcceptAll)).IsTrue();
        await Assert.That(ids.Contains(KeyBindingIds.AcceptOpen)).IsTrue();
    }

    [Test]
    public async Task AcceptOpen_hotkey_maps_to_the_AcceptOpen_binding()
    {
        var settings = new Settings
        {
            AcceptOpenHotKey = new() { Key = "C" },
        };
        await using var tracker = new RecordingTracker();

        var binding = Program.BuildKeyBindings(settings, tracker)
            .Single();

        await Assert.That(binding.Id).IsEqualTo(KeyBindingIds.AcceptOpen);
    }
}
