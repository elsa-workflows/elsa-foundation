using Elsa.Activities.Design.Core.Contracts;
using Elsa.Workflows.Design.Tests.Unit.BaselineValidatorTests;
using Elsa.Workflows.Design.Validations.Validators;

namespace Elsa.Workflows.Design.Tests.Infrastructure;

internal static partial class CredentialLiteralTestSupport
{
    /// <summary>The real credential-literal validator, judging against <paramref name="catalog"/>, walking the test activity structure.</summary>
    public static CredentialLiteralValidator Validator(IActivityDefinitionLookup catalog) =>
        new(ValidatorTestHelpers.CatalogResolver(catalog), ValidatorTestHelpers.Options(), ValidatorTestHelpers.Walker());
}
