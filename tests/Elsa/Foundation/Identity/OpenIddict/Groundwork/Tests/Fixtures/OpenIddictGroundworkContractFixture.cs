using System.Reflection;
using System.Runtime.ExceptionServices;
using Elsa.Foundation.Identity.OpenIddict.Extensions;
using Groundwork.Documents.Serialization;
using Groundwork.Documents.Store;
using Microsoft.Extensions.DependencyInjection;
using OpenIddict.Abstractions;

namespace Elsa.Foundation.Identity.OpenIddict.Groundwork.Tests.Fixtures;

public static class OpenIddictGroundworkContractFixture
{
    public const string AssemblyName = "Elsa.Foundation.Identity.OpenIddict.Groundwork";
    public const string CodecTypeName =
        "Elsa.Foundation.Identity.OpenIddict.Groundwork.Serialization.OpenIddictGroundworkJson";
    public const string FailureMapperTypeName =
        "Elsa.Foundation.Identity.OpenIddict.Groundwork.Exceptions.OpenIddictGroundworkFailureMapper";
    public const string FeatureTypeName =
        "Elsa.Foundation.Identity.OpenIddict.Groundwork.OpenIddictGroundworkFeature";
    public const string RegistrationTypeName =
        "Elsa.Foundation.Identity.OpenIddict.Groundwork.Extensions.OpenIddictGroundworkServiceCollectionExtensions";
    public const string RegistrationMethodName = "AddFoundationIdentityOpenIddictGroundwork";

    public static IServiceCollection CreateBaselineServices()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddFoundationIdentityOpenIddictBehavior(options => options.IsDevelopmentOrDemo = true);
        return services;
    }

    public static IServiceCollection ApplyGroundworkRegistration(IServiceCollection services)
    {
        var registrationType = RequiredType(RegistrationTypeName);
        var method = registrationType.GetMethods(BindingFlags.Public | BindingFlags.Static)
            .SingleOrDefault(candidate =>
            {
                var parameters = candidate.GetParameters();
                return candidate.Name == RegistrationMethodName &&
                       parameters.Length > 0 &&
                       parameters[0].ParameterType == typeof(IServiceCollection) &&
                       parameters.Skip(1).All(parameter => parameter.IsOptional);
            });

        Assert.True(
            method is not null,
            $"Missing public {RegistrationTypeName}.{RegistrationMethodName}(IServiceCollection, ...optional).");

        var arguments = new object?[method!.GetParameters().Length];
        arguments[0] = services;
        for (var index = 1; index < arguments.Length; index++)
            arguments[index] = Type.Missing;

        Invoke(method, null, arguments);
        return services;
    }

    public static Type RequiredType(string qualifiedName)
    {
        var type = Type.GetType($"{qualifiedName}, {AssemblyName}", throwOnError: false);
        Assert.True(type is not null, $"Missing production contract type '{qualifiedName}' in '{AssemblyName}'.");
        return type!;
    }

    public static VersionedJsonDocumentCodec RequiredCodec()
    {
        var type = RequiredType(CodecTypeName);
        var value =
            type.GetProperty("Codec", BindingFlags.Public | BindingFlags.Static)?.GetValue(null) ??
            type.GetField("Codec", BindingFlags.Public | BindingFlags.Static)?.GetValue(null) ??
            type.GetMethod("CreateCodec", BindingFlags.Public | BindingFlags.Static, Type.EmptyTypes)?.Invoke(null, null);

        Assert.IsType<VersionedJsonDocumentCodec>(value);
        return (VersionedJsonDocumentCodec)value!;
    }

    public static IReadOnlyList<DocumentSchemaVersionPolicy> RequiredPolicies()
    {
        var type = RequiredType(CodecTypeName);
        var value =
            type.GetProperty("Policies", BindingFlags.Public | BindingFlags.Static)?.GetValue(null) ??
            type.GetField("Policies", BindingFlags.Public | BindingFlags.Static)?.GetValue(null) ??
            type.GetMethod("CreatePolicies", BindingFlags.Public | BindingFlags.Static, Type.EmptyTypes)?.Invoke(null, null);

        var policies = Assert.IsAssignableFrom<IEnumerable<DocumentSchemaVersionPolicy>>(value).ToArray();
        Assert.NotEmpty(policies);
        return policies;
    }

    public static IReadOnlyList<IDocumentJsonUpcaster> RequiredUpcasters()
    {
        var type = RequiredType(CodecTypeName);
        var value =
            type.GetProperty("Upcasters", BindingFlags.Public | BindingFlags.Static)?.GetValue(null) ??
            type.GetField("Upcasters", BindingFlags.Public | BindingFlags.Static)?.GetValue(null) ??
            type.GetMethod("CreateUpcasters", BindingFlags.Public | BindingFlags.Static, Type.EmptyTypes)?.Invoke(null, null);

        return Assert.IsAssignableFrom<IEnumerable<IDocumentJsonUpcaster>>(value).ToArray();
    }

    public static VersionedJsonContent Serialize(
        VersionedJsonDocumentCodec codec,
        string documentKind,
        object descriptor)
    {
        var method = typeof(VersionedJsonDocumentCodec)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Single(candidate => candidate.Name == nameof(VersionedJsonDocumentCodec.Serialize) && candidate.IsGenericMethod);

        return Assert.IsType<VersionedJsonContent>(
            Invoke(method.MakeGenericMethod(descriptor.GetType()), codec, [documentKind, descriptor]));
    }

    public static object Deserialize(
        VersionedJsonDocumentCodec codec,
        DocumentEnvelope envelope,
        Type descriptorType)
    {
        var method = typeof(VersionedJsonDocumentCodec)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Single(candidate => candidate.Name == nameof(VersionedJsonDocumentCodec.Deserialize) && candidate.IsGenericMethod);

        var value = Invoke(method.MakeGenericMethod(descriptorType), codec, [envelope]);
        Assert.IsType(descriptorType, value);
        return value!;
    }

    public static DocumentSchemaVersionPolicy PolicyFor(string role) =>
        Assert.Single(RequiredPolicies().Where(policy =>
            policy.DocumentKind.Contains(role, StringComparison.OrdinalIgnoreCase)));

    public static DocumentEnvelope Envelope(
        string kind,
        string schemaVersion,
        string contentJson,
        string id = "contract-record-1") =>
        new(kind, id, schemaVersion, 1, contentJson, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch);

    public static string ShiftVersionStamp(string stamp, int delta)
    {
        var end = stamp.Length - 1;
        while (end >= 0 && !char.IsDigit(stamp[end]))
            end--;

        Assert.True(end >= 0, $"Schema-version stamp '{stamp}' contains no numeric version component.");

        var start = end;
        while (start > 0 && char.IsDigit(stamp[start - 1]))
            start--;

        var digits = stamp[start..(end + 1)];
        var shifted = int.Parse(digits, System.Globalization.CultureInfo.InvariantCulture) + delta;
        Assert.True(shifted >= 0, $"Cannot shift schema-version stamp '{stamp}' by {delta}.");
        return string.Concat(
            stamp.AsSpan(0, start),
            shifted.ToString($"D{digits.Length}", System.Globalization.CultureInfo.InvariantCulture),
            stamp.AsSpan(end + 1));
    }

    public static object? Invoke(MethodInfo method, object? target, object?[]? arguments)
    {
        try
        {
            return method.Invoke(target, arguments);
        }
        catch (TargetInvocationException exception) when (exception.InnerException is not null)
        {
            ExceptionDispatchInfo.Capture(exception.InnerException).Throw();
            throw;
        }
    }

    public static IReadOnlyList<CodecCase> CodecCases { get; } =
    [
        new(
            "application",
            static () => new OpenIddictApplicationDescriptor
            {
                ApplicationType = "web",
                ClientId = "client-1",
                ClientSecret = "secret-1",
                ClientType = "confidential",
                ConsentType = "explicit",
                DisplayName = "Workflow Client"
            },
            static (expected, actual) =>
            {
                var left = Assert.IsType<OpenIddictApplicationDescriptor>(expected);
                var right = Assert.IsType<OpenIddictApplicationDescriptor>(actual);
                Assert.Equal(left.ApplicationType, right.ApplicationType);
                Assert.Equal(left.ClientId, right.ClientId);
                Assert.Equal(left.ClientSecret, right.ClientSecret);
                Assert.Equal(left.ClientType, right.ClientType);
                Assert.Equal(left.ConsentType, right.ConsentType);
                Assert.Equal(left.DisplayName, right.DisplayName);
            }),
        new(
            "authorization",
            static () => new OpenIddictAuthorizationDescriptor
            {
                ApplicationId = "application-1",
                CreationDate = DateTimeOffset.UnixEpoch,
                Status = "valid",
                Subject = "subject-1",
                Type = "permanent"
            },
            static (expected, actual) =>
            {
                var left = Assert.IsType<OpenIddictAuthorizationDescriptor>(expected);
                var right = Assert.IsType<OpenIddictAuthorizationDescriptor>(actual);
                Assert.Equal(left.ApplicationId, right.ApplicationId);
                Assert.Equal(left.CreationDate, right.CreationDate);
                Assert.Equal(left.Status, right.Status);
                Assert.Equal(left.Subject, right.Subject);
                Assert.Equal(left.Type, right.Type);
            }),
        new(
            "scope",
            static () => new OpenIddictScopeDescriptor
            {
                Description = "Run workflows",
                DisplayName = "Workflow execution",
                Name = "workflows:run"
            },
            static (expected, actual) =>
            {
                var left = Assert.IsType<OpenIddictScopeDescriptor>(expected);
                var right = Assert.IsType<OpenIddictScopeDescriptor>(actual);
                Assert.Equal(left.Description, right.Description);
                Assert.Equal(left.DisplayName, right.DisplayName);
                Assert.Equal(left.Name, right.Name);
            }),
        new(
            "token",
            static () => new OpenIddictTokenDescriptor
            {
                ApplicationId = "application-1",
                AuthorizationId = "authorization-1",
                CreationDate = DateTimeOffset.UnixEpoch,
                ExpirationDate = DateTimeOffset.UnixEpoch.AddHours(1),
                Payload = "payload-1",
                ReferenceId = "reference-1",
                Status = "valid",
                Subject = "subject-1",
                Type = "refresh_token"
            },
            static (expected, actual) =>
            {
                var left = Assert.IsType<OpenIddictTokenDescriptor>(expected);
                var right = Assert.IsType<OpenIddictTokenDescriptor>(actual);
                Assert.Equal(left.ApplicationId, right.ApplicationId);
                Assert.Equal(left.AuthorizationId, right.AuthorizationId);
                Assert.Equal(left.CreationDate, right.CreationDate);
                Assert.Equal(left.ExpirationDate, right.ExpirationDate);
                Assert.Equal(left.Payload, right.Payload);
                Assert.Equal(left.ReferenceId, right.ReferenceId);
                Assert.Equal(left.Status, right.Status);
                Assert.Equal(left.Subject, right.Subject);
                Assert.Equal(left.Type, right.Type);
            })
    ];

    public sealed record CodecCase(
        string Role,
        Func<object> CreateDescriptor,
        Action<object, object> AssertEquivalent);
}
