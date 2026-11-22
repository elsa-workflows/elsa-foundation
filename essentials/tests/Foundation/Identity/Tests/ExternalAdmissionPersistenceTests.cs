using System.Security.Claims;
using Elsa.Foundation.Identity.AspNetCoreIdentity.Services;
using Elsa.Foundation.Identity.Core.Authentication;
using Elsa.Foundation.Identity.Core.Authorization;
using Elsa.Foundation.Identity.Core.Iam;
using Elsa.Foundation.Identity.ExternalAuthentication;
using Elsa.Foundation.Identity.ExternalAuthentication.Core;
using Elsa.Foundation.Identity.Persistence.EntityFrameworkCore;
using Elsa.Foundation.Identity.Persistence.EntityFrameworkCore.DependencyInjection;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Foundation.Identity.Tests;

public sealed class ExternalAdmissionPersistenceTests
{
    [Fact]
    public async Task Policy_preview_admission_and_canonical_projection_use_the_shared_IAM_authority_across_restart()
    {
        var file = Path.Combine(Path.GetTempPath(), $"elsa-3579-policy-{Guid.NewGuid():n}.db");
        try
        {
            string userId;
            await using (var first = await Create(file))
            {
                await using var seed = first.CreateAsyncScope();
                Bind(seed.ServiceProvider);
                await seed.ServiceProvider.GetRequiredService<IRoleStore>().SaveAsync(new("reader", "default", "Reader", null,
                    new HashSet<string> { "workflows:read" }, false));
                var context = await Context(first);
                var admission = first.GetRequiredService<IExternalAdmissionService>();
                Assert.Equal(ExternalAdmissionKind.NewUser, (await admission.AssessAsync(context)).Kind);
                Assert.Equal(0, await seed.ServiceProvider.GetRequiredService<IdentityIamDbContext>().Users.CountAsync());
                var result = await admission.AdmitAsync(context);
                Assert.Equal(ExternalAdmissionFailure.None, result.Failure);
                userId = result.UserId!;
                Assert.Equal(1, await seed.ServiceProvider.GetRequiredService<IdentityIamDbContext>().Users.CountAsync());
            }
            await using var restarted = await Create(file);
            var current = await restarted.GetRequiredService<IExternalAdmissionService>().AssessAsync(await Context(restarted));
            Assert.Equal(ExternalAdmissionKind.ExistingUser, current.Kind);
            Assert.Equal(userId, current.User!.Id);
            await using var request = restarted.CreateAsyncScope();
            Bind(request.ServiceProvider);
            var principal = await new AspNetCoreIdentityPrincipalFactory(request.ServiceProvider.GetRequiredService<IUserStore>(),
                request.ServiceProvider.GetRequiredService<IRoleStore>(), request.ServiceProvider.GetRequiredService<ITenantMembershipStore>())
                .CreateAsync(new PrincipalFactoryContext("default", userId, "company"));
            Assert.Equal(userId, Assert.Single(principal.FindAll(ClaimTypes.NameIdentifier)).Value);
            Assert.Equal(["reader"], principal.FindAll(IdentityClaimTypes.Role).Select(claim => claim.Value));
            Assert.Equal(["workflows:read"], principal.FindAll(IdentityClaimTypes.Permission).Select(claim => claim.Value));
            Assert.Equal("v1", Assert.Single(principal.FindAll(IdentityClaimTypes.Normalized)).Value);
        }
        finally
        {
            foreach (var suffix in new[] { "", "-wal", "-shm" }) File.Delete(file + suffix);
        }
    }

    private static async Task<ServiceProvider> Create(string file)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddIdentityIamEntityFrameworkCore(new() { Provider = "Sqlite", ConnectionString = $"Data Source={file};Pooling=False" });
        services.AddFoundationExternalAuthentication(options => options.Connections.Add(new()
            { Key = "company", Enabled = true, Admission = new() { Type = "jit", DefaultRoleIds = ["reader"] } }));
        var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<IdentityIamDbContext>().Database.MigrateAsync();
        return provider;
    }

    private static void Bind(IServiceProvider services) => services.GetRequiredService<IPersistenceAccessContextBinder>()
        .Bind(PersistenceAccessContext.Scoped(new PersistenceScope("default")));

    private static async Task<ExternalAdmissionContext> Context(IServiceProvider services) => new(
        Assert.Single((await services.GetRequiredService<IConnectionRegistry>().ReadAsync()).Connections),
        new("default", "company", "https://issuer/", "subject"),
        [new("iss", "https://issuer/"), new("sub", "subject"), new("email", "ada@example.test"), new(IdentityClaimTypes.Permission, "*")]);
}
