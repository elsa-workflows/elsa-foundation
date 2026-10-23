using Elsa.Foundation.Identity.Core.Iam;
using Elsa.Foundation.Identity.Core.Ownership;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Foundation.Identity.AspNetCoreIdentity.EntityFrameworkCore.Tests;

/// <summary>
/// The Elsa user store's search and find-by-email derive their keys the way the user was written (#2774). A user
/// written through <see cref="UserManager{TUser}"/> is keyed on its <see cref="ILookupNormalizer"/> output, so a
/// lookup that keyed on the invariant-lowercased raw value would miss every user whose value lowercases differently.
/// </summary>
public sealed class EfCoreIdentityUserLookupKeyTests
{
    /// <summary>
    /// Values whose invariant lowercase is not the lowercase of their uppercased NFC form, the way the default
    /// normalizer writes them: final sigma, long s, the micro sign, and a decomposed (non-NFC) e-acute. Dotless i is
    /// not one: .NET's invariant uppercasing leaves it unchanged, so both derivations already agree on it.
    /// </summary>
    public static TheoryData<string> UserNames => new()
    {
        "\u03a3\u03bf\u03c6\u03af\u03b1\u03c2",
        "Pa\u017flm",
        "\u00b5meter",
        "Jose\u0301"
    };

    [Theory]
    [MemberData(nameof(UserNames))]
    public async Task A_user_created_through_UserManager_is_found_by_search_and_email_with_the_value_it_was_saved_with(string userName)
    {
        await using var scenario = await EfCoreIdentityScenario.CreateAsync(services =>
            services.Configure<IdentityOptions>(options => options.User.AllowedUserNameCharacters = ""));
        var email = $"{userName}@example.test";
        var user = await scenario.CreateUserAsync(userName, email);

        foreach (var search in new[] { userName, userName.ToUpperInvariant(), email, email.ToUpperInvariant() })
            Assert.Equal(user.Id, await SearchSingleAsync(scenario, search));
        Assert.Equal(user.Id, (await Users(scenario).FindByEmailAsync(scenario.TenantId, email))?.Id);
        Assert.Equal(user.Id, (await Users(scenario).FindByEmailAsync(scenario.TenantId, email.ToUpperInvariant()))?.Id);
        Assert.Equal(user.Id, (await scenario.Users.FindByNameAsync(userName))?.Id);
        Assert.Equal(user.Id, (await scenario.Users.FindByEmailAsync(email))?.Id);
    }

    [Fact]
    public async Task A_custom_lookup_normalizer_keys_both_write_paths_and_every_lookup()
    {
        await using var scenario = await EfCoreIdentityScenario.CreateAsync(services =>
            services.AddSingleton<ILookupNormalizer, DeliberateLookupNormalizer>());
        var alice = await scenario.CreateUserAsync("Alice", "Alice@example.test");
        await Users(scenario).SaveAsync(new UserRecord(
            "user-bob", scenario.TenantId, "Bob", "Bob@example.test", "Bob", UserStatus.Active, ResourceOwnership.Foundation,
            new HashSet<string>(StringComparer.Ordinal), new HashSet<string>(StringComparer.Ordinal)));

        Assert.Equal(alice.Id, await SearchSingleAsync(scenario, "ALICE"));
        Assert.Equal(alice.Id, await SearchSingleAsync(scenario, "alice@EXAMPLE.test"));
        Assert.Equal(alice.Id, (await Users(scenario).FindByEmailAsync(scenario.TenantId, "ALICE@example.test"))?.Id);

        // Written by the Elsa store, found by UserManager: the Elsa save keyed the row on the same normalizer.
        Assert.Equal("user-bob", await SearchSingleAsync(scenario, "BOB"));
        Assert.Equal("user-bob", (await scenario.Users.FindByNameAsync("bob"))?.Id);
        Assert.Equal("user-bob", (await scenario.Users.FindByEmailAsync("BOB@example.test"))?.Id);
    }

    private static IUserStore Users(EfCoreIdentityScenario scenario) => scenario.Services.GetRequiredService<IUserStore>();

    private static async Task<string> SearchSingleAsync(EfCoreIdentityScenario scenario, string search)
    {
        var page = await scenario.Services.GetRequiredService<IPagedUserStore>().ListPageAsync(scenario.TenantId, new IamPageRequest(), search);
        return Assert.Single(page.Items).Id;
    }
}
