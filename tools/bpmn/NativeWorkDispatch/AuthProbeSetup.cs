using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using Elsa.Foundation.Identity.AspNetCoreIdentity.EntityFrameworkCore.DependencyInjection;
using Elsa.Foundation.Identity.AspNetCoreIdentity.Models;
using Elsa.Foundation.Identity.Core.Iam;
using Elsa.Foundation.Identity.Persistence.EntityFrameworkCore;
using Elsa.Foundation.Identity.Persistence.EntityFrameworkCore.DependencyInjection;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Extensions;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

/// <summary>Creates bounded test accounts through the same Identity services used by the owned host.</summary>
internal static class AuthProbeSetup
{
    internal static async Task CreateAsync(string root, string nonce, JsonSerializerOptions json)
    {
        var path = Path.Combine(root, "owned-auth-probe-request.json");
        var request = JsonSerializer.Deserialize<Request>(await File.ReadAllTextAsync(path), json)
            ?? throw new InvalidOperationException("The owned authorization setup request is absent.");
        var allowed = new HashSet<string>(["bpmn:read", "bpmn:retry-service"], StringComparer.Ordinal);
        if (request.Nonce != nonce || request.Users.Length != 4 || request.Users.Select(user => user.UserName).Distinct(StringComparer.Ordinal).Count() != 4 ||
            request.Users.Any(user => !user.UserName.StartsWith("bpmn-probe-", StringComparison.Ordinal) ||
                user.TenantId is not ("default" or "bpmn-probe-foreign") || user.Permissions.Except(allowed).Any() || string.IsNullOrWhiteSpace(user.Password)))
            throw new InvalidOperationException("The authorization setup differs from its controller-owned bounded profiles.");
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddWorkflowRuntime();
        services.AddFoundationAspNetCoreIdentityEntityFrameworkCore(new IdentityIamEntityFrameworkCoreOptions
        { Provider = "Sqlite", ConnectionString = $"Data Source={Path.Combine(root, "elsa.db")};Pooling=False" }, isDevelopmentOrDemo: true);
        await using var provider = services.BuildServiceProvider();
        var outcomes = new List<object>();
        foreach (var account in request.Users)
        {
            await using var scope = provider.CreateAsyncScope();
            scope.ServiceProvider.GetRequiredService<IPersistenceAccessContextBinder>().Bind(PersistenceAccessContext.Scoped(new(account.TenantId)));
            var users = scope.ServiceProvider.GetRequiredService<UserManager<AspNetCoreIdentityUser>>();
            var roles = scope.ServiceProvider.GetRequiredService<Elsa.Foundation.Identity.Core.Iam.IRoleManager>();
            var user = new AspNetCoreIdentityUser { UserName = account.UserName, TenantId = account.TenantId,
                Email = account.UserName + "@example.test", EmailConfirmed = true, DisplayName = account.UserName };
            Require(await users.CreateAsync(user, account.Password));
            var roleName = account.UserName + "-role";
            var role = await roles.CreateAsync(new(account.TenantId, roleName, "Owned BPMN authorization journey", account.Permissions.ToHashSet(StringComparer.Ordinal)));
            Require(await users.AddToRoleAsync(user, roleName));
            var stored = await scope.ServiceProvider.GetRequiredService<Elsa.Foundation.Identity.Core.Iam.IUserStore>().FindAsync(account.TenantId, user.Id)
                ?? throw new InvalidOperationException("The normal Identity user write did not remain visible.");
            if (!stored.RoleIds.Contains(role.Id) || !stored.DirectPermissions.SetEquals([]))
                throw new InvalidOperationException("The owned account did not retain only its actual bounded role grant.");
            outcomes.Add(new { userId = user.Id, account.UserName, account.TenantId, roleId = role.Id, permissions = role.Permissions.Order(StringComparer.Ordinal).ToArray() });
        }
        static object Product(Assembly assembly) => new { name = assembly.GetName().Name,
            informationalVersion = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion,
            sha256 = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(assembly.Location))) };
        Console.WriteLine(JsonSerializer.Serialize(new { operation = "create-owned-auth-probe-users", users = outcomes,
            credentialsIncluded = false, executionStateSeeded = false, products = new[] { Product(Assembly.GetExecutingAssembly()),
                Product(typeof(AspNetCoreIdentityUser).Assembly), Product(typeof(AspNetCoreIdentityEntityFrameworkCoreServiceCollectionExtensions).Assembly),
                Product(typeof(IdentityIamEntityFrameworkCoreOptions).Assembly), Product(typeof(UserRecord).Assembly) } }, json));
    }

    private static void Require(IdentityResult result)
    {
        if (!result.Succeeded) throw new InvalidOperationException("The normal Identity operation refused the owned account: " + string.Join(',', result.Errors.Select(error => error.Code)));
    }
    private sealed record Request(string Nonce, Account[] Users);
    private sealed record Account(string UserName, string TenantId, string Password, string[] Permissions);
}
