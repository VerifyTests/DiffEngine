using System;
using System.Linq;

namespace DiffEngine
{
    public static class ContinuousTestingDetector
    {
        static ContinuousTestingDetector()
        {
            if (AppDomain.CurrentDomain.GetAssemblies()
                .Any(a => a.FullName.StartsWith("Microsoft.CodeAnalysis.LiveUnitTesting.Runtime")))
            {
                Detected = true;
                return;
            }

            if (Environment.GetEnvironmentVariable("NCRUNCH") != null &&
                Environment.GetEnvironmentVariable("NCrunch.IsHighPriority") != "1")
            {
                Detected = true;
            }
        }

        public static bool Detected { get; set; }
    }
}