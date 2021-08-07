using System;

static class TargetPositionReader
{
    public static TargetPosition Position { get; }

    static TargetPositionReader()
    {
        var value = Environment.GetEnvironmentVariable("DiffEngine_TargetPosition");

        if (value == null)
        {
            Position = TargetPosition.Right;
            return;
        }

        value = value.ToLowerInvariant();
        if (value == "left")
        {
            Position = TargetPosition.Left;
            return;
        }

        if (value == "right")
        {
            Position = TargetPosition.Right;
            return;
        }

        throw new($"Unable to parse Position from `DiffEngine_TargetPosition`. Must be `Left` or `Right`. Environment variable: {value}");
    }
}