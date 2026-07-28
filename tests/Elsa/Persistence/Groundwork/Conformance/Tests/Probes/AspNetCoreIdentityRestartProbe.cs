using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Elsa.Persistence.Groundwork.Testing;

namespace Elsa.Persistence.Groundwork.Conformance.Tests.Probes;

public enum AspNetCoreIdentityRestartProbeOperation
{
    CreateUser,
    FindByNormalizedUserName,
    DuplicateCreate
}

public sealed record AspNetCoreIdentityRestartProbeUser
{
    [JsonConstructor]
    public AspNetCoreIdentityRestartProbeUser(
        string tenantId,
        string userId,
        string userName,
        string normalizedUserName,
        string? email = null,
        string? normalizedEmail = null,
        string? displayName = null)
    {
        if (string.IsNullOrWhiteSpace(tenantId))
            throw new ArgumentException("A tenant ID is required.", nameof(tenantId));
        if (string.IsNullOrWhiteSpace(userId))
            throw new ArgumentException("A user ID is required.", nameof(userId));
        if (string.IsNullOrWhiteSpace(userName))
            throw new ArgumentException("A user name is required.", nameof(userName));
        if (string.IsNullOrWhiteSpace(normalizedUserName))
            throw new ArgumentException("A normalized user name is required.", nameof(normalizedUserName));

        TenantId = tenantId;
        UserId = userId;
        UserName = userName;
        NormalizedUserName = normalizedUserName;
        Email = email;
        NormalizedEmail = normalizedEmail;
        DisplayName = displayName;
    }

    public string TenantId { get; }
    public string UserId { get; }
    public string UserName { get; }
    public string NormalizedUserName { get; }
    public string? Email { get; }
    public string? NormalizedEmail { get; }
    public string? DisplayName { get; }

    public override string ToString() =>
        $"{nameof(AspNetCoreIdentityRestartProbeUser)} {{ TenantId = [REDACTED], UserId = [REDACTED], NormalizedUserNameSha256 = {AspNetCoreIdentityRestartProbe.ComputeSha256(NormalizedUserName)} }}";
}

public sealed record AspNetCoreIdentityRestartProbePayload
{
    [JsonConstructor]
    public AspNetCoreIdentityRestartProbePayload(
        AspNetCoreIdentityRestartProbeOperation operation,
        AspNetCoreIdentityRestartProbeUser user)
    {
        ArgumentNullException.ThrowIfNull(user);
        Operation = operation;
        User = user;
    }

    public AspNetCoreIdentityRestartProbeOperation Operation { get; }
    public AspNetCoreIdentityRestartProbeUser User { get; }

    public override string ToString() =>
        $"{nameof(AspNetCoreIdentityRestartProbePayload)} {{ Operation = {Operation}, User = [REDACTED] }}";
}

public sealed record AspNetCoreIdentityRestartProbeObservation
{
    [JsonConstructor]
    public AspNetCoreIdentityRestartProbeObservation(
        AspNetCoreIdentityRestartProbeOperation operation,
        string outcome,
        string? foundUserId,
        string? errorCode,
        long documentVersion)
    {
        if (string.IsNullOrWhiteSpace(outcome))
            throw new ArgumentException("An outcome is required.", nameof(outcome));
        if (documentVersion < 1)
            throw new ArgumentOutOfRangeException(nameof(documentVersion), "A persisted identity document version is required.");

        Operation = operation;
        Outcome = outcome;
        FoundUserId = foundUserId;
        ErrorCode = errorCode;
        DocumentVersion = documentVersion;
    }

    public AspNetCoreIdentityRestartProbeOperation Operation { get; }
    public string Outcome { get; }
    public string? FoundUserId { get; }
    public string? ErrorCode { get; }
    public long DocumentVersion { get; }

    public override string ToString() =>
        $"{nameof(AspNetCoreIdentityRestartProbeObservation)} {{ Operation = {Operation}, Outcome = {Outcome}, FoundUserId = [REDACTED], ErrorCode = {ErrorCode ?? "-"}, DocumentVersion = {DocumentVersion} }}";
}

public static class AspNetCoreIdentityRestartProbe
{
    public const string CreateUserProbeId = "identity-create-user";
    public const string FindByNormalizedUserNameProbeId = "identity-find-normalized-user";
    public const string DuplicateCreateProbeId = "identity-duplicate-create";
    public const string DocumentId = "identity-restart-probe";

    private static JsonSerializerOptions JsonOptions { get; } = CreateJsonOptions();

    public static GroundworkProcessProbeRequest CreateUser(AspNetCoreIdentityRestartProbeUser user) =>
        Request(CreateUserProbeId, AspNetCoreIdentityRestartProbeOperation.CreateUser, user);

    public static GroundworkProcessProbeRequest FindByNormalizedUserName(AspNetCoreIdentityRestartProbeUser user) =>
        Request(FindByNormalizedUserNameProbeId, AspNetCoreIdentityRestartProbeOperation.FindByNormalizedUserName, user);

    public static GroundworkProcessProbeRequest DuplicateCreate(AspNetCoreIdentityRestartProbeUser user) =>
        Request(DuplicateCreateProbeId, AspNetCoreIdentityRestartProbeOperation.DuplicateCreate, user);

    public static AspNetCoreIdentityRestartProbePayload DecodePayload(string json) =>
        Deserialize<AspNetCoreIdentityRestartProbePayload>(json);

    public static string EncodeObservation(AspNetCoreIdentityRestartProbeObservation observation) =>
        Serialize(observation);

    public static AspNetCoreIdentityRestartProbeObservation DecodeObservation(string json) =>
        Deserialize<AspNetCoreIdentityRestartProbeObservation>(json);

    public static string ObservationDigest(AspNetCoreIdentityRestartProbeObservation observation) =>
        ComputeSha256(EncodeObservation(observation));

    private static GroundworkProcessProbeRequest Request(
        string probeId,
        AspNetCoreIdentityRestartProbeOperation operation,
        AspNetCoreIdentityRestartProbeUser user) =>
        new(
            probeId,
            ToProcessOperation(operation),
            DocumentId,
            Serialize(new AspNetCoreIdentityRestartProbePayload(operation, user)));

    private static GroundworkProcessProbeOperation ToProcessOperation(AspNetCoreIdentityRestartProbeOperation operation) =>
        operation switch
        {
            AspNetCoreIdentityRestartProbeOperation.CreateUser => GroundworkProcessProbeOperation.IdentityCreateUser,
            AspNetCoreIdentityRestartProbeOperation.FindByNormalizedUserName => GroundworkProcessProbeOperation.IdentityFindByNormalizedUserName,
            AspNetCoreIdentityRestartProbeOperation.DuplicateCreate => GroundworkProcessProbeOperation.IdentityDuplicateCreate,
            _ => throw new ArgumentOutOfRangeException(nameof(operation), operation, null)
        };

    private static string Serialize<T>(T value) =>
        JsonSerializer.Serialize(value, JsonOptions);

    private static T Deserialize<T>(string json) =>
        JsonSerializer.Deserialize<T>(json, JsonOptions)
        ?? throw new InvalidOperationException("The ASP.NET Core Identity restart-probe payload contained JSON null.");

    public static string ComputeSha256(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = false,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
            WriteIndented = false
        };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false));
        return options;
    }
}
