using Elsa.Activities.Http.Activities;
using Elsa.Activities.Http.Connections;
using Elsa.Activities.Http.Constants;
using Elsa.Activities.Http.Contracts;
using Elsa.Activities.Http.Models;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Activities.Http.Services;

/// <summary>Routes before request construction or credentials, then uses the normal HTTP exchange when admitted.</summary>
internal sealed class HttpActivityRequestExecutor(IHttpClientFactory httpClientFactory,
    IHttpConnectionApplicator connectionApplicator, IRuntimeTestEffectGate effects) : IHttpActivityRequestExecutor
{
    public async ValueTask<SendHttpRequestResult> ExecuteAsync(HttpActivityRequest request, ActivityExecutionContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();

        var effectRequest = new RuntimeTestEffectRequest(new RuntimeTestInvocation(context.WorkflowExecutionId,
                context.InvocationId, context.AttemptId, context.ExecutableNodeId), HttpActivityEffectData.EffectKey,
            context.OperationKey + ":http", HttpActivityEffectData.Fingerprint(request));
        var admission = await effects.BeginAsync(effectRequest, cancellationToken);
        if (admission is null || admission.Request != effectRequest || !admission.CanExecute)
            throw HttpActivityEffectData.Refuse("The exact HTTP operation cannot execute again without reconciliation.");

        var credentialsMayHaveExecuted = false;
        try
        {
            SendHttpRequestResult result;
            if (admission.Mode == RuntimeTestEffectMode.Substitute)
            {
                if (admission.Profile != HttpActivityEffectData.SubstituteProfile) throw HttpActivityEffectData.Refuse("The HTTP substitute profile is unavailable.");
                result = HttpActivityEffectData.ReadSubstitute(admission.Configuration);
                await effects.RevalidateAsync(admission, cancellationToken);
            }
            else
            {
                var effectiveRequest = admission.Mode switch
                {
                    RuntimeTestEffectMode.Ordinary => request,
                    RuntimeTestEffectMode.TestSystem when admission.Profile == HttpActivityEffectData.TestSystemProfile =>
                        HttpActivityEffectData.ReadTestSystem(admission.Configuration, request),
                    _ => throw HttpActivityEffectData.Refuse("The HTTP test-system profile is unavailable.")
                };
                await effects.RevalidateAsync(admission, cancellationToken);
                using var testClient = admission.Mode == RuntimeTestEffectMode.TestSystem ? HttpTestSystemClient.Create() : null;
                var client = testClient ?? httpClientFactory.CreateClient(HttpActivityConstants.HttpClientName);
                credentialsMayHaveExecuted = true;
                result = await SendAsync(client, effectiveRequest, admission, context, cancellationToken);
            }
            if (admission.Mode != RuntimeTestEffectMode.Ordinary)
                await effects.CompleteAsync(new RuntimeTestEffectCompletion(admission, RuntimeTestEffectDisposition.Completed,
                    HttpActivityEffectData.Fingerprint(result)), cancellationToken);
            return result;
        }
        catch
        {
            if (admission.Mode != RuntimeTestEffectMode.Ordinary)
            {
                using var completionDeadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                try
                {
                    await effects.CompleteAsync(new RuntimeTestEffectCompletion(admission, credentialsMayHaveExecuted
                        ? RuntimeTestEffectDisposition.Unknown : RuntimeTestEffectDisposition.Refused), completionDeadline.Token);
                }
                catch
                {
                    throw HttpActivityEffectData.Refuse("The HTTP operation's disposition could not be recorded; its admitted responsibility remains unresolved.");
                }
            }
            throw;
        }
    }

    private async ValueTask<SendHttpRequestResult> SendAsync(HttpClient client, HttpActivityRequest request,
        RuntimeTestEffectAdmission admission, ActivityExecutionContext context, CancellationToken cancellationToken)
    {
        using var first = await CreateRequestAsync(request, refreshCredentials: false,
            enforceMappedDestination: admission.Mode == RuntimeTestEffectMode.TestSystem, context, cancellationToken);
        var canRetry = first.Options.TryGetValue(HttpConnectionApplyOptions.HasRefreshableState, out var refreshable) && refreshable;
        if (canRetry && first.Content is not null)
            await first.Content.LoadIntoBufferAsync(cancellationToken);

        var response = await client.SendAsync(first, HttpCompletionOption.ResponseContentRead, cancellationToken);
        if ((int)response.StatusCode == 401 && canRetry)
        {
            response.Dispose();
            await effects.RevalidateAsync(admission, cancellationToken);
            using var retry = await CreateRequestAsync(request, refreshCredentials: true,
                enforceMappedDestination: admission.Mode == RuntimeTestEffectMode.TestSystem, context, cancellationToken);
            response = await client.SendAsync(retry, HttpCompletionOption.ResponseContentRead, cancellationToken);
        }

        using (response)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            return new SendHttpRequestResult((int)response.StatusCode, body, CollectHeaders(response));
        }
    }

    private async ValueTask<HttpRequestMessage> CreateRequestAsync(HttpActivityRequest definition, bool refreshCredentials,
        bool enforceMappedDestination, ActivityExecutionContext context, CancellationToken cancellationToken)
    {
        var request = new HttpRequestMessage(new HttpMethod(definition.Method), definition.Url);
        try
        {
            foreach (var (name, value) in definition.RequestHeaders)
                request.Headers.TryAddWithoutValidation(name, value);
            if (!string.IsNullOrEmpty(definition.Content))
            {
                var contentType = string.IsNullOrWhiteSpace(definition.ContentType) ? "text/plain" : definition.ContentType.Trim();
                request.Content = new StringContent(definition.Content, System.Text.Encoding.UTF8, contentType);
            }
            if (refreshCredentials)
                request.Options.Set(HttpConnectionApplyOptions.RefreshCredentials, true);
            await connectionApplicator.ApplyAsync(request, definition.Connection, context, cancellationToken);
            if (enforceMappedDestination &&
                (request.RequestUri is not { IsAbsoluteUri: true } destination ||
                 !StringComparer.Ordinal.Equals(destination.AbsoluteUri, definition.Url.AbsoluteUri) || request.Headers.Contains("Host")))
                throw HttpActivityEffectData.Refuse("The connection applicator changed the exact mapped HTTP destination or authority.");
            return request;
        }
        catch
        {
            request.Dispose();
            throw;
        }
    }

    private static IDictionary<string, string[]> CollectHeaders(HttpResponseMessage response)
    {
        var result = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, values) in response.Headers)
            result[name] = values.ToArray();
        foreach (var (name, values) in response.Content.Headers)
            result[name] = values.ToArray();
        return result;
    }
}
