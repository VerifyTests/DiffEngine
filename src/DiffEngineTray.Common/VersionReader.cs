using System.Linq;
using System.Reflection;

static class VersionReader
{
    public static string VersionString = typeof(VersionReader).Assembly
        .GetCustomAttributes<AssemblyInformationalVersionAttribute>()
        .Single()
        .InformationalVersion;
}