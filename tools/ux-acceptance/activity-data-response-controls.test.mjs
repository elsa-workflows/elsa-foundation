import assert from "node:assert/strict";
import { Buffer } from "node:buffer";
import test from "node:test";
import { setImmediate } from "node:timers/promises";
import { createActivityDataResponseCollector } from "../../Studio/tests/acceptance/activityDataResponseCollector.mjs";

const endpoint = "http://localhost:51947/elsa/api/design/workflows/activity-data/context";
const packet = { definitionId: "owned", documentRevision: "before", nodeId: "consumer", inputKey: "text" };
function response({ address = endpoint, method = "POST", failure = null, completion = null,
  body = '{"state":"supported","documentRevision":"before"}', unreadable = false } = {}) {
  const request = { url: () => address, method: () => method, postDataJSON: () => packet,
    failure: () => failure ? { errorText: failure } : null };
  return { request: () => request, finished: async () => completion,
    body: async () => { if (unreadable) throw new Error("private-error-marker"); return Buffer.from(body); } };
}
function fixture() {
  const contexts = [], transport = [];
  return { contexts, transport, collector: createActivityDataResponseCollector(endpoint, contexts, transport) };
}

test("a completed owned 200 body remains exact context evidence", async () => {
  const f = fixture(); f.collector.observe(response()); await f.collector.settle();
  assert.deepEqual(f.contexts, [{ request: packet, response: { state: "supported", documentRevision: "before" } }]);
  assert.deepEqual(f.transport, []);
});

test("same-request cancellation is transport, never supported admission evidence", async () => {
  const f = fixture(); f.collector.observe(response({ failure: "net::ERR_ABORTED", completion: new Error("aborted"), unreadable: true }));
  await setImmediate(); await f.collector.settle();
  assert.deepEqual(f.contexts, []);
  assert.deepEqual(f.transport, [{ kind: "cancelled", request: packet, failure: "net::ERR_ABORTED" }]);
});

test("uncancelled missing bodies and malformed JSON stay sticky without detached rejection", async () => {
  for (const bad of [response({ unreadable: true }), response({ body: "not JSON" }), response({ failure: "net::ERR_FAILED", completion: new Error("failed") })]) {
    const f = fixture(); f.collector.observe(bad); await setImmediate();
    f.collector.observe(response());
    await assert.rejects(f.collector.settle(), /response evidence incomplete/);
    await assert.rejects(f.collector.settle(), /response evidence incomplete/);
    assert.equal(f.contexts.length, 1); assert.deepEqual(f.transport, []);
  }
});

test("foreign hosts, paths and methods cannot supply context or a cancellation exemption", async () => {
  const f = fixture();
  for (const options of [{ address: endpoint.replace("51947", "51948") }, { address: endpoint + "/other" },
    { address: endpoint + "?other=1" }, { method: "PUT" }])
    f.collector.observe(response({ ...options, failure: "net::ERR_ABORTED", completion: new Error("aborted") }));
  await f.collector.settle(); assert.deepEqual(f.contexts, []); assert.deepEqual(f.transport, []);
});

test("all started observations settle before sticky failure is reported without raw error text", async () => {
  const f = fixture(); let release;
  const waiting = response(); waiting.body = () => new Promise(resolve => { release = () => resolve(Buffer.from('{"state":"supported"}')); });
  f.collector.observe(waiting); f.collector.observe(response({ unreadable: true }));
  f.collector.observe(response({ body: "bad JSON" })); await setImmediate();
  let settled = false; const drain = f.collector.settle().finally(() => { settled = true; });
  // Install the assertion handler immediately, just as the collector does for event tasks.
  const rejected = assert.rejects(drain, error => {
    assert.deepEqual(error.errors.map(item => item.message).sort(),
      ["activity-data-context-body-unavailable", "activity-data-context-json-invalid"]);
    assert.equal(String(error).includes("private-error-marker"), false);
    return true;
  });
  await setImmediate(); assert.equal(settled, false); release(); await rejected;
  assert.equal(f.contexts.length, 1);
  await assert.rejects(f.collector.settle(), /response evidence incomplete/);
});
