using Microsoft.Extensions.DependencyInjection;
using OpenIddict.Abstractions;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Elsa.Modularity.Tests;

/// <summary>
/// Tokens and authorizations of every kind the prune distinguishes, seeded into the store a provider runs and inspected afterwards,
/// so the prune is held to the same expectations on every engine.
/// </summary>
/// <param name="store">The provider whose OpenIddict managers the entries go through.</param>
/// <param name="time">The clock the prune's age is measured on. OpenIddict judges expiry by the real clock, so it starts at the real time.</param>
internal sealed class OpenIddictPruneScenario(IServiceProvider store, TimeProvider time)
{
    /// <summary>
    /// What was seeded: <see cref="Kept"/> is the ids that must survive a prune with the default age, and <see cref="All"/> every id.
    /// </summary>
    internal sealed record Entries(string[] Kept, string[] All);

    /// <param name="extraExpired">How many more redeemed, old tokens to seed, to make the prune take more than one batch.</param>
    public async Task<Entries> SeedAsync(int extraExpired = 0)
    {
        await using var scope = store.CreateAsyncScope();
        var tokens = scope.ServiceProvider.GetRequiredService<IOpenIddictTokenManager>();
        var authorizations = scope.ServiceProvider.GetRequiredService<IOpenIddictAuthorizationManager>();
        var old = time.GetUtcNow().AddDays(-30);
        var recent = time.GetUtcNow().AddDays(-1);
        var future = DateTimeOffset.UtcNow.AddDays(7);
        var past = DateTimeOffset.UtcNow.AddDays(-20);

        async Task<string> Token(string status, DateTimeOffset created, DateTimeOffset expires) => await IdAsync(tokens, await tokens.CreateAsync(new OpenIddictTokenDescriptor
        {
            Subject = "pruned-subject",
            Type = TokenTypeHints.RefreshToken,
            Status = status,
            CreationDate = created,
            ExpirationDate = expires
        }));

        async Task<string> Authorization(string status, DateTimeOffset created) => await IdAsync(authorizations, await authorizations.CreateAsync(new OpenIddictAuthorizationDescriptor
        {
            Subject = "pruned-subject",
            Type = AuthorizationTypes.Permanent,
            Status = status,
            CreationDate = created
        }));

        var kept = new[]
        {
            // Still valid, however old; redeemed or expired, but not old enough to be pruned yet.
            await Token(Statuses.Valid, old, future),
            await Token(Statuses.Redeemed, recent, future),
            await Token(Statuses.Valid, recent, past),
            await Authorization(Statuses.Valid, old),
            await Authorization(Statuses.Revoked, recent)
        };
        var pruned = new List<string>
        {
            await Token(Statuses.Valid, old, past),
            await Token(Statuses.Redeemed, old, future),
            await Token(Statuses.Revoked, old, future),
            await Authorization(Statuses.Revoked, old)
        };
        for (var extra = 0; extra < extraExpired; extra++)
            pruned.Add(await Token(Statuses.Redeemed, old, future));

        return new Entries([.. kept.Order()], [.. kept, .. pruned]);
    }

    /// <summary>The ids of <paramref name="ids"/> that are still in the store, as a token or as an authorization, in order.</summary>
    public async Task<string[]> RemainingAsync(IReadOnlyCollection<string> ids)
    {
        await using var scope = store.CreateAsyncScope();
        var tokens = scope.ServiceProvider.GetRequiredService<IOpenIddictTokenManager>();
        var authorizations = scope.ServiceProvider.GetRequiredService<IOpenIddictAuthorizationManager>();
        var remaining = new List<string>();
        foreach (var id in ids)
        {
            if (await tokens.FindByIdAsync(id) is not null || await authorizations.FindByIdAsync(id) is not null)
                remaining.Add(id);
        }

        return [.. remaining.Order()];
    }

    private static async Task<string> IdAsync(IOpenIddictTokenManager manager, object token) =>
        await manager.GetIdAsync(token) ?? throw new InvalidOperationException("OpenIddict did not assign an id to the token.");

    private static async Task<string> IdAsync(IOpenIddictAuthorizationManager manager, object authorization) =>
        await manager.GetIdAsync(authorization) ?? throw new InvalidOperationException("OpenIddict did not assign an id to the authorization.");
}
