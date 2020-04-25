using System.Diagnostics;

namespace DiffEngine
{
    public static class Logging
    {
        internal static bool enabled;

        public static void Enable()
        {
            enabled = true;
        }

        internal static void Write(string message)
        {
            if (enabled)
            {
                Trace.WriteLine(message);
            }
        }
    }
}