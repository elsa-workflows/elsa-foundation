using System.Runtime.InteropServices;
using Elsa.Workflows.Publishing.Releases.Core;

namespace Elsa.Workflows.Publishing.Releases.Tests;

public class ReleaseDirectoryPlatformTests
{
    [Theory]
    [InlineData("linux", Architecture.X64, 0x20000, 0x10000, 0x800, 0x80000, 24, 16, 4, 8)]
    [InlineData("osx", Architecture.Arm64, 0x100, 0x100000, 4, 0x1000000, 4, 6, 2, 2)]
    public void Supported_platforms_use_their_own_open_flags_and_stat_layout(string os, Architecture architecture,
        int noFollow, int directory, int nonBlocking, int closeOnExec, int modeOffset, int linksOffset, int modeBytes, int linksBytes)
    {
        var abi = ReleaseDirectoryReader.GetAbi(Platform(os), architecture);
        Assert.Equal((noFollow, directory, nonBlocking, closeOnExec, modeOffset, linksOffset, modeBytes, linksBytes),
            (abi.NoFollow, abi.Directory, abi.NonBlocking, abi.CloseOnExec, abi.ModeOffset, abi.LinksOffset, abi.ModeBytes, abi.LinksBytes));
    }

    [Theory]
    [InlineData("linux", Architecture.Arm64)]
    [InlineData("linux", Architecture.X86)]
    [InlineData("linux", Architecture.Arm)]
    [InlineData("osx", Architecture.X64)]
    [InlineData("osx", Architecture.X86)]
    [InlineData("windows", Architecture.X64)]
    [InlineData("windows", Architecture.Arm64)]
    [InlineData("unknown", Architecture.X64)]
    public void Unqualified_platforms_fail_closed(string os, Architecture architecture)
    {
        var exception = Assert.Throws<ReleaseBundleFormatException>(() => ReleaseDirectoryReader.GetAbi(Platform(os), architecture));
        Assert.Equal("directory_platform_unsupported", exception.Code);
    }

    private static OSPlatform Platform(string os) => os switch
    {
        "linux" => OSPlatform.Linux, "osx" => OSPlatform.OSX, "windows" => OSPlatform.Windows, _ => default
    };
}
