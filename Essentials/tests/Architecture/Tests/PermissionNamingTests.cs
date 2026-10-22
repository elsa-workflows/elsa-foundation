using System.Text.RegularExpressions;
using Xunit;

namespace Elsa.Architecture.Tests;

/// <summary>
/// Every permission a first-party contributor adds to the catalog uses Elsa 3.9's name shape (ADR 0085 D2, #2773):
/// lowercase segments joined by <c>-</c> inside a segment, <c>/</c> between a parent and a child resource, and <c>:</c>
/// before the verb. The catalog itself refuses a key that is not one <c>{resource}:{verb}</c>; this guard also refuses the
/// spellings that grammar accepts and the vocabulary does not, such as a dotted resource or an uppercase segment.
/// </summary>
public sealed partial class PermissionNamingTests
{
    [Fact]
    public void Every_first_party_catalog_permission_is_a_lowercase_resource_verb_name()
    {
        var keys = PermissionBypassScanner.ContributedPermissionKeys(RuntimeLibraries.Assemblies.SelectMany(assembly => assembly.GetTypes()));

        // The sweep must have reached both trees, or an empty result would pass without checking anything.
        Assert.Contains(Elsa.Foundation.Identity.Core.Authorization.DefaultIdentityPermissionKeys.IdentityUsersManage, keys);
        Assert.Contains(Elsa.Agent.Api.Constants.AgentPermissionKeys.Use, keys);
        Assert.True(keys.Count >= 50, $"Only {keys.Count} contributed permission keys were found.");
        Assert.Empty(keys.Where(key => !IsResourceVerbName(key)).Order(StringComparer.Ordinal));
    }

    [Theory]
    [InlineData("identity/users:read", true)]
    [InlineData("workflows/runtime/javascript:execute", true)]
    [InlineData("studio/preferences/defaults:manage", true)]
    [InlineData("workflows/activity-executions:inspect-values", true)]
    [InlineData("identity.users.read", false)]
    [InlineData("identity.users:read", false)]
    [InlineData("Identity/Users:Read", false)]
    [InlineData("identity/users", false)]
    [InlineData("identity/*:read", false)]
    [InlineData("identity//users:read", false)]
    [InlineData("identity/users:read:all", false)]
    public void The_naming_rule_accepts_only_lowercase_resource_verb_names(string key, bool expected)
    {
        Assert.Equal(expected, IsResourceVerbName(key));
    }

    private static bool IsResourceVerbName(string key) => ResourceVerbName().IsMatch(key);

    [GeneratedRegex("^[a-z][a-z0-9-]*(/[a-z][a-z0-9-]*)*:[a-z][a-z0-9-]*$")]
    private static partial Regex ResourceVerbName();
}
