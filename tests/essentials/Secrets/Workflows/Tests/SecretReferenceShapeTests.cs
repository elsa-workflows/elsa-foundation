using System.Reflection;
using System.Text.Json;
using Elsa.Secrets.Core.Models;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Runtime.Core.Models;
using Xunit;

namespace Elsa.Secrets.Workflows.Tests;

/// <summary>
/// The authored secret reference payload (<see cref="SecretReferencePayload"/>, spec 188) accepts exactly the members the
/// reference models declare, the Secrets module's <see cref="SecretReference"/> and the runtime's
/// <see cref="RuntimeSecretReference"/>, in the camelCase the Studio secret picker writes. A member added to a model and not
/// to the payload definition (or the reverse) would make the credential-literal rule and publication refuse, or accept,
/// a shape the models disagree with. This project references all three.
/// </summary>
public sealed class SecretReferenceShapeTests
{
    public static TheoryData<Type> ReferenceModels => new() { typeof(SecretReference), typeof(RuntimeSecretReference) };

    [Theory]
    [MemberData(nameof(ReferenceModels))]
    public void The_payload_accepts_exactly_the_members_the_reference_model_declares(Type model) =>
        Assert.Equal(
            model.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Select(property => JsonNamingPolicy.CamelCase.ConvertName(property.Name))
                .Order(StringComparer.Ordinal),
            SecretReferencePayload.MemberNames.Order(StringComparer.Ordinal));
}
