using ConsoleLogStreaming.Core.Capture;

namespace Elsa.Studio.ConsoleStream;

internal static class ConsoleStreamHookInstaller
{
    private static int _installed;

    public static void InstallOnce()
    {
        if (Interlocked.Exchange(ref _installed, 1) == 0)
            ConsoleStreamHook.Install();
    }
}
