using System;
using System.IO;
using System.Runtime.InteropServices;

namespace DiffEngine.Tests
{
    public class MacDiffTool
    {
        public static string Exe;

        static MacDiffTool()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                Exe = Path.GetFullPath(Path.Combine(AssemblyLocation.CurrentDirectory, "../../../../DiffEngineTray.Mac/bin/Debug/DiffEngineTray.Mac.app"));
                return;
            }

            throw new Exception();
        }
    }
}