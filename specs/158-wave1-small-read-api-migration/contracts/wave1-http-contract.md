# Wave 1 HTTP and OpenAPI Contract

| Owner | Method | Route | Legacy operation ID | Security | Success / response content |
|---|---|---|---|---|---|
| Elsa.Api.Capabilities | GET | `/capabilities` | `ElsaApiCapabilitiesEndpointsGetCapabilities` | `api-capabilities.read` or wildcard | JSON `ApiCapabilitiesDocument`, 200 |
| Elsa.Attention.Api | GET | `/_elsa/attention/items` | `ElsaAttentionApiEndpointsGetAttentionItems` | `attention.read` or wildcard | JSON `AttentionAggregationResult`, 200; plain text 400 |
| Elsa.Expressions.Api | GET | `/expressions/descriptors` | `ElsaExpressionsApiEndpointsListExpressionDescriptors` | `expressions.read` or wildcard | JSON `ExpressionDescriptorsResponse`, 200 |
| Elsa.Expressions.Api | GET | `/expressions/variable-types` | `ElsaExpressionsApiEndpointsListVariableTypeDescriptors` | `expressions.read` or wildcard | JSON `VariableTypeDescriptorsResponse`, 200 |
| Elsa.Expressions.JavaScript.Rendering | GET | `/javascript/documents/render` | `ElsaExpressionsJavaScriptRenderingEndpointsRenderEndpoint` | `expressions.javascript.render` or wildcard | JSON success envelope, 200; JSON failure envelope, 500 |
| Elsa.Workflows.Runtime.JavaScript | POST | `/javascript/execute` | `ElsaWorkflowsRuntimeJavaScriptActivitiesRunJavaScriptEndpoint` | `workflows.runtime.javascript.execute` or wildcard | JSON `RequestModel` body; JSON success 200, validation 400, failure 500 |
| Elsa.Workflows.Dashboard | GET | `/_elsa/workflows/dashboard/definitions` | `ElsaWorkflowsDashboardGetWorkflowPortfolio` | `workflows.dashboard.read` or wildcard | JSON `WorkflowPortfolioSnapshot`, 200; plain text 400 |
| Elsa.Workflows.Dashboard | GET | `/_elsa/workflows/dashboard/runs` | `ElsaWorkflowsDashboardGetWorkflowRunHealth` | `workflows.dashboard.read` or wildcard | JSON `WorkflowRunHealthSnapshot`, 200; plain text 400 |

All routes are authenticated and have one module ownership marker, Minimal API authoring marker, and permission security disposition. The host application's `IHostEnvironment.ApplicationName` is emitted as the sole OpenAPI tag using standard `ITagsMetadata`.

## Explicit metadata exceptions requiring review

The legacy FastEndpoints OpenAPI baseline advertised `204` for JavaScript rendering and execution,
despite the handlers returning successful `200` JSON responses. It also omitted the rendering `500`
and execution `400`/`500` responses. The migrated contract advertises the truthful runtime matrix;
these are deliberate compatibility corrections and remain review-gated. They are not treated as
unapproved parity until accepted by the issue reviewer.
