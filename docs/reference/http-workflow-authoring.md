# HTTP workflow authoring

Use this guide for published Native and Flowchart endpoints served by Actor Foundation.

In Studio, create a workflow and choose **HTTP request and response** as its starting point. It contains a `POST /hello` endpoint connected to a JSON response. Change the route or response body in the inspector if needed, then publish. **Test HTTP request** calls the published endpoint and shows its actual status, content type and body. It executes a real workflow, so use appropriate test data.

The test dialog reads the workflow HTTP mount from the active server's API capability. It includes any shell or tenant path prefix and does not assume `/workflows/http`. Edit the endpoint route beneath that mount; the dialog does not let you change the server's base path. If the server does not advertise a usable workflow HTTP mount, Studio disables the request rather than guessing one. An unusable host mount is omitted with a server warning; other API capabilities remain available. Hosts must configure `HttpEndpointOptions.BasePath` as a decoded path without percent escapes, so the advertised mount matches ASP.NET's decoded request path.

The Actor endpoint uses **Wait for workflow response** (`Sync`). A reachable Write HTTP Response
supplies the response to the original exchange. A workflow that finishes without a response returns `204`.
A route with no published owner or waiter returns `404`.

Bind a request output when a later activity reads it. The Actor HTTP edge reads JSON and supported text
bodies and captures the decoded template values. Select one HTTP method in the activity. Publication
refuses `Async`, enabled `Authorize`, and configured `Policy`, `RequestTimeout` and `RequestSizeLimit`.
Use a [standalone HTTP resource](../standalone-triggers.md) for authenticated source access or async fan-out.

## Synchronous request deadline

`KernelActivitiesHttp` exposes the shell-wide **Synchronous request timeout**
(`HttpEndpointOptions.SyncRequestTimeout`), which defaults to 30 seconds. After matching the route, the
Actor edge applies this deadline to reading the body, waiting for the Actor reply, and writing the response.
The timeout handler returns `408 Request Timeout` by default when the request deadline expires before
anything has been written. If the deadline interrupts an already-started response, the host aborts the exchange.

Studio's test-request dialog has a 35-second client timeout. Use another HTTP client if you configure
a longer server deadline and need to observe the full interval.

The [classic HTTP compatibility guide](../contributing/workbench-compatibility-tests.md#classic-executable-import-and-http-response-replay)
points to the pinned source for the earlier async response modes and durable endpoint bookmarks.
