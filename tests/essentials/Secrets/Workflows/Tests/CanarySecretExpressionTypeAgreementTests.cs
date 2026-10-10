using Elsa.Secrets.Core.Models;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Publishing.Services;
using Xunit;

namespace Elsa.Secrets.Workflows.Tests;

/// <summary>
/// The publishing compiler and the credential-literal rule each duplicate the Secrets module's <c>Secret</c> expression
/// type name rather than reference that module (framework §2.17); this project, which references all three, pins that
/// they agree (spec 188, T082). Were one to drift, a secret reference the Studio picker writes would compile as an
/// expression, or be refused at save as a literal.
/// </summary>
public sealed class CanarySecretExpressionTypeAgreementTests
{
    [Fact]
    public void The_publishing_compiler_reads_the_expression_type_the_Secrets_module_declares() =>
        Assert.Equal(SecretExpressionTypes.Secret, RuntimeInputBindingCompiler.SecretExpressionType);

    [Fact]
    public void The_credential_literal_rule_accepts_the_expression_type_the_Secrets_module_declares() =>
        Assert.Equal(SecretExpressionTypes.Secret, CredentialInputBinding.SecretExpressionType);
}
