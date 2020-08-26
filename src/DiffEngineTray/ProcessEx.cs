using System;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;

static class ProcessEx
{
    public static bool TryGet(int id, [NotNullWhen(true)] out Process? process)
    {
        try
        {
            process = Process.GetProcessById(id);
            return true;
        }
        catch (ArgumentException)
        {
            //If process doesnt exists
            process = null;
            return false;
        }
    }
}