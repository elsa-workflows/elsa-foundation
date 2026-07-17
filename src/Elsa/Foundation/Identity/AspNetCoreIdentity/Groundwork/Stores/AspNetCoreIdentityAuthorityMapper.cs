using Elsa.Foundation.Identity.Abstractions.Iam;
using Elsa.Foundation.Identity.Abstractions.Ownership;
using Elsa.Foundation.Identity.AspNetCoreIdentity.Models;
using Elsa.Foundation.Identity.Persistence.Groundwork.Documents;
using Microsoft.AspNetCore.Identity;

namespace Elsa.Foundation.Identity.AspNetCoreIdentity.Groundwork.Stores;

internal static class AspNetCoreIdentityAuthorityMapper
{
    public static IdentityUserFrameworkState ToFrameworkState(AspNetCoreIdentityUser user) => new(
        user.Id,
        user.TenantId,
        user.UserName,
        user.NormalizedUserName,
        user.Email,
        user.NormalizedEmail,
        user.EmailConfirmed,
        user.PasswordHash,
        user.SecurityStamp,
        user.ConcurrencyStamp,
        user.PhoneNumber,
        user.PhoneNumberConfirmed,
        user.TwoFactorEnabled,
        user.LockoutEnd,
        user.LockoutEnabled,
        user.AccessFailedCount,
        user.DisplayName);

    public static UserRecord ToUserRecord(AspNetCoreIdentityUser user) => new(
        user.Id,
        user.TenantId,
        user.UserName ?? string.Empty,
        user.Email,
        user.DisplayName,
        UserStatus.Active,
        ResourceOwnership.Foundation,
        new HashSet<string>(StringComparer.Ordinal),
        new HashSet<string>(StringComparer.OrdinalIgnoreCase));

    public static AspNetCoreIdentityUser ToUser(IdentityUserDocument document)
    {
        var state = document.FrameworkState ?? FromUserRecord(document.User, document.NormalizedUserName, document.NormalizedEmail);
        return new AspNetCoreIdentityUser
        {
            Id = state.Id,
            TenantId = state.TenantId,
            UserName = state.UserName,
            NormalizedUserName = state.NormalizedUserName,
            Email = state.Email,
            NormalizedEmail = state.NormalizedEmail,
            EmailConfirmed = state.EmailConfirmed,
            PasswordHash = state.PasswordHash,
            SecurityStamp = state.SecurityStamp,
            ConcurrencyStamp = state.ConcurrencyStamp,
            PhoneNumber = state.PhoneNumber,
            PhoneNumberConfirmed = state.PhoneNumberConfirmed,
            TwoFactorEnabled = state.TwoFactorEnabled,
            LockoutEnd = state.LockoutEnd,
            LockoutEnabled = state.LockoutEnabled,
            AccessFailedCount = state.AccessFailedCount,
            DisplayName = state.DisplayName
        };
    }

    public static IdentityRoleFrameworkState ToFrameworkState(IdentityRole role) => new(
        role.Id,
        role.Name,
        role.NormalizedName,
        role.ConcurrencyStamp);

    public static RoleRecord ToRoleRecord(IdentityRole role, string tenantId) => new(
        role.Id,
        tenantId,
        role.Name ?? string.Empty,
        Description: null,
        new HashSet<string>(StringComparer.OrdinalIgnoreCase),
        System: false);

    public static IdentityRole ToRole(IdentityRoleDocument document)
    {
        var state = document.FrameworkState ?? FromRoleRecord(document.Role, document.NormalizedRoleName);
        return new IdentityRole
        {
            Id = state.Id,
            Name = state.Name,
            NormalizedName = state.NormalizedName,
            ConcurrencyStamp = state.ConcurrencyStamp
        };
    }

    private static IdentityUserFrameworkState FromUserRecord(
        UserRecord user,
        string? normalizedUserName,
        string? normalizedEmail) => new(
        user.Id,
        user.TenantId,
        user.UserName,
        normalizedUserName,
        user.Email,
        normalizedEmail,
        EmailConfirmed: false,
        PasswordHash: null,
        SecurityStamp: null,
        ConcurrencyStamp: null,
        PhoneNumber: null,
        PhoneNumberConfirmed: false,
        TwoFactorEnabled: false,
        LockoutEnd: null,
        LockoutEnabled: true,
        AccessFailedCount: 0,
        user.DisplayName);

    private static IdentityRoleFrameworkState FromRoleRecord(RoleRecord role, string? normalizedRoleName) => new(
        role.Id,
        role.Name,
        normalizedRoleName,
        ConcurrencyStamp: null);
}
