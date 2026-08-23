/// <summary>
/// Wheel messages into notches. A wheel click sends a whole one; a precision touchpad sends a
/// fraction of one per message, many times a second, for as long as the fingers move.
/// </summary>
public class WheelNotchesTests
{
    [Test]
    public async Task A_wheel_click_is_a_notch()
    {
        var notches = new WheelNotches(120);

        await Assert.That(notches.Add(120)).IsEqualTo(1);
    }

    /// <summary>
    /// The reported bug: each message divided on its own is zero notches, so two finger scrolling
    /// moved nothing at all.
    /// </summary>
    [Test]
    public async Task Small_movements_add_up_to_one()
    {
        var notches = new WheelNotches(120);

        var before = new[] { notches.Add(20), notches.Add(20), notches.Add(20), notches.Add(20), notches.Add(20) };
        var last = notches.Add(20);

        await Assert.That(before).IsEquivalentTo([0, 0, 0, 0, 0]);
        await Assert.That(last).IsEqualTo(1);
    }

    [Test]
    public async Task Small_movements_add_up_the_other_way_too()
    {
        var notches = new WheelNotches(120);

        var before = new[] { notches.Add(-40), notches.Add(-40) };
        var last = notches.Add(-40);

        await Assert.That(before).IsEquivalentTo([0, 0]);
        await Assert.That(last).IsEqualTo(-1);
    }

    /// <summary>
    /// What is left over is kept, rather than each notch starting from nothing.
    /// </summary>
    [Test]
    public async Task Keeps_the_remainder_across_notches()
    {
        var notches = new WheelNotches(120);

        notches.Add(100);
        var first = notches.Add(100);
        var second = notches.Add(100);

        await Assert.That(first).IsEqualTo(1);
        // 300 in, one notch out, 60 held. The third message crosses 240
        await Assert.That(second).IsEqualTo(1);
    }

    /// <summary>
    /// A movement back the other way undoes what is held, rather than each direction keeping a
    /// debt that the next movement has to pay off before anything happens.
    /// </summary>
    [Test]
    public async Task A_movement_back_undoes_what_is_held()
    {
        var notches = new WheelNotches(120);

        notches.Add(60);
        notches.Add(-60);
        var up = new[] { notches.Add(60), notches.Add(60) };

        await Assert.That(up).IsEquivalentTo([0, 1]);
    }

    [Test]
    public async Task A_fast_flick_is_several_notches()
    {
        var notches = new WheelNotches(120);

        await Assert.That(notches.Add(360)).IsEqualTo(3);
    }
}
