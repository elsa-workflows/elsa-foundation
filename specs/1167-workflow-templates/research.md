# Built-in workflow starters research

**Date**: 2026-11-22
**Status**: Design research for the built-in starters amendment to [spec 1167](spec.md).

## Backend API Call: Liquid and JSON gap

The requested Backend API Call starter can send an HTTP request today, but the current activity and Liquid contracts do not provide the requested JSON-to-Liquid collection flow:

1. [`SendHttpRequest`](../../essentials/src/Activities/Http/Activities/SendHttpRequest.cs) reads response content as text and returns `SendHttpRequestResult.ResponseBody` as a nullable `string` with `FormattedContent` source representation. It exposes no parsed-JSON output. An expression bound to that output receives the JSON document as a string.
2. [`PortableLiquidExpressionHandler`](../../essentials/src/Expressions/Liquid/Services/PortableLiquidExpressionHandler.cs) evaluates the `binding-pure-v1` profile. It creates a model-less Fluid context from only the explicitly declared parameter roots; it does not expose ambient workflow state or services. Its custom filter setup currently blocks date/time filters and provides no `parse_json` filter, so a Liquid loop cannot iterate over the response body string as products.
3. [`ILiquidFilter`](../../essentials/src/Expressions/Liquid/Contracts/ILiquidFilter.cs) and its [`Base64Filter`](../../essentials/src/Expressions/Liquid/Filters/Base64Filter.cs) and [`DictionaryKeysFilter`](../../essentials/src/Expressions/Liquid/Filters/DictionaryKeysFilter.cs) implementations exist, but [`LiquidExpressionsFeature`](../../essentials/src/Expressions/Liquid/LiquidExpressionsFeature.cs) registers only the handler, parser and expression tooling providers. These filter implementations are not registered with Fluid's `TemplateOptions` today.
4. There is a Set Variable workflow intrinsic (`Elsa.SetVariable`) in [`IntrinsicAuthoringDescriptorProvider`](../../essentials/src/Activities/Design/Api/Services/IntrinsicAuthoringDescriptorProvider.cs), but it is not a CLR activity. It may support an alternative preprocessing graph; it does not add JSON parsing to the binding-pure Liquid contract. The earlier plan statement that no Set Variable-style support exists was inaccurate and is corrected in the amendment plan.

[`WriteHttpResponse`](../../essentials/src/Activities/Http/Activities/WriteHttpResponse.cs) accepts a JSON value as its body and a caller-supplied media type. A rendered string can therefore be returned with `text/html`; the response activity does not itself render Liquid or parse response JSON.

### Recommended approach

Add a binding-pure `parse_json` Liquid filter and register the `ILiquidFilter` implementations supported by the Liquid feature with Fluid's `TemplateOptions`. The filter should convert a valid JSON string into Fluid values, including arrays and objects, so the existing declared `ResponseBody` parameter can be used as an iterable collection. Keep evaluation within the existing binding-pure parameter boundary. Use Liquid's `escape` filter for every upstream value interpolated into HTML.

Illustrative Body binding (the parameter alias and final markup are recipe details, not a committed filter API):

```liquid
{% assign products = responseBody | parse_json %}
<table>
  <thead><tr><th>Product</th><th>Price</th><th>Category</th></tr></thead>
  <tbody>
  {% for product in products %}
    <tr>
      <td>{{ product.title | escape }}</td>
      <td>{{ product.price | escape }}</td>
      <td>{{ product.category | escape }}</td>
    </tr>
  {% endfor %}
  </tbody>
</table>
```

Malformed JSON must fail the binding clearly rather than render a partial or incomplete table. Tests should verify array iteration and HTML escaping with hostile values as well as ordinary product data.

### Alternatives considered

- **Set Variable intrinsic plus JavaScript parsing**: the intrinsic exists and may allow a workflow to parse the response with JavaScript before passing an object to Liquid. This mixes expression languages and adds a JavaScript dependency to a starter whose requested rendering experience is Liquid. It is not the selected path; FR-018 specifies JSON parsing within Liquid bindings.
- **Parsed response output on `SendHttpRequest`**: add an optional structured `ResponseContent` output for JSON responses. This makes parsing reusable to non-Liquid consumers, but changes the activity result contract and requires appropriate activity versioning. It is not selected for this amendment.
- **JavaScript rendering in `WriteHttpResponse.Body`**: a JavaScript binding can use `JSON.parse` and template literals today, but it does not provide the requested Liquid rendering path and still needs safe HTML escaping.

## Backend API Call workflow sketch

The initial recipe is a Flowchart with an `HttpEndpoint` trigger at `/products`, a `SendHttpRequest` GET to an editable URL defaulting to `https://fakestoreapi.com/products`, and a `WriteHttpResponse` returning the rendered table as `text/html`. Bind the request result's `ResponseBody` to the Liquid expression as an explicitly declared activity-result parameter, parse it using `parse_json`, and render the array using the template above. The request's successful outcome proceeds to the response; transport failure and timeout remain activity outcomes and must not be confused with starter availability.

The default upstream is a third-party service and may be unreachable. Tests must use a local HTTP stub rather than the public endpoint, while keeping the URL editable for authors. Host outbound-HTTP policy can also block the request; `SendHttpRequest` represents transport failure or timeout with its `Failed` or `Timeout` outcome rather than making the starter unavailable. The implementation should preserve and handle those outcomes without treating a runtime upstream failure as a catalog-resolution failure.

## Starter recipe and server contract decisions

- A module embeds a recipe with stable id, display name, description, owner, required activity types, required expression languages and authored workflow content. The proposed recipe representation is JSON aligned with Studio-authored workflow state and presentation, but it is not the same as a pinned Studio export.
- Each activity reference is expressed by activity type rather than a fixed activity version. A placeholder node id is replaced with a fresh id for every use; references between nodes must be remapped consistently. The server resolves required activity types against its own activity catalog and chooses the newest available version for each use.
- The server exposes a separate read-only built-in list through a new Design capability link alongside the existing workflow-template links. This keeps the custom template store and its CRUD contract unchanged. Built-ins are not mutable through the custom template API; copying a built-in produces a custom template.
- The host-level removal setting is `Workflows:Design:Templates:RemovedStarters`. It filters matching ids from the built-in catalog for all tenants on that host. Unknown ids are ignored and diagnosed once at startup. Removing every built-in leaves the client-side Blank workflow available.
- Enabled module sources determine which starters exist. A starter from a disabled module is absent; a present starter whose required activity or expression language is unavailable is listed as unavailable with a reason. The server catalog is host-wide, not tenant-specific.
- Studio consumes the capability when present and retains the current client-side HTTP starter for older hosts that do not advertise it. Blank workflow remains client-side because it offers root-activity selection rather than a fixed recipe graph.

## Starter ownership

| Starter | Owning module / dependency |
|---|---|
| Hello World Console | Primitives / Sequence |
| Hello World HTTP | Activities HTTP feature |
| Backend API Call | Activities HTTP feature and Liquid expressions |
| Scheduled job | Scheduling |
| Dispatch child workflow | DispatchWorkflow |
| BPMN process | BPMN |

Per ADR 0080, new server types remain internal by default. Only the starter contribution interface and removal options are public extension points; see [ADR 0080](../../docs/adr/0080-elsa-4-simplification-decisions.md) and [extension points](../../EXTENSION_POINTS.md).