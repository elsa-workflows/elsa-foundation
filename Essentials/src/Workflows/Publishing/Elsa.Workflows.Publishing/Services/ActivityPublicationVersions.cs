using Elsa.Workflows.Publishing.Core.Contracts;
using Elsa.Activities.Design.Core.Models;
using Elsa.Primitives.Versioning;

namespace Elsa.Workflows.Publishing.Services;

/// <summary>The existing activity publication release choices, shared by draft and typed source publishers.</summary>
public sealed class ActivityPublicationVersions : IActivityPublicationVersionPolicy
{
    public IReadOnlyList<string> Choices(string? headVersion, ActivityVersionBump requiredBump)
    {
        if (headVersion is null) return ["1.0.0"];
        if (!SemVer.TryParse(headVersion, out var head)) throw new ArgumentException("The publication head must be a semantic version.", nameof(headVersion));
        var patch = $"{head.Major}.{head.Minor}.{checked(head.Patch + 1)}";
        var minor = $"{head.Major}.{checked(head.Minor + 1)}.0";
        var major = $"{checked(head.Major + 1)}.0.0";
        return requiredBump switch { ActivityVersionBump.Major => [major], ActivityVersionBump.Minor => [minor, major], _ => [patch, minor, major] };
    }
}
