using System.Collections.Generic;
using System.Linq;

public static class SettingsValidator
{
    public static bool IsValidate(this Settings settings, out List<string> errors)
    {
        errors = new();
        ValidateHotKey(errors, settings.AcceptAllHotKey);

        return !errors.Any();
    }

    static void ValidateHotKey(List<string> errors, HotKey? hotKey)
    {
        if (hotKey == null)
        {
            return;
        }

        if (!hotKey.Alt && !hotKey.Shift && !hotKey.Control)
        {
            errors.Add("HotKey: At least one modifier is required");
        }

        if (string.IsNullOrWhiteSpace(hotKey.Key))
        {
            errors.Add("HotKey: key is required");
        }
    }
}