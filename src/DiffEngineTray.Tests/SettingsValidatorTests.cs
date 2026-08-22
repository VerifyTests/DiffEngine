public class SettingsValidatorTests
{
    [Test]
    public async Task No_hotkeys_is_valid()
    {
        var settings = new Settings();

        var valid = settings.IsValidate(out var errors);

        await Assert.That(valid).IsTrue();
        await Assert.That(errors.Count).IsEqualTo(0);
    }

    [Test]
    public async Task Hotkey_without_modifier_is_invalid()
    {
        var settings = new Settings
        {
            AcceptAllHotKey = new()
            {
                Key = "A"
            }
        };

        var valid = settings.IsValidate(out var errors);

        await Assert.That(valid).IsFalse();
        await Assert.That(errors.Contains("HotKey: At least one modifier is required")).IsTrue();
    }

    [Test]
    public async Task Hotkey_without_key_is_invalid()
    {
        var settings = new Settings
        {
            AcceptAllHotKey = new()
            {
                Shift = true
            }
        };

        var valid = settings.IsValidate(out var errors);

        await Assert.That(valid).IsFalse();
        await Assert.That(errors.Contains("HotKey: key is required")).IsTrue();
    }

    /// <summary>
    /// Only a hand edit produces one, the form offering nothing but letters, and it used to pass
    /// validation and then throw out of the hot key registration at every startup.
    /// </summary>
    [Test]
    public async Task Hotkey_with_a_key_that_is_not_a_key_name_is_invalid()
    {
        var settings = new Settings
        {
            AcceptAllHotKey = new()
            {
                Shift = true,
                Key = "Ctrl+A"
            }
        };

        var valid = settings.IsValidate(out var errors);

        await Assert.That(valid).IsFalse();
        await Assert.That(errors.Contains("HotKey: 'Ctrl+A' is not a key name")).IsTrue();
    }

    [Test]
    public async Task Valid_hotkey_passes()
    {
        var settings = new Settings
        {
            AcceptAllHotKey = new()
            {
                Shift = true,
                Key = "A"
            }
        };

        var valid = settings.IsValidate(out var errors);

        await Assert.That(valid).IsTrue();
        await Assert.That(errors.Count).IsEqualTo(0);
    }

    [Test]
    public async Task Errors_accumulate_across_hotkeys()
    {
        var settings = new Settings
        {
            AcceptAllHotKey = new()
            {
                Key = "A"
            },
            DiscardAllHotKey = new()
            {
                Shift = true
            }
        };

        var valid = settings.IsValidate(out var errors);

        await Assert.That(valid).IsFalse();
        await Assert.That(errors.Count).IsEqualTo(2);
    }
}
