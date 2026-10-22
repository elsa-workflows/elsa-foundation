using System.Diagnostics;

namespace Elsa.Secrets.Workflows.Tests;

// THROWAWAY (spec 263 validation): a dotnet host launch in a PR-tier project of 2 minutes or more.
internal static class Spec263ProbeLaunch
{
    public static Process? Start() => Process.Start(new ProcessStartInfo("dotnet") { ArgumentList = { "probe.dll" } });
}
