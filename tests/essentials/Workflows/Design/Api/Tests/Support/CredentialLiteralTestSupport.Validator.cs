using Elsa.Workflows.Design.Core.Services;
using Elsa.Workflows.Design.Validations;
using Elsa.Workflows.Design.Validations.Internal;
using Elsa.Workflows.Design.Validations.Validators;
using Microsoft.Extensions.Options;

namespace Elsa.Workflows.Design.Tests.Infrastructure;

/// <summary>
/// This project's part of the credential-literal fixture the Design test project owns (linked from there, with its
/// <c>StubActivityCatalog</c>; spec 188, FR-008).
/// </summary>
internal static partial class CredentialLiteralTestSupport
{
    /// <summary>The real credential-literal validator, judging against <see cref="Catalog"/>.</summary>
    public static CredentialLiteralValidator Validator() => new(
        new CatalogVersionResolver(Catalog()),
        Options.Create(new WorkflowDesignValidatorOptions()),
        new ActivityTreeWalker(new DefaultActivityStructureService([])));
}
