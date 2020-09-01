using System.Collections.Generic;
using System.Linq;

public static class SettingsValidator
{
    public static bool IsValidate(this Settings settings, out List<string> errors)
    {
        errors = new List<string>();
        var hotKey = settings.AcceptAllHotKey;
        if (hotKey != null)
        {
            if (!hotKey.Alt && !hotKey.Shift && !hotKey.Control)
            {
                errors.Add("HotKey: At least one modifier is required");
            }

            if (string.IsNullOrWhiteSpace(hotKey.Key))
            {
                errors.Add("HotKey: key is required");
            }
        }

        return !errors.Any();
    }
}