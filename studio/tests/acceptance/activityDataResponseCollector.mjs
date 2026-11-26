import { URL } from "node:url";

/** Observe real bodies without leaking detached rejections into the test lifecycle. */
export function createActivityDataResponseCollector(contextUrl, contexts, transportControls) {
  const endpoint = new URL(contextUrl);
  const pending = [];
  const failures = [];
  const failedRequests = new WeakMap();
  const observedFailures = new WeakSet();
  const fail = category => failures.push(new Error(category));

  function owns(request) {
    const address = new URL(request.url());
    return request.method() === "POST" && address.origin === endpoint.origin &&
      address.pathname === endpoint.pathname && address.search === endpoint.search &&
      !address.username && !address.password && !address.hash;
  }

  function observe(response) {
    // Attach the rejection handler in the same turn as the event, before any body can reject.
    const task = (async () => {
      const request = response.request();
      if (!owns(request)) return;
      const requestBody = request.postDataJSON();
      // Playwright emits requestfailed without resolving Response.finished() on abort.
      // Register this exact Request before yielding; unrelated failures cannot release it.
      const failed = new Promise(resolve => { failedRequests.set(request, resolve); });
      let completionError;
      try {
        completionError = request.failure() || observedFailures.has(request) ? true : await Promise.race([response.finished(), failed]);
      } finally { failedRequests.delete(request); }
      const failure = request.failure()?.errorText;
      if (failure === "net::ERR_ABORTED") {
        transportControls.push({ kind: "cancelled-context-response", request: requestBody, failure });
        return;
      }
      if (completionError || failure) {
        fail("activity-data-context-transport-failed");
        return;
      }
      let body;
      try { body = await response.body(); }
      catch { fail("activity-data-context-body-unavailable"); return; }
      let decoded;
      try { decoded = JSON.parse(body.toString("utf8")); }
      catch { fail("activity-data-context-json-invalid"); return; }
      contexts.push({ request: requestBody, response: decoded });
    })().catch(() => { fail("activity-data-context-observation-failed"); });
    pending.push(task);
  }

  // Waits for every started observation and keeps its failures for settle().
  async function drain() {
    let consumed = 0;
    while (consumed < pending.length) {
      const batch = pending.slice(consumed);
      consumed += batch.length;
      await Promise.all(batch);
    }
  }

  async function settle() {
    await drain();
    if (failures.length) throw new AggregateError(failures, "Activity data response evidence incomplete");
  }

  function requestFailed(request) {
    observedFailures.add(request);
    failedRequests.get(request)?.(true);
  }

  return { observe, requestFailed, drain, settle };
}
