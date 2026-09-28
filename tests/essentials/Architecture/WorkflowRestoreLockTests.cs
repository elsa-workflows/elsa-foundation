using System.Text.RegularExpressions;
using Xunit;
using YamlDotNet.RepresentationModel;
using static Elsa.Architecture.Tests.RepoPaths;

namespace Elsa.Architecture.Tests;

/// <summary>
/// Every CI invocation that restores a project restores in locked mode (#2122): a dependency change lands only as a
/// reviewed lock-file diff, never as whatever a clean restore resolves that day. <c>dotnet restore</c> steps already
/// carry <c>--locked-mode</c> by convention; this instead catches the implicit restore inside <c>dotnet run</c>,
/// <c>dotnet build</c>, <c>dotnet test</c>, <c>dotnet pack</c> and <c>dotnet publish</c>, which is unlocked unless the
/// same command line also carries <c>-p:RestoreLockedMode=true</c>, or skips restoring altogether with
/// <c>--no-restore</c> or <c>--no-build</c> because a locked restore already ran earlier in the step.
/// </summary>
public sealed class WorkflowRestoreLockTests
{
    private static readonly Regex RestoringDotnetCommand = new(@"(?<![\w.-])dotnet\s+(run|build|test|pack|publish)\b", RegexOptions.Compiled);
    private static readonly Regex HeredocStart = new(@"<<-?\s*['""]?(\w+)['""]?\s*$", RegexOptions.Compiled);
    private static readonly string[] LockOrSkipMarkers = ["--locked-mode", "RestoreLockedMode=true", "--no-restore", "--no-build"];

    [Fact]
    public void Every_workflow_dotnet_run_build_test_pack_or_publish_is_locked_or_skips_restore()
    {
        var workflowsDirectory = Path.Join(RepoRoot, ".github", "workflows");
        var violations = Directory.GetFiles(workflowsDirectory, "*.yml").OrderBy(file => file, StringComparer.Ordinal)
            .SelectMany(file => RunScripts(file).SelectMany(script => Violations(Path.GetFileName(file), script)))
            .ToArray();

        Assert.True(violations.Length == 0, string.Join(Environment.NewLine, violations));
    }

    /// <summary>Every logical, backslash-joined shell line in <paramref name="script"/> that restores unlocked.</summary>
    private static IEnumerable<string> Violations(string fileName, string script)
    {
        foreach (var line in LogicalLines(WithoutHeredocBodies(script)))
            if (RestoringDotnetCommand.IsMatch(line) && !LockOrSkipMarkers.Any(marker => line.Contains(marker, StringComparison.Ordinal)))
                yield return $"{fileName}: '{line.Trim()}' restores without --locked-mode, -p:RestoreLockedMode=true, --no-restore or --no-build";
    }

    /// <summary>
    /// <paramref name="script"/> with every heredoc body blanked out, keeping the opening line: the workflows write
    /// refresh instructions (including sample <c>dotnet run</c> commands) into <c>$GITHUB_STEP_SUMMARY</c> through a
    /// <c>cat &lt;&lt;'EOF'</c> block, which is documentation for a human, not a command this step runs.
    /// </summary>
    private static string WithoutHeredocBodies(string script)
    {
        var lines = script.Split('\n');
        var kept = new List<string>();
        string? delimiter = null;
        foreach (var line in lines)
        {
            if (delimiter is not null)
            {
                if (line.Trim() == delimiter)
                    delimiter = null;
                continue;
            }

            var match = HeredocStart.Match(line.TrimEnd('\r'));
            if (match.Success)
                delimiter = match.Groups[1].Value;

            kept.Add(line);
        }

        return string.Join('\n', kept);
    }

    /// <summary>Physical lines joined wherever one ends with a shell continuation (<c>\</c>), so a flag on a later
    /// continuation line still covers the command it belongs to.</summary>
    private static IEnumerable<string> LogicalLines(string script)
    {
        var current = "";
        foreach (var raw in script.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            var continues = line.TrimEnd().EndsWith('\\');
            current += (current.Length > 0 ? " " : "") + (continues ? line.TrimEnd()[..^1] : line);
            if (!continues)
            {
                yield return current;
                current = "";
            }
        }

        if (current.Length > 0)
            yield return current;
    }

    /// <summary>Every <c>run:</c> step script in a workflow file, wherever it sits in the job graph.</summary>
    private static IEnumerable<string> RunScripts(string path)
    {
        var stream = new YamlStream();
        using (var reader = new StreamReader(path))
            stream.Load(reader);

        return stream.Documents.SelectMany(document => RunScripts(document.RootNode));
    }

    private static IEnumerable<string> RunScripts(YamlNode node)
    {
        switch (node)
        {
            case YamlMappingNode mapping:
                foreach (var (key, value) in mapping.Children)
                {
                    if (key is YamlScalarNode { Value: "run" } && value is YamlScalarNode { Value: { } run })
                        yield return run;
                    else
                        foreach (var nested in RunScripts(value))
                            yield return nested;
                }
                break;
            case YamlSequenceNode sequence:
                foreach (var item in sequence.Children)
                    foreach (var nested in RunScripts(item))
                        yield return nested;
                break;
        }
    }
}
