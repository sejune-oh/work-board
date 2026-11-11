using System.Runtime.CompilerServices;

namespace WorkBoard.Api.Tests;

internal static class TestEnvironment
{
    // Test hosts run in Development, where the periodic sync loop would otherwise start and call GitHub.
    [ModuleInitializer]
    internal static void DisablePeriodicSync() => Environment.SetEnvironmentVariable("Sync__Enabled", "false");
}
