public static class SettingsValidator
{
    public static bool IsValidate(this Settings settings, out List<string> errors)
    {
        errors = [];

        ValidateHotKey(errors, settings.AcceptAllHotKey);
        ValidateHotKey(errors, settings.DiscardAllHotKey);
        ValidateHotKey(errors, settings.AcceptOpenHotKey);

        return !errors.Any();
    }

    static void ValidateHotKey(List<string> errors, HotKey? hotKey)
    {
        if (hotKey == null)
        {
            return;
        }

        if (hotKey is {Alt: false, Shift: false, Control: false})
        {
            errors.Add("HotKey: At least one modifier is required");
        }

        if (string.IsNullOrWhiteSpace(hotKey.Key))
        {
            errors.Add("HotKey: key is required");
        }
    }
}