﻿using System;

static class TargetPositionHelper
{
    public static bool TargetOnLeft { get; private set; }

    static TargetPositionHelper()
    {
        TargetOnLeft = ReadTargetOnLeft().GetValueOrDefault(false);
    }

    static bool? ReadTargetOnLeft()
    {
        var value = Environment.GetEnvironmentVariable("DiffEngine_TargetOnLeft");

        if (value == null)
        {
            return null;
        }

        if (value == "true")
        {
            return true;
        }

        if (value == "false")
        {
            return false;
        }

        throw new($"Unable to parse Position from `DiffEngine_TargetOnLeft`. Must be `true` or `false`. Environment variable: {value}");
    }

    public static void SetTargetOnLeft(bool value)
    {
        TargetOnLeft = value;
        string? envVariable;
        if (value)
        {
            envVariable = "true";
        }
        else
        {
            envVariable = null;
        }

        Environment.SetEnvironmentVariable("DiffEngine_TargetOnLeft", envVariable, EnvironmentVariableTarget.User);
    }
}