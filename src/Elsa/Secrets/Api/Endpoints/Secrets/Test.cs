using Elsa.Api.FastEndpoints.Abstractions;
using Elsa.Secrets.Api.Constants;
using Elsa.Secrets.Api.Requests;
using Elsa.Secrets.Core.Contracts;
using Elsa.Secrets.Core.Models;
using Elsa.Secrets.Core.Permissions;
using FastEndpoints;

namespace Elsa.Secrets.Api.Endpoints.Secrets;

internal sealed class Test(ISecretManager secretManager) : ElsaEndpoint<TestSecretRequest, SecretTestResult>
{
    public override void Configure()
    {
        Post(RouteConstants.GetRoute("{name}/test"));
        ConfigurePermissions(SecretsPermissions.Test);
    }

    public override async Task HandleAsync(TestSecretRequest request, CancellationToken cancellationToken)
    {
        var result = await secretManager.TestAsync(request.Name, cancellationToken);
        await Send.OkAsync(result, cancellationToken);
    }
}
