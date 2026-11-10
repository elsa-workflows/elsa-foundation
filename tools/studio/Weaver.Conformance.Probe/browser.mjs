// Real, owned Studio journeys. This driver never registers a product contribution or fabricates a response.
import fs from "node:fs";
import path from "node:path";
import crypto from "node:crypto";
import { createRequire } from "node:module";

const require = createRequire(new URL("../../../Studio/package.json", import.meta.url));
const { chromium, expect } = require("@playwright/test");
const configPath = process.argv[2];
if (!configPath || !path.isAbsolute(configPath)) throw new Error("Supply an absolute private browser configuration path.");
const config = JSON.parse(fs.readFileSync(configPath, "utf8"));
const supported = new Set(["resource", "workflow", "duplicate-execute", "duplicate-ack", "deny-before-effect", "client-binding-removed", "malformed-result", "lost-confirmation", "renderer-unavailable", "consumed-intent-remount", "sdk-incompatible", "autonomy-size", "workflow-read-only", "workflow-batch", "workflow-explain", "workflow-library", "workflow-templates", "workflow-version-history", "workflow-executable-inspector", "workflow-run"]);
if (!supported.has(config.journey)) throw new Error("Unsupported browser journey: " + config.journey);
const witness = JSON.parse(fs.readFileSync(new URL("./witness.json", import.meta.url), "utf8"));
// T015 (#3440): each lifecycle case reuses one original journey; FullAuto is selected before the conversation exists.
const LIFECYCLE_JOURNEYS = { "visibility-review-control": ["resource", "full-auto"], "workflow-review-control": ["workflow", "full-auto"],
  "interrupted-after-effect": ["lost-confirmation", null] };
const lifecycle = config.lifecycleCase ?? null;
if (lifecycle !== null && LIFECYCLE_JOURNEYS[lifecycle]?.[0] !== config.journey) throw new Error("A lifecycle case must name its exact declared journey.");
function need(condition, message) { if (!condition) throw new Error(message); }
function ownedOrigin(value) {
  const url = new URL(value);
  need(url.protocol === "http:" && url.hostname === "127.0.0.1" && url.port && !["5089", "5095", "7030", "7243"].includes(url.port)
    && !url.username && !url.password && url.pathname === "/" && !url.search && !url.hash, "An explicit owned loopback origin is required.");
  return url.origin;
}
const studio = ownedOrigin(config.studioOrigin), backend = ownedOrigin(config.backendOrigin);
need(studio !== backend, "Backend and Studio must have distinct owned origins.");
need(path.isAbsolute(config.output) && !fs.existsSync(config.output), "Evidence output must be a new absolute directory.");
need(path.isAbsolute(config.storageState) && path.isAbsolute(config.commandFile), "Private auth and protocol command paths are required.");
fs.mkdirSync(config.output, { recursive: true, mode: 0o700 });
const digest = bytes => crypto.createHash("sha256").update(bytes).digest("hex");
const ref = file => ({ path: file, sha256: digest(fs.readFileSync(file)) });
const secretKey = /^(authorization|proxy-authorization|password|apiKey|gitHubToken|runtimeConnectionToken|accessToken|refreshToken|clientSecret|cookie|set-cookie)$/i;
function redact(value) {
  if (Array.isArray(value)) return value.map(redact);
  if (value && typeof value === "object") return Object.fromEntries(Object.entries(value).map(([key, item]) => [key, secretKey.test(key) ? "[redacted]" : redact(item)]));
  return typeof value === "string" ? value.replace(/\bBearer\s+[^\s"\\]+/gi, "Bearer [redacted]")
    .replace(/\bsk-(?:proj-|ant-)?[A-Za-z0-9_-]{12,}/g, "[redacted]")
    .replace(/("(?:authorization|proxy-authorization|password|apiKey|gitHubToken|runtimeConnectionToken|accessToken|refreshToken|clientSecret|cookie|set-cookie)"\s*:\s*)"(?:\\.|[^"\\])*"/gi, '$1"[redacted]"')
    .replace(/\b((?:access[_-]?token|refresh[_-]?token|client[_-]?secret|password|api[_-]?key)=)[^\s&#"']+/gi, "$1[redacted]") : value;
}
function safeFailureFrames(error) {
  const frames = [], seen = new Set();
  for (const line of typeof error?.stack === "string" ? error.stack.split("\n") : []) {
    if (!/^\s+at /.test(line)) continue;
    const match = line.trim().match(/[\\/]browser\.mjs:(\d+):(\d+)\)?$/);
    if (!match) continue;
    const row = Number(match[1]), column = Number(match[2]), key = row + ":" + column;
    if (![row, column].every(value => Number.isSafeInteger(value) && value > 0 && value <= 1000000) || seen.has(key)) continue;
    seen.add(key);
    frames.push({ file: "browser.mjs", line: row, column });
    if (frames.length === 8) break;
  }
  return frames;
}
function retainFailureDiagnostic(error) {
  const diagnostic = { kind: ["Error", "TimeoutError", "AssertionError"].includes(error?.name) ? error.name : "Error", frames: safeFailureFrames(error) };
  try {
    fs.writeFileSync(path.join(config.output, "failure-diagnostic.json"), JSON.stringify(diagnostic) + "\n", { mode: 0o600, flag: "wx" });
  } catch { /* Preserve the original failure even when its diagnostic cannot be written. */ }
  return diagnostic;
}
function diagnosticUrl(value) {
  try { const url = new URL(value); return url.origin + url.pathname; }
  catch { return "[unavailable URL]"; }
}
function retain(name, value) {
  const file = path.join(config.output, name + ".json");
  fs.writeFileSync(file, JSON.stringify(redact(value), null, 2) + "\n", { mode: 0o600 });
  return ref(file);
}
const result = { journey: config.journey, provider: config.provider, evidenceCategory: "deterministic-provider-protocol", liveProvider: false,
  status: "running", observations: {}, evidence: [], startedAt: new Date().toISOString(), noFabricatedResponses: true };
process.on("uncaughtExceptionMonitor", retainFailureDiagnostic);
const browser = await chromium.launch({ headless: true });
result.browserVersion = browser.version();
const context = await browser.newContext({ storageState: config.storageState, viewport: { width: 1920, height: 1080 } });
const page = await context.newPage();
const streamCapture = await captureNativeStreams(page);
// Only the autonomy journey observes its policy route; other journeys keep their qualified capture.
const autonomyCapture = config.journey === "autonomy-size" ? await captureAutonomyExchanges(page) : null;
page.setDefaultTimeout(30000);
const requests = [], owningWrites = [], pageErrors = [], consoleMessages = [], failedRequests = [];
const resourceInteractions = [], moduleOwnerReads = [], pendingOwnerReads = [];
const publishAttempts = [];
await context.exposeBinding("qualificationObserveResourceInteraction", (_source, value) => resourceInteractions.push(value));
await context.addInitScript(observeResourceInteractions, { route: witness.route });
let owningDraftPath;
page.on("pageerror", error => pageErrors.push(error.message));
page.on("console", message => {
  const location = message.location();
  consoleMessages.push(redact({ type: message.type(), text: message.text(), location: {
    url: diagnosticUrl(location.url), lineNumber: location.lineNumber, columnNumber: location.columnNumber
  } }));
});
page.on("requestfailed", request => failedRequests.push(redact({
  url: diagnosticUrl(request.url()), method: request.method(), resourceType: request.resourceType(),
  errorText: request.failure()?.errorText
})));
page.on("request", request => {
  const url = new URL(request.url());
  if (url.origin === backend && owningDraftPath === url.pathname && ["PUT", "POST", "PATCH", "DELETE"].includes(request.method())) {
    owningWrites.push({ url: request.url(), method: request.method(), body: request.postDataJSON() });
  }
  // Publish is an explicit boundary: a lifecycle journey records any attempt to cross it.
  if ((lifecycle || config.journey === "workflow-batch") && url.origin === backend && /publish/i.test(url.pathname) && request.method() !== "GET")
    publishAttempts.push({ url: diagnosticUrl(request.url()), method: request.method() });
  if (url.origin !== backend || !url.pathname.startsWith("/_elsa/weaver/") || !["POST", "PUT", "PATCH", "DELETE"].includes(request.method())) return;
  requests.push({ url: request.url(), method: request.method(), body: request.postDataJSON() });
});
page.on("response", response => {
  const url = new URL(response.url());
  if (url.origin !== backend || ![witness.ownerEndpoint, witness.sizeOwnerEndpoint].includes(url.pathname) || response.request().method() !== "GET") return;
  const observation = { url: diagnosticUrl(response.url()), status: response.status(), completed: false };
  moduleOwnerReads.push(observation);
  pendingOwnerReads.push(response.text().then(rawBody => Object.assign(observation, { rawBody, completed: true }))
    .catch(error => { observation.error = error.message; }));
});
// Spec 1193 T012: the read-only actor holds exactly the grants its journey needs, so any refused backend read names a
// missing grant precisely instead of surfacing later as a missing control.
const refusedReads = [];
if (config.journey === "workflow-read-only") page.on("response", response => {
  const url = new URL(response.url());
  if (url.origin === backend && response.request().method() === "GET" && [401, 403].includes(response.status()))
    refusedReads.push({ path: url.pathname, status: response.status() });
});
// Spec 1193 T028: every exchange the page has with the agent API, raw and in memory only (never retained, so no redactor
// rewrites it): each request's URL and body, whatever its method, and each non-stream response body. Stream bodies are
// the native SSE capture's own raw bytes. The explanation journey searches all of it for the typed canary.
const agentTraffic = [], agentResponseReads = [];
// Spec 1193 T045: the run journey searches the same raw agent traffic for its Run canary.
if (["workflow-explain", "workflow-run"].includes(config.journey)) {
  page.on("request", request => {
    const url = new URL(request.url());
    if (url.origin === backend && url.pathname.startsWith("/_elsa/weaver/"))
      agentTraffic.push({ method: request.method(), url: request.url(), body: request.postData() ?? "" });
  });
  page.on("response", response => {
    const url = new URL(response.url());
    if (url.origin !== backend || !url.pathname.startsWith("/_elsa/weaver/") || /event-stream/i.test(response.headers()["content-type"] ?? "")) return;
    agentResponseReads.push(response.text().then(body => agentTraffic.push({ url: response.url(), status: response.status(), body }))
      .catch(error => agentTraffic.push({ url: response.url(), unreadable: error.message })));
  });
}
const dock = () => page.getByRole("region", { name: "Weaver assistant", exact: true });
// Spec 1193 T036: every write the page sends to the workflow design API (library, templates, definitions), so a journey can
// prove which owner writes happened: none from a page working-state change, only the user's own Save of the template.
const designWrites = [];
if (["workflow-library", "workflow-templates"].includes(config.journey)) page.on("request", request => {
  const url = new URL(request.url());
  if (url.origin === backend && url.pathname.includes("/design/workflows/") && ["POST", "PUT", "PATCH", "DELETE"].includes(request.method()))
    designWrites.push({ path: url.pathname, method: request.method(), body: request.postDataJSON() });
});
// Spec 1193 T041: the read-only inspection journeys may write nothing to the design, publishing or runtime APIs.
// Spec 1193 T045: the run page's journey is read-only in the same way.
const INSPECTION_JOURNEYS = ["workflow-version-history", "workflow-executable-inspector", "workflow-run"];
if (INSPECTION_JOURNEYS.includes(config.journey)) page.on("request", request => {
  const url = new URL(request.url());
  if (url.origin === backend && /\/(design\/workflows|publishing|runtime\/workflows)\//.test(url.pathname) && ["POST", "PUT", "PATCH", "DELETE"].includes(request.method()))
    designWrites.push({ path: url.pathname, method: request.method() });
});
// Reads the pages make by POST (preflights, options) are not effects; a change, promotion, publication, run or deletion is.
const inspectionEffects = () => designWrites.filter(item => item.method !== "POST" || /\/(promote|publish|ingest|execute|test-runs|stop|cancel)$/.test(item.path));
function observeResourceInteractions({ route }) {
  const labels = ["Read with Weaver", "Review visibility change", "Show Acme status card on Home", "Resize with Weaver"];
  let pressed = null, sequence = 0;
  function control(target) {
    if (!(target instanceof Element)) return null;
    const node = target.closest('button, input[type="checkbox"]');
    const label = node?.tagName === "BUTTON" ? node.textContent?.trim() : node?.closest("label")?.textContent?.trim();
    return labels.includes(label) ? { node, label } : null;
  }
  function identity(target) {
    return { tag: target instanceof Element ? target.tagName : null, label: control(target)?.label ?? null };
  }
  function rect(node) {
    const { x, y, width, height } = node.getBoundingClientRect();
    return { x, y, width, height };
  }
  function emit(type, event, active, phase) {
    // Only exact fixture control labels and geometry leave the page. Unknown
    // release/focus targets contribute their tag, never text or input values.
    const target = control(event?.target);
    const value = { type, phase, label: target?.label ?? null, isTrusted: event?.isTrusted,
      disabled: target?.node.disabled, checked: target?.node.tagName === "INPUT" ? target.node.checked : undefined,
      target: identity(event?.target), activeElement: identity(document.activeElement),
      clientX: event?.clientX, clientY: event?.clientY, pointerId: event?.pointerId,
      at: performance.now(), timeOrigin: performance.timeOrigin };
    if (active) Object.assign(value, { activationId: active.id, pressedLabel: active.label,
      pressedElementConnected: active.node.isConnected, pressedElementDisabled: active.node.disabled,
      originalRect: active.rect, currentRect: rect(active.node),
      targetInsidePressedElement: event?.target instanceof Node ? active.node.contains(event.target) : undefined,
      hitAtPressPoint: identity(document.elementFromPoint(active.x, active.y)) });
    void window.qualificationObserveResourceInteraction(value).catch(() => undefined);
  }
  for (const type of ["pointerdown", "mousedown", "pointerup", "mouseup", "click", "change", "pointercancel"])
    document.addEventListener(type, event => {
      if (location.pathname !== route) return;
      const target = control(event.target);
      if (type === "pointerdown") {
        pressed = target ? { ...target, id: ++sequence, rect: rect(target.node), x: event.clientX, y: event.clientY } : null;
        if (pressed) {
          const active = pressed;
          queueMicrotask(() => emit("activation-snapshot", null, active, "microtask"));
          requestAnimationFrame(() => emit("activation-snapshot", null, active, "animation-frame"));
        }
      }
      if (!target && !pressed) return;
      emit(type, event, pressed);
      if (type === "click" || type === "pointercancel") pressed = null;
    }, { capture: true, passive: true });
}
async function captureNativeStreams(target) {
  // Observe the existing browser request only. A clone/second fetch would change
  // stream consumption, and response.body() loses bytes when Studio aborts it.
  const cdp = await context.newCDPSession(target);
  const streams = new Map(), requests = new Map(), pending = [];
  const limit = 8 * 1024 * 1024, sizeError = "Native SSE capture exceeded its bounded evidence size.";
  const hash = async value => Buffer.from(await globalThis.crypto.subtle.digest("SHA-256", value)).toString("hex");
  function sseText(value, allowTrailingIncomplete = false) {
    const text = new TextDecoder("utf-8", { fatal: true, ignoreBOM: true }).decode(value, { stream: allowTrailingIncomplete });
    // This observer qualifies AgentApi.WriteEventAsync's UTF-8/no-BOM data lines,
    // not arbitrary SSE encodings or original compressed/network-wire bytes.
    if (text.startsWith("\ufeff") || text && !text.startsWith("data: "))
      throw new Error("Response bytes do not match the native Agent SSE framing profile.");
    return text;
  }
  function bytes(body, encoded, allowEmpty = false) {
    if (typeof body !== "string" || typeof encoded !== "boolean" || !allowEmpty && !body.length)
      throw new Error("Native SSE response body was absent or empty.");
    if ((encoded ? body.length > 4 * Math.ceil(limit / 3) : Buffer.byteLength(body, "utf8") > limit)) throw new Error(sizeError);
    const value = Buffer.from(body, encoded ? "base64" : "utf8");
    if (value.length > limit) throw new Error(sizeError);
    if (encoded && value.toString("base64") !== body) throw new Error("Native SSE body is not canonical base64.");
    return value;
  }
  async function finishedBytes(response, stream) {
    const { body, base64Encoded } = response;
    if (typeof body !== "string" || typeof base64Encoded !== "boolean" || !body.length || !body.isWellFormed())
      throw new Error("Native SSE response body was absent, empty or lossy Unicode.");
    if (body.length > (base64Encoded ? 4 * Math.ceil(limit / 3) : limit)) throw new Error(sizeError);
    stream.responseBodyBase64Encoded = base64Encoded;
    stream.cdpBodyRepresentation = base64Encoded ? "base64-field-utf8-text" : "decoded-field-utf8-text";
    stream.cdpBodySha256 = await hash(Buffer.from(body, "utf8"));
    const charsetParameters = stream.contentType.split(";").slice(1).map(value => value.trim()).filter(value => /^charset\s*=/i.test(value));
    if (charsetParameters.length > 1) throw new Error("Conflicting native SSE charset metadata.");
    const header = charsetParameters[0]?.match(/^charset\s*=\s*(?:"([^"]*)"|([^\s"]+))$/i);
    if (charsetParameters.length && !header) throw new Error("Unsupported native SSE charset metadata.");
    const headerCharset = header ? (header[1] ?? header[2]).toLowerCase() : null;
    if (typeof stream.reportedCharset !== "string" || typeof stream.reportedMimeType !== "string"
      || stream.reportedMimeType.toLowerCase() !== "text/event-stream") throw new Error("Missing native SSE decoder metadata.");
    const charset = stream.reportedCharset.toLowerCase();
    if (!(charset === "" && headerCharset === null || ["utf-8", "utf8"].includes(charset)
      && (headerCharset === null || ["utf-8", "utf8"].includes(headerCharset))))
      throw new Error("Unsupported or conflicting native SSE charset metadata.");
    let complete;
    if (base64Encoded) {
      complete = bytes(body, true);
      stream.encodingMethod = "cdp-base64-bytes";
    } else if (charset) {
      // U+FFFD may already hide a CDP decoding loss; refuse that ambiguous text.
      if (body.includes("\ufffd")) throw new Error("CDP UTF-8 text may contain a lossy replacement.");
      complete = bytes(body, false);
      if (sseText(complete) !== body) throw new Error("CDP UTF-8 text did not roundtrip exactly.");
      stream.encodingMethod = "declared-utf8-text-roundtrip";
    } else {
      // Chromium 149 InspectorPageAgent defaults unlabelled plaintext to
      // ISO-8859-1; TextCodecLatin1 aliases it to this bijective Windows-1252 table.
      const decoder = new TextDecoder("windows-1252", { fatal: true, ignoreBOM: true });
      const inverse = new Map(Array.from({ length: 256 }, (_, byte) => [decoder.decode(Uint8Array.of(byte)), byte]));
      if (inverse.size !== 256 || inverse.has("\ufffd") || inverse.get("€") !== 0x80 || inverse.get("‚") !== 0x82)
        throw new Error("The Chromium plaintext inverse is not lossless.");
      complete = Buffer.alloc(body.length);
      let index = 0;
      for (const character of body) {
        if (!inverse.has(character)) throw new Error("CDP plaintext contains an unmappable character.");
        complete[index++] = inverse.get(character);
      }
      if (index !== body.length || decoder.decode(complete) !== body) throw new Error("CDP plaintext did not roundtrip exactly.");
      stream.encodingMethod = "chromium-149-default-windows-1252-inverse";
    }
    sseText(complete); // Fatal UTF-8 validation after decoder reversal, without JSON coercion.
    return complete;
  }
  function finishedBody(stream) {
    if (stream.termination !== "loadingFinished" || !stream.streamResourceContentError || stream.captureValidationError || stream.sizeExceeded
      || stream.responseBodyAttempted || stream.status !== 200 || stream.contentType.split(";", 1)[0].trim() !== "text/event-stream") return;
    stream.responseBodyAttempted = true;
    // Retrieve the existing response for this exact CDP request, never replay it.
    pending.push(cdp.send("Network.getResponseBody", { requestId: stream.requestId }).then(async response => {
      const complete = await finishedBytes(response, stream);
      if (stream.sizeExceeded) throw new Error(sizeError);
      if (stream.termination !== "loadingFinished") throw new Error("Native SSE response did not remain finished during body retrieval.");
      stream.completeBody = complete;
      stream.captureMethod = "getResponseBody";
      stream.captureError = null;
    }).catch(error => { stream.responseBodyError = error.message; stream.captureError = error.message; }));
  }
  function safeStreamUrl(value) {
    const url = new URL(value);
    return url.origin === backend && /^\/_elsa\/weaver\/sessions\/[^/]+\/stream$/.test(url.pathname)
      && !url.username && !url.password && !url.search && !url.hash ? url.href : null;
  }
  cdp.on("Network.requestWillBeSent", event => {
    const url = new URL(event.request.url);
    if (url.origin !== backend || !/^\/_elsa\/weaver\/sessions\/[^/]+\/stream$/.test(url.pathname)) return;
    requests.set(event.requestId, { rawUrl: event.request.url,
      count: (requests.get(event.requestId)?.count ?? 0) + 1,
      observation: { source: "Network.requestWillBeSent", requestId: event.requestId,
        method: event.request.method, url: safeStreamUrl(event.request.url), resourceType: event.type ?? null,
        initiatorType: event.initiator?.type ?? null } });
  });
  cdp.on("Network.responseReceived", event => {
    const url = new URL(event.response.url);
    if (url.origin !== backend || !/^\/_elsa\/weaver\/sessions\/[^/]+\/stream$/.test(url.pathname)) return;
    const contentType = Object.entries(event.response.headers).find(([key]) => key.toLowerCase() === "content-type")?.[1] ?? "";
    const isSse = event.response.status === 200 && contentType.split(";", 1)[0].trim() === "text/event-stream";
    const request = requests.get(event.requestId);
    const responseUrl = safeStreamUrl(event.response.url);
    const stream = { requestId: event.requestId, url: diagnosticUrl(url.href), status: event.response.status, contentType,
      requestObservation: request?.observation ?? null, requestObservationCount: request?.count ?? 0,
      responseObservation: { source: "Network.responseReceived", requestId: event.requestId, url: responseUrl, resourceType: event.type ?? null },
      responseObservationCount: (streams.get(event.requestId)?.responseObservationCount ?? 0) + 1,
      requestIdentityMatchesResponse: !!request && request.rawUrl === event.response.url && responseUrl !== null && request.observation.url === responseUrl,
      reportedCharset: event.response.charset ?? null, reportedMimeType: event.response.mimeType ?? null,
      bodyProfile: "native-weaver-api-utf8-no-bom-data-lines", bodyByteRepresentation: "response-body-after-content-decoding",
      termination: "open", captureError: null, captureMethod: isSse ? "streamResourceContent" : "not-sse",
      streamResourceContentError: null, responseBodyError: null, captureValidationError: null, responseBodyAttempted: false,
      receivedDataBytes: 0, dataEventCount: 0, dataLengthMetadataError: null,
      prefix: Buffer.alloc(0), chunks: [], streamedBytes: 0, completeBody: null, sizeExceeded: false };
    streams.set(event.requestId, stream);
    if (!isSse) return;
    pending.push(cdp.send("Network.streamResourceContent", { requestId: event.requestId }).then(response => {
      // CDP bufferedData ends precisely where dataReceived.data starts. Events
      // can arrive before this promise resolves, so prepend this prefix once.
      try {
        const buffered = bytes(response.bufferedData, true, true);
        if (buffered.length + stream.streamedBytes > limit) throw new Error(sizeError);
        stream.prefix = buffered;
      } catch (error) {
        stream.captureValidationError = error.message; stream.captureError = error.message;
        if (error.message === sizeError) stream.sizeExceeded = true;
      }
    }).catch(error => {
      stream.streamResourceContentError = error.message;
      if (error.message === sizeError) stream.sizeExceeded = true;
      if (!stream.captureValidationError) stream.captureError = error.message;
      finishedBody(stream);
    }));
  });
  cdp.on("Network.dataReceived", event => {
    const stream = streams.get(event.requestId);
    if (!stream) return;
    // Observe decoded body lengths even where no SSE body capture was enabled.
    // An OPTIONS204 may be ignored only when this independent observer saw no body.
    stream.dataEventCount++;
    if (!Number.isSafeInteger(event.dataLength) || event.dataLength < 0)
      stream.dataLengthMetadataError = "Network.dataReceived did not supply an exact nonnegative body length.";
    else stream.receivedDataBytes += event.dataLength;
    if (stream.captureMethod === "not-sse" || !event.data || stream.captureError || stream.completeBody) return;
    try {
      const received = bytes(event.data, true, true);
      stream.streamedBytes += received.length;
      if (stream.prefix.length + stream.streamedBytes > limit) throw new Error(sizeError);
      stream.chunks.push(received);
    } catch (error) {
      stream.captureValidationError = error.message; stream.captureError = error.message;
      if (error.message === sizeError) stream.sizeExceeded = true;
    }
  });
  cdp.on("Network.loadingFinished", event => {
    const stream = streams.get(event.requestId);
    if (stream) { stream.termination = "loadingFinished"; finishedBody(stream); }
  });
  cdp.on("Network.loadingFailed", event => {
    const stream = streams.get(event.requestId);
    if (stream) Object.assign(stream, { termination: "loadingFailed", errorText: event.errorText, canceled: event.canceled === true });
  });
  await cdp.send("Network.enable");
  return async () => {
    for (let settled = 0; settled < pending.length;) {
      const batch = pending.slice(settled); settled = pending.length;
      await Promise.all(batch);
    }
    return Promise.all([...streams.values()].map(async ({ prefix, chunks, completeBody, sizeExceeded, ...stream }) => {
      const observed = completeBody ?? Buffer.concat([prefix, ...chunks]);
      const partial = completeBody === null && ["open", "loadingFailed"].includes(stream.termination);
      let rawBody = "", trailingIncompleteUtf8Bytes = null;
      try {
        rawBody = sseText(observed, partial);
        const textBytes = Buffer.from(rawBody, "utf8");
        trailingIncompleteUtf8Bytes = observed.length - textBytes.length;
        if (trailingIncompleteUtf8Bytes < 0 || trailingIncompleteUtf8Bytes > (partial ? 3 : 0)
          || !observed.subarray(0, textBytes.length).equals(textBytes)) throw new Error("Native SSE UTF-8 prefix did not roundtrip exactly.");
      }
      catch (error) { stream.captureValidationError ??= error.message; stream.captureError ??= error.message; }
      return { ...stream, bufferedBytes: prefix.length, completeBodyBytes: completeBody?.length ?? 0,
        observedBodyBytes: observed.length, textDecoding: partial ? "utf8-fatal-stream-prefix" : "utf8-fatal-complete",
        trailingIncompleteUtf8Bytes, textTruncation: trailingIncompleteUtf8Bytes === null ? "unavailable"
          : trailingIncompleteUtf8Bytes ? "trailing-incomplete-utf8" : "none",
        reconstructedBodySha256: await hash(observed), rawBody };
    }));
  };
}
async function captureAutonomyExchanges(target) {
  // Independent CDP metadata for the autonomy route only: every actual PUT and its CORS
  // preflight. Bodies come from the awaited Playwright exchange; nothing is replayed.
  const cdp = await context.newCDPSession(target);
  const rows = new Map();
  const owned = value => {
    try {
      const url = new URL(value);
      return url.origin === backend && /^\/_elsa\/weaver\/sessions\/[^/]+\/autonomy$/.test(url.pathname) ? url : null;
    } catch { return null; }
  };
  const exact = url => url.username || url.password || url.search || url.hash ? null : url.href;
  const row = id => {
    if (!rows.has(id)) rows.set(id, { requestId: id, method: null, url: null, responseUrl: null, resourceType: null, initiatorType: null,
      responseResourceType: null, status: null, requestObservationCount: 0, responseObservationCount: 0, termination: "open",
      errorText: null, canceled: false });
    return rows.get(id);
  };
  cdp.on("Network.requestWillBeSent", event => {
    const url = owned(event.request.url);
    if (!url) return;
    const item = row(event.requestId);
    Object.assign(item, { method: event.request.method, url: exact(url), resourceType: event.type ?? null, initiatorType: event.initiator?.type ?? null });
    item.requestObservationCount++;
  });
  cdp.on("Network.responseReceived", event => {
    const url = owned(event.response.url);
    if (!url) return;
    const item = row(event.requestId);
    Object.assign(item, { responseUrl: exact(url), status: event.response.status, responseResourceType: event.type ?? null });
    item.responseObservationCount++;
  });
  cdp.on("Network.loadingFinished", event => { if (rows.has(event.requestId)) rows.get(event.requestId).termination = "loadingFinished"; });
  cdp.on("Network.loadingFailed", event => {
    if (rows.has(event.requestId)) Object.assign(rows.get(event.requestId), { termination: "loadingFailed", errorText: event.errorText ?? null, canceled: event.canceled === true });
  });
  await cdp.send("Network.enable");
  return () => [...rows.values()].map(item => ({ ...item }));
}
function command(toolName, args) {
  const value = { commandId: crypto.randomUUID(), toolName, arguments: args };
  const temporary = config.commandFile + "." + value.commandId;
  fs.writeFileSync(temporary, JSON.stringify(value), { mode: 0o600 });
  fs.renameSync(temporary, config.commandFile);
  result.evidence.push(retain("command-" + value.commandId, value));
  return value;
}
async function owner(name, endpoint, headers = {}) {
  need(endpoint.startsWith("/") && !endpoint.startsWith("//"), "Owner endpoint must be local to the verified backend.");
  const response = await page.request.get(backend + endpoint, { headers });
  const rawBody = await response.text();
  result.evidence.push(retain(name + "-http", { url: diagnosticUrl(response.url()), status: response.status(), rawBody }));
  need(response.status() === 200, "Owning resource read failed: " + response.status() + " " + endpoint);
  const value = JSON.parse(rawBody);
  const receipt = retain(name, { url: response.url(), status: response.status(), value });
  result.evidence.push(receipt);
  return { value, receipt };
}
async function screenshot(name) {
  const file = path.join(config.output, name + ".png");
  await page.screenshot({ path: file, fullPage: true });
  result.evidence.push(ref(file));
}
async function failureDom() {
  const html = await page.locator("html").evaluate(root => {
    const copy = root.cloneNode(true);
    copy.querySelectorAll("script, style, noscript, template").forEach(element => element.remove());
    for (const element of [copy, ...copy.querySelectorAll("*")]) {
      for (const attribute of [...element.attributes]) {
        if (!/^(id|class|role|hidden|disabled|inert|open|type|title|name|tabindex|checked|aria-[\w-]+|data-testid|data-state|data-status|data-pending)$/.test(attribute.name))
          element.removeAttribute(attribute.name);
      }
      if (element.tagName === "TEXTAREA") element.textContent = "[redacted input]";
    }
    return copy.outerHTML;
  });
  result.evidence.push(retain("failure-dom", { url: diagnosticUrl(page.url()), html }));
}
async function nativeEnter(name, locator, control) {
  const interaction = { method: "keyboard", key: "Enter", control, completed: false };
  try { await locator.press("Enter"); interaction.completed = true; }
  finally { result.evidence.push(retain(name + "-interaction", interaction)); }
}
async function workingDraft(name) {
  const [download] = await Promise.all([
    page.waitForEvent("download"),
    (async () => {
      // The nonmodal dock overlaps the pointer target; native keyboard activation preserves its proposal UI.
      await nativeEnter(name + "-menu", page.getByRole("button", { name: "More workflow actions", exact: true }), "More workflow actions");
      const menu = page.getByRole("menu", { name: "More workflow actions", exact: true });
      await expect(menu.getByRole("menuitemcheckbox", { name: /^Autosave\b/ })).toBeFocused();
      result.evidence.push(retain(name + "-menu-focus", { control: "Autosave", initialFocusObserved: true }));
      await nativeEnter(name + "-export", page.getByRole("menuitem", { name: "Export JSON", exact: true }), "Export JSON");
    })()
  ]);
  const file = path.join(config.output, name + ".json");
  await download.saveAs(file);
  fs.chmodSync(file, 0o600);
  result.evidence.push(ref(file));
  const value = JSON.parse(fs.readFileSync(file, "utf8"));
  need(value.definitionId === config.workflow.definitionId && value.state && Array.isArray(value.layout), "Native working-state export does not match the selected workflow.");
  return value;
}
async function nativeValidation(name) {
  const panel = page.locator(".wf-validation");
  await expect(panel).toContainText("No validation errors");
  await expect(panel).not.toContainText("unavailable");
  result.evidence.push(retain(name, { text: await panel.innerText() }));
}
function localPath(href) {
  const url = new URL(href, backend + "/");
  need(url.origin === backend, "Discovered owner link escaped the verified backend.");
  return url.pathname + url.search;
}
function responseFor(suffix) {
  return page.waitForResponse(response => new URL(response.url()).origin === backend && response.url().endsWith(suffix) && response.request().method() === "POST");
}
async function accepted(responsePromise, name) {
  const response = await responsePromise;
  const rawBody = await response.text();
  result.evidence.push(retain(name + "-http", { url: diagnosticUrl(response.url()), status: response.status(), rawBody }));
  need(response.ok(), name + " failed HTTP " + response.status());
  const value = JSON.parse(rawBody);
  result.evidence.push(retain(name, { url: response.url(), status: response.status(), request: response.request().postDataJSON(), response: value }));
  return { response, value };
}
async function interceptAcknowledgement(mode) {
  const pattern = backend + "/_elsa/weaver/sessions/*/client-invocations/*/acknowledgements";
  let resolveObservation;
  const completed = new Promise(resolve => { resolveObservation = resolve; });
  const timeout = setTimeout(() => resolveObservation({ error: new Error("No actual workflow ACK reached the interception boundary within 30 seconds.") }), 30000);
  let intercepted = false;
  const handler = async route => {
    if (intercepted || route.request().method() !== "POST") return route.continue();
    intercepted = true;
    const request = route.request();
    try {
      const originalText = request.postData(), original = request.postDataJSON();
      expect(original).toMatchObject({ outcome: "applied", result: { typeId: "workflows.layout-change", schemaVersion: "1", data: { observed: true } } });
      result.evidence.push(retain(mode + "-original-ack", { url: request.url(), rawBody: originalText, request: original }));
      if (mode === "malformed-result") {
        const changed = structuredClone(original);
        changed.result.schemaVersion = "unsupported-witness-schema";
        const restored = structuredClone(changed); restored.result.schemaVersion = original.result.schemaVersion;
        expect(restored).toEqual(original);
        result.evidence.push(retain("malformed-result-forwarded-ack", { url: request.url(), request: changed, changedPaths: ["result.schemaVersion"] }));
        await route.continue({ postData: JSON.stringify(changed) });
        clearTimeout(timeout); resolveObservation({ url: request.url(), original, originalText, changed });
      } else {
        // Forward the actual browser ACK once, retaining the server's genuine response before losing delivery.
        const response = await route.fetch();
        const rawBody = await response.text();
        result.evidence.push(retain("lost-confirmation-accepted-http", { url: request.url(), status: response.status(), rawBody }));
        const value = JSON.parse(rawBody);
        need(response.status() === 200, "The ACK whose delivery is lost must actually be accepted by the native server.");
        expect(value.data).toMatchObject({ executed: true, status: "executed", toolResult: { succeeded: true, receipt: { scope: "clientWorkingState" } } });
        await route.abort("failed");
        clearTimeout(timeout); resolveObservation({ url: request.url(), original, originalText, value, responseLost: true });
      }
    } catch (error) {
      result.evidence.push(retain(mode + "-interception-failure", { type: error.name, message: error.message }));
      try { await route.abort("failed"); } catch { /* Preserve the first diagnostic. */ }
      clearTimeout(timeout); resolveObservation({ error });
    }
  };
  await page.route(pattern, handler);
  return { completed, dispose: () => { clearTimeout(timeout); return page.unroute(pattern, handler); } };
}
function observedLifecycle(ack, status, summary) {
  expect(ack.value).toMatchObject({ error: null, data: { executed: false, status, summary, toolResult: {
    succeeded: false, content: summary, payload: { typeId: "weaver.operation-outcome", schemaVersion: "1", data: { status } }, receipt: null
  } } });
}
async function proposalFor(action, name) {
  const message = responseFor("/messages");
  await action();
  const sent = await accepted(message, name + "-message");
  const proposal = dock().locator(".agent-proposal").last();
  await expect(proposal).toHaveAttribute("data-status", "awaiting-approval");
  await expect(proposal.getByRole("button", { name: "Approve", exact: true })).toBeEnabled();
  result.evidence.push(retain(name + "-review", { text: await proposal.innerText(), status: await proposal.getAttribute("data-status") }));
  return { proposal, sent };
}
async function approve(proposal, name) {
  const response = responseFor("/approve");
  await proposal.getByRole("button", { name: "Approve", exact: true }).click();
  const decision = await accepted(response, name + "-approve");
  await expect(proposal).toHaveAttribute("data-status", "approved");
  return decision;
}
async function sendTool(toolName, args, name) {
  command(toolName, args);
  await expect(dock().getByLabel("Ask Weaver", { exact: true })).toBeEnabled();
  await dock().getByLabel("Ask Weaver", { exact: true }).fill(JSON.stringify({ toolName, arguments: args }));
  return proposalFor(() => dock().getByRole("button", { name: "Send message", exact: true }).click(), name);
}
function unrelated(value) {
  // Native revision/UpdatedAt are owner metadata. Compare the entire authored JSON,
  // retaining namespace/schema identity, while leaving that metadata in the receipts.
  const copy = { namespace: value.namespace, schemaVersion: value.schemaVersion, value: structuredClone(value.value) };
  need(copy.value && Array.isArray(copy.value.widgets), "Owner did not return the native Home layout document.");
  const widget = copy.value.widgets.filter(item => item.id === witness.widgetId);
  need(widget.length === 1, "Owner must contain one explicit witness widget.");
  // The owned widget visibility is the only authored field this operation changes.
  delete widget[0].visible;
  return copy;
}
function canonicalLayout(value) {
  return value.map(row => ({ ...row, width: row.width ?? null, height: row.height ?? null, additionalProperties: row.additionalProperties ?? null }));
}
// T012 (#3440): an established conversation's mode is in effect only after the owning backend
// acknowledges exactly one conditional PUT. A selector label alone is never policy evidence.
const AUTONOMY_LABELS = { manual: "Manual review", "full-auto": "Autopilot" };
class AutonomyFailure extends Error {
  constructor(cause, message) { super(message); this.name = "AutonomyFailure"; this.autonomyCause = cause; }
}
function autonomyNeed(condition, cause, message) { if (!condition) throw new AutonomyFailure(cause, message); }
function canonical(value) {
  return JSON.stringify(value, (_key, item) => item && typeof item === "object" && !Array.isArray(item)
    ? Object.fromEntries(Object.entries(item).sort(([left], [right]) => left < right ? -1 : left > right ? 1 : 0)) : item);
}
function autonomyPolicy(value) {
  return !!value && typeof value === "object" && !Array.isArray(value)
    && canonical(Object.keys(value).sort()) === canonical(["allowedAutonomyModes", "autonomyMode", "maxAutonomyMode", "revision"])
    && typeof value.autonomyMode === "string" && typeof value.maxAutonomyMode === "string"
    && Array.isArray(value.allowedAutonomyModes) && value.allowedAutonomyModes.every(item => typeof item === "string")
    && Number.isSafeInteger(value.revision) && value.revision >= 0;
}
async function requireAcknowledgedSelector(mode, cause) {
  const selector = dock().locator(".weaver-auto-apply");
  const button = selector.getByRole("button", { name: "Auto-apply mode", exact: true });
  try {
    await expect(selector).toHaveAttribute("data-autonomy-state", "acknowledged");
    await expect(button).toHaveText(AUTONOMY_LABELS[mode]);
    await expect(button).toHaveAttribute("aria-busy", "false");
    await expect(dock().getByRole("status", { name: "Auto-apply mode status", exact: true })).toHaveCount(0);
  } catch (error) {
    throw new AutonomyFailure(cause, "The selector does not show only the acknowledged " + mode + " mode: " + error.message);
  }
  const observed = { state: await selector.getAttribute("data-autonomy-state"), pendingMode: await selector.getAttribute("data-pending-mode"),
    ariaBusy: await button.getAttribute("aria-busy"), label: (await button.innerText()).trim(), statusCount: 0, mode };
  autonomyNeed(observed.state === "acknowledged" && observed.pendingMode === null && observed.label === AUTONOMY_LABELS[mode], cause,
    "An acknowledged selector still names a pending or different mode.");
  return observed;
}
async function changeEstablishedMode(sessionId, requestedMode, acknowledged) {
  const updatePath = "/_elsa/weaver/sessions/" + encodeURIComponent(sessionId) + "/autonomy";
  const offset = requests.length;
  await requireAcknowledgedSelector(acknowledged.autonomyMode, "autonomy.selector_unacknowledged");
  const selector = dock().locator(".weaver-auto-apply");
  const button = selector.getByRole("button", { name: "Auto-apply mode", exact: true });
  await expect(button).toBeEnabled();
  const update = page.waitForResponse(response => {
    const url = new URL(response.url());
    return url.origin === backend && url.pathname === updatePath && response.request().method() === "PUT";
  }, { timeout: 30000 });
  update.catch(() => undefined); // A failed menu action reports its own error first.
  await button.click();
  await dock().getByRole("menu", { name: "Auto-apply mode", exact: true })
    .getByRole("menuitemradio", { name: new RegExp("^" + AUTONOMY_LABELS[requestedMode] + "(?:\\s|$)") }).click();
  let response;
  try { response = await update; }
  catch (error) {
    const sent = requests.slice(offset).some(item => item.method === "PUT" && item.url === backend + updatePath);
    throw new AutonomyFailure(sent ? "autonomy.update_lost" : "autonomy.update_not_sent",
      (sent ? "The actual autonomy PUT received no response: " : "No actual autonomy PUT reached the owned backend: ") + error.message);
  }
  const rawBody = await response.text(), request = response.request(), body = request.postDataJSON();
  result.evidence.push(retain("autonomy-mode-update-http", { url: diagnosticUrl(response.url()), method: request.method(), status: response.status(), rawBody }));
  let value = null;
  try { value = JSON.parse(rawBody); } catch { /* The raw retained body is the evidence. */ }
  const receipt = retain("autonomy-mode-update", { url: response.url(), method: request.method(), status: response.status(), request: body, response: value });
  result.evidence.push(receipt);
  autonomyNeed(canonical(body) === canonical({ autonomyMode: requestedMode, expectedRevision: acknowledged.revision }), "autonomy.update_body_mismatch",
    "The selector must send only the requested mode and the revision acknowledged at creation.");
  autonomyNeed(response.status() === 200 && value && value.error == null && autonomyPolicy(value.data), "autonomy.update_refused",
    "The owning backend refused the established-session mode change: HTTP " + response.status() + " " + (value?.error?.code ?? "without an error code"));
  const ack = value.data;
  autonomyNeed(ack.autonomyMode === requestedMode && ack.revision === acknowledged.revision + 1 && ack.maxAutonomyMode === acknowledged.maxAutonomyMode
    && canonical(ack.allowedAutonomyModes) === canonical(acknowledged.allowedAutonomyModes), "autonomy.acknowledgement_mismatch",
    "The acknowledgement is not the requested mode at the next revision with unchanged limits.");
  const selectorState = await requireAcknowledgedSelector(requestedMode, "autonomy.selector_unacknowledged");
  const updates = requests.slice(offset);
  autonomyNeed(updates.length === 1 && updates[0].method === "PUT" && updates[0].url === backend + updatePath, "autonomy.update_duplicated",
    "Exactly one actual autonomy request may change the established mode.");
  result.evidence.push(retain("autonomy-mode-selector", selectorState));
  return { acknowledgement: ack, receipt, selector: selectorState };
}
async function readTurnSettled(sessionId, messageId) {
  // Policy claims are released when the accepted message is terminal; read the owner without retaining polls.
  for (let attempt = 0; attempt < 20; attempt++) {
    const response = await page.request.get(backend + "/_elsa/weaver/sessions/" + encodeURIComponent(sessionId));
    const value = response.status() === 200 ? await response.json() : null;
    if (value?.data?.messages?.find(item => item.id === messageId)?.status === "completed") return;
    await page.waitForTimeout(250);
  }
}
async function autonomySize() {
  const cases = {
    "automatic-size-control": ["full-auto", "full-auto"],
    "reviewed-size-control": ["manual", "manual"],
    "automatic-to-manual": ["full-auto", "manual"],
    "manual-to-automatic": ["manual", "full-auto"]
  };
  const pair = cases[config.autonomyCase];
  need(pair, "The autonomy qualification requires an exact declared case key.");
  const [initialMode, requestedMode] = pair;
  async function selectDraftMode(mode) {
    // No conversation exists yet: this selection only controls the real create request.
    const selector = dock().locator(".weaver-auto-apply");
    const button = selector.getByRole("button", { name: "Auto-apply mode", exact: true });
    await expect(button).toBeEnabled();
    await button.click();
    await dock().getByRole("menu", { name: "Auto-apply mode", exact: true })
      .getByRole("menuitemradio", { name: new RegExp("^" + AUTONOMY_LABELS[mode] + "(?:\\s|$)") }).click();
    await expect(button).toHaveText(AUTONOMY_LABELS[mode]);
    await expect(selector).toHaveAttribute("data-autonomy-state", "requested");
  }
  await page.goto(studio + witness.route);
  await expect(page.getByTestId("acme-home-revision")).toBeVisible();
  await expect(page.getByTestId("acme-home-size")).not.toHaveText("Not available");
  const before = await owner("size-owner-before", witness.sizeOwnerEndpoint);
  const layoutBefore = await owner("size-layout-before", "/_elsa/studio/preferences/home-layout", { "X-Elsa-Studio-Host-Id": witness.hostId });
  const bootstrap = await owner("autonomy-native-bootstrap", "/_elsa/weaver/bootstrap");
  const installedCapabilities = bootstrap.value.data?.capabilities;
  need(Array.isArray(installedCapabilities) && installedCapabilities.some(item => item.id === witness.sizeCapability && item.moduleId === witness.moduleId),
    "The actual backend must advertise the module's size capability before this journey.");
  result.ownerBefore = before.receipt;
  need(["small", "medium"].includes(before.value.size), "A canonical non-noop native size is required.");
  await page.getByRole("button", { name: /^Weaver/ }).click();
  await expect(dock()).toBeVisible();
  await selectDraftMode(initialMode);
  need(requests.length === 0, "The initial mode must be selected before the first native session or prompt.");
  const readCommand = command(witness.readTool, {});
  const created = responseFor("/sessions"), readMessage = responseFor("/messages");
  await page.getByRole("button", { name: "Read with Weaver", exact: true }).click();
  const creation = await accepted(created, "autonomy-create-session");
  expect(creation.response.request().postDataJSON().autonomyMode).toBe(initialMode);
  const createdPolicy = creation.value.data?.policy;
  autonomyNeed(autonomyPolicy(createdPolicy) && createdPolicy.autonomyMode === initialMode
    && createdPolicy.allowedAutonomyModes.includes(initialMode) && createdPolicy.allowedAutonomyModes.includes(requestedMode),
    "autonomy.create_policy_missing", "The new session did not acknowledge the requested initial mode with a readable policy.");
  const read = await accepted(readMessage, "autonomy-read-message");
  const sessionId = creation.value.data?.sessionId;
  need(typeof sessionId === "string" && sessionId && read.value.data?.message?.sessionId === sessionId,
    "The harmless read must belong to the actual newly created session.");
  await expect(dock().getByRole("region", { name: "Module results", exact: true }).getByTestId("acme-home-result")).toBeVisible();
  await expect(dock()).toHaveAttribute("aria-busy", "false");
  expect((await owner("size-owner-after-read", witness.sizeOwnerEndpoint)).value).toEqual(before.value);
  await readTurnSettled(sessionId, read.value.data?.message?.id);
  const initialSession = await owner("autonomy-session-before-selection", "/_elsa/weaver/sessions/" + encodeURIComponent(sessionId));
  expect(initialSession.value.data?.sessionId).toBe(sessionId);
  autonomyNeed(canonical(initialSession.value.data?.policy) === canonical(createdPolicy), "autonomy.readback_mismatch",
    "Session detail does not read back the policy acknowledged at creation.");
  autonomyNeed(initialSession.value.data.messages?.find(item => item.id === read.value.data?.message?.id)?.status === "completed",
    "autonomy.session_not_idle", "The original read was not terminal in its owner before the mode decision.");
  const selectionOffset = requests.length;
  let update = null;
  if (initialMode !== requestedMode) update = await changeEstablishedMode(sessionId, requestedMode, createdPolicy);
  else result.evidence.push(retain("autonomy-mode-selector", await requireAcknowledgedSelector(initialMode, "autonomy.selector_unacknowledged")));
  const effectivePolicy = update ? update.acknowledgement : createdPolicy;
  const selectionRequests = requests.slice(selectionOffset);
  autonomyNeed(selectionRequests.length === (update ? 1 : 0), update ? "autonomy.update_duplicated" : "autonomy.unexpected_update",
    "An unchanged control sends no autonomy request; a transition sends exactly one.");
  const selectedSession = await owner("autonomy-session-after-selection", "/_elsa/weaver/sessions/" + encodeURIComponent(sessionId));
  autonomyNeed(selectedSession.value.data?.sessionId === sessionId && canonical(selectedSession.value.data?.policy) === canonical(effectivePolicy),
    "autonomy.readback_mismatch", "Session detail does not read back the acknowledged mode and revision.");
  const targetSize = before.value.size === "small" ? "medium" : "small";
  const sizeCommand = command(witness.sizeTool, { size: targetSize });
  await page.getByRole("combobox", { name: "Personal card size", exact: true }).selectOption(targetSize);
  const sizeMessage = responseFor("/messages");
  await page.getByRole("button", { name: "Resize with Weaver", exact: true }).click();
  const sent = await accepted(sizeMessage, "autonomy-size-message");
  expect(sent.value.data?.message?.sessionId).toBe(sessionId);
  expect(new URL(sent.response.url()).pathname).toBe("/_elsa/weaver/sessions/" + sessionId + "/messages");
  const sentBody = sent.response.request().postDataJSON();
  // The current host forwards its first active capability; the module prompt source
  // identifies the size action. Do not invent action-specific capability propagation.
  expect(sentBody.requestedCapabilities).toEqual(read.response.request().postDataJSON().requestedCapabilities);
  need(sentBody.requestedCapabilities.length === 1 && installedCapabilities.some(item => item.id === sentBody.requestedCapabilities[0]
    && item.moduleId === witness.moduleId), "The requested capability must be actually installed for this module.");
  expect(sentBody.source).toMatchObject({ moduleId: witness.moduleId, actionId: witness.sizeTool });
  expect(sentBody.contextAttachments).toEqual(expect.arrayContaining([expect.objectContaining({ moduleId: witness.moduleId,
    contentType: witness.contextType, target: expect.objectContaining({ resourceId: witness.widgetId, revision: before.value.revision }) })]));
  const acceptedPolicy = sent.value.data?.message?.acceptedPolicy;
  // The accepted message carries exactly the acknowledged public session policy, never the private domain policy.
  autonomyNeed(canonical(acceptedPolicy) === canonical(effectivePolicy),
    "autonomy.attribution_mismatch", "The size prompt was not accepted under the acknowledged policy revision.");
  // A visible module preview/result distinguishes real review from real application; idle alone can race dispatch.
  await expect(dock().getByTestId("acme-home-size-result").last()).toBeVisible();
  await expect(dock()).toHaveAttribute("aria-busy", "false");
  const pending = dock().locator('.agent-proposal[data-status="awaiting-approval"]').filter({ has: page.getByTestId("acme-home-size-result") });
  const pendingCount = await pending.count();
  need(pendingCount <= 1, "The single size request created duplicate review proposals.");
  const outcome = pendingCount === 1 ? "review" : "automatic";
  // Approval and application change data-status on the same card, so keep it by
  // its module result rather than by the status that classified the outcome.
  const sizeProposal = dock().locator(".agent-proposal").filter({ has: page.getByTestId("acme-home-size-result") });
  const atDecision = await owner("size-owner-at-decision", witness.sizeOwnerEndpoint);
  const layoutAtDecision = await owner("size-layout-at-decision", "/_elsa/studio/preferences/home-layout", { "X-Elsa-Studio-Host-Id": witness.hostId });
  const expectedOutcome = requestedMode === "manual" ? "review" : "automatic";
  const mismatch = expectedOutcome !== outcome;
  // The acknowledged mode is authoritative: a different native outcome is a real failure, never an expected one.
  autonomyNeed(!mismatch, "autonomy.mode_mismatch", "The acknowledged " + requestedMode + " mode produced a " + outcome + " outcome.");
  if (outcome === "review") {
    expect(atDecision.value).toEqual(before.value);
    expect(layoutAtDecision.value).toEqual(layoutBefore.value);
    await expect(sizeProposal).toHaveCount(1);
    await expect(sizeProposal).toHaveAttribute("data-status", "awaiting-approval");
    await expect(sizeProposal.getByRole("button", { name: "Approve", exact: true })).toBeEnabled();
    result.evidence.push(retain("autonomy-pending-review", { status: await sizeProposal.getAttribute("data-status"), text: await sizeProposal.innerText() }));
    await approve(sizeProposal, "autonomy-size");
    expect((await owner("size-owner-after-approval", witness.sizeOwnerEndpoint)).value).toEqual(before.value);
    const executed = responseFor("/execute");
    await sizeProposal.getByRole("button", { name: "Apply", exact: true }).click();
    await accepted(executed, "autonomy-size-execute");
    await expect(sizeProposal).toHaveAttribute("data-status", "executed");
    await expect(sizeProposal.getByTestId("acme-home-size-result")).toContainText("independently confirmed the saved size");
    await expect(dock()).toHaveAttribute("aria-busy", "false");
  } else {
    expect(atDecision.value.size).toBe(targetSize);
    expect(atDecision.value.revision).not.toBe(before.value.revision);
    await expect(dock().getByTestId("acme-home-size-result").last()).toContainText("independently confirmed the saved size");
  }
  const after = await owner("size-owner-after", witness.sizeOwnerEndpoint);
  const layoutAfter = await owner("size-layout-after", "/_elsa/studio/preferences/home-layout", { "X-Elsa-Studio-Host-Id": witness.hostId });
  result.ownerAfter = after.receipt;
  const finalSession = await owner("autonomy-session-after-outcome", "/_elsa/weaver/sessions/" + encodeURIComponent(sessionId));
  const finalData = finalSession.value.data;
  expect(finalData?.sessionId).toBe(sessionId);
  expect(requests.filter(item => new URL(item.url).pathname === "/_elsa/weaver/sessions")).toHaveLength(1);
  expect(requests.filter(item => new URL(item.url).pathname.endsWith("/messages"))).toHaveLength(2);
  autonomyNeed(requests.filter(item => new URL(item.url).pathname.endsWith("/autonomy")).length === selectionRequests.length,
    update ? "autonomy.update_duplicated" : "autonomy.unexpected_update", "A later autonomy request changed the established policy.");
  autonomyNeed(canonical(finalData?.policy) === canonical(effectivePolicy), "autonomy.readback_mismatch",
    "The final session detail no longer reads back the acknowledged policy.");
  // Every message keeps the revision current at its own acceptance: the original read under the
  // created policy and the size turn under the acknowledged one. A later value never relabels history.
  const sizeIndex = finalData.messages.findIndex(item => item.id === sent.value.data?.message?.id);
  autonomyNeed(sizeIndex > 0 && finalData.messages.every((item, index) => item.acceptedPolicyRevision === (index < sizeIndex ? createdPolicy.revision : effectivePolicy.revision)),
    "autonomy.attribution_mismatch", "Session history is not attributed to the policy revisions that accepted it.");
  const attribution = await dock().locator(".weaver-message").evaluateAll(nodes => nodes.map(node => ({ role: node.getAttribute("data-role"),
    revision: node.getAttribute("data-accepted-policy-revision"), autonomyMode: node.getAttribute("data-accepted-autonomy-mode") })));
  result.evidence.push(retain("autonomy-ui-attribution", attribution));
  const attributed = attribution.filter(item => item.revision !== null);
  const revisionModes = new Map([[String(createdPolicy.revision), createdPolicy.autonomyMode], [String(effectivePolicy.revision), effectivePolicy.autonomyMode]]);
  autonomyNeed(attributed.length >= 2 && attributed.every(item => revisionModes.get(item.revision) === item.autonomyMode)
    && attributed[0].revision === String(createdPolicy.revision) && attributed.at(-1).revision === String(effectivePolicy.revision),
    "autonomy.attribution_mismatch", "Visible results are not attributed to the policy that admitted their turn.");
  expect(after.value.size).toBe(targetSize);
  expect(after.value.revision).not.toBe(before.value.revision);
  result.autonomy = { caseKey: config.autonomyCase, initialMode, requestedMode, expectedOutcome, outcomeAtDecision: outcome,
    sameSessionId: sessionId, readCommandId: readCommand.commandId, sizeCommandId: sizeCommand.commandId,
    readMessageId: read.value.data?.message?.id, sizeMessageId: sent.value.data?.message?.id,
    readRequestId: read.value.data?.message?.requestId, sizeRequestId: sent.value.data?.message?.requestId,
    selectionRequests, targetSize, applied: true, modeMismatch: false, policyAcknowledgementClaimed: update !== null,
    createdPolicy, acknowledgedPolicy: update?.acknowledgement ?? null, effectivePolicy, policyUpdate: update?.receipt ?? null,
    ownerAtDecision: atDecision.receipt, layoutBefore: layoutBefore.receipt, layoutAtDecision: layoutAtDecision.receipt,
    layoutAfter: layoutAfter.receipt, sessionBefore: initialSession.receipt, sessionAfterSelection: selectedSession.receipt, sessionAfter: finalSession.receipt };
  result.observations = { sameConversation: true, initialModeBeforeCreation: true, acknowledgedBeforePrompt: true,
    ownerObserved: true, noImplicitExecute: outcome !== "automatic" || !requests.some(item => item.url.endsWith("/execute")) };
  await screenshot("autonomy-native-outcome");
}
async function lifecycleBegin() {
  // Select the declared draft mode before the first native request, exactly as the mode cases do.
  if (!lifecycle) return null;
  const mode = LIFECYCLE_JOURNEYS[lifecycle][1];
  if (!await dock().isVisible()) {
    await page.getByRole("button", { name: /^Weaver/ }).click();
    await expect(dock()).toBeVisible();
  }
  if (mode) {
    need(requests.length === 0, "The lifecycle mode must be selected before the first native request.");
    const selector = dock().locator(".weaver-auto-apply");
    const button = selector.getByRole("button", { name: "Auto-apply mode", exact: true });
    await expect(button).toBeEnabled();
    await button.click();
    await dock().getByRole("menu", { name: "Auto-apply mode", exact: true })
      .getByRole("menuitemradio", { name: new RegExp("^" + AUTONOMY_LABELS[mode] + "(?:\\s|$)") }).click();
    await expect(button).toHaveText(AUTONOMY_LABELS[mode]);
    await expect(selector).toHaveAttribute("data-autonomy-state", "requested");
  }
  const response = responseFor("/sessions");
  response.catch(() => undefined); // A failed first request reports its own error first.
  // Wrapped: an async function returning the promise itself would wait for the request it precedes.
  return { response };
}
async function lifecycleCreated(created) {
  if (!created) return;
  const creation = await accepted(created.response, "autonomy-create-session");
  const mode = LIFECYCLE_JOURNEYS[lifecycle][1];
  const policy = creation.value.data?.policy, sessionId = creation.value.data?.sessionId;
  need(typeof sessionId === "string" && sessionId && autonomyPolicy(policy)
    && (!mode || creation.response.request().postDataJSON().autonomyMode === mode && policy.autonomyMode === mode),
  "The lifecycle conversation did not acknowledge its selected mode with a readable policy.");
  result.lifecycle = { caseKey: lifecycle, sessionId, createdPolicy: policy, requestedMode: mode };
}
async function lifecycleFinish() {
  // The originating turn has already settled; read the owning conversation once, never retrying work.
  if (!lifecycle) return;
  need(result.lifecycle?.sessionId, "The lifecycle conversation was never acknowledged.");
  const session = await owner("lifecycle-session-after", "/_elsa/weaver/sessions/" + encodeURIComponent(result.lifecycle.sessionId));
  need(session.value.data?.sessionId === result.lifecycle.sessionId, "The lifecycle conversation changed.");
  result.lifecycle.sessionAfter = session.receipt;
}
async function resource() {
  await page.goto(studio + witness.route);
  await expect(page.getByTestId("acme-home-revision")).toBeVisible();
  const before = await owner("owner-before", witness.ownerEndpoint);
  const layoutBefore = await owner("layout-before", "/_elsa/studio/preferences/home-layout", { "X-Elsa-Studio-Host-Id": witness.hostId });
  result.ownerBefore = before.receipt;
  await page.goto(studio + "/dashboard");
  if (before.value.visible) await expect(page.getByTestId("acme-home-widget")).toBeVisible();
  else await expect(page.getByTestId("acme-home-widget")).toHaveCount(0);
  await screenshot("home-before");
  await page.goto(studio + witness.route);
  await expect(page.getByTestId("acme-home-revision")).toHaveText(before.value.revision);
  const created = await lifecycleBegin();
  command(witness.readTool, {});
  const readMessage = responseFor("/messages");
  await page.getByRole("button", { name: "Read with Weaver", exact: true }).click();
  const read = await accepted(readMessage, "read-message");
  await lifecycleCreated(created);
  const readResult = dock().getByRole("region", { name: "Module results", exact: true }).getByTestId("acme-home-result");
  await expect(readResult).toContainText(`The owning resource reports ${before.value.visible ? "visible" : "hidden"}.`);
  await expect(readResult.getByTestId("acme-home-result-revision")).toHaveText(before.value.revision);
  result.evidence.push(retain("resource-read-rendering", { text: await readResult.innerText(),
    revision: await readResult.getByTestId("acme-home-result-revision").textContent(), owner: before.receipt }));
  await expect(dock()).toHaveAttribute("aria-busy", "false");
  expect((await owner("owner-after-read", witness.ownerEndpoint)).value).toEqual(before.value);
  const next = !before.value.visible;
  command(witness.writeTool, { visible: next });
  await page.getByLabel("Show Acme status card on Home", { exact: true }).setChecked(next);
  const { proposal, sent } = await proposalFor(() => page.getByRole("button", { name: "Review visibility change", exact: true }).click(), "resource");
  const body = sent.response.request().postDataJSON();
  expect(body.contextAttachments).toEqual(expect.arrayContaining([expect.objectContaining({ moduleId: witness.moduleId, contentType: witness.contextType,
    target: expect.objectContaining({ resourceId: witness.widgetId, revision: before.value.revision }) })]));
  result.observations.context = true;
  result.observations.read = true;
  await expect(proposal.getByTestId("acme-home-result")).toContainText("only after approval and application");
  expect((await owner("owner-at-review", witness.ownerEndpoint)).value).toEqual(before.value);
  result.observations.review = true;
  if (config.journey === "deny-before-effect") {
    const deny = responseFor("/deny");
    await proposal.getByRole("button", { name: "Deny", exact: true }).click();
    await accepted(deny, "deny");
    await expect(proposal).toHaveAttribute("data-status", "denied");
    await expect(proposal.getByRole("button", { name: "Apply", exact: true })).toBeDisabled();
    const after = await owner("owner-after", witness.ownerEndpoint);
    expect(after.value).toEqual(before.value);
    result.ownerAfter = after.receipt;
    result.observations = { ownerObserved: true, noFalseSuccess: true, noReplay: true };
    result.expectedFailureObserved = true;
    result.actual = { stage: "review", diagnostic: "denied" };
    result.status = "failed"; // Expected refusal remains a failed child, never converted to a passed mutation.
    await screenshot("denied");
    return;
  }
  await approve(proposal, "resource");
  expect((await owner("owner-after-approval", witness.ownerEndpoint)).value).toEqual(before.value);
  const executed = responseFor("/execute");
  await proposal.getByRole("button", { name: "Apply", exact: true }).click();
  const execution = await accepted(executed, "resource-execute");
  const receipt = execution.value.data?.toolResult?.receipt;
  expect(receipt).toMatchObject({ scope: "serverPersisted", before: { resourceId: witness.widgetId, revision: before.value.revision }, after: { resourceId: witness.widgetId } });
  await expect(proposal).toHaveAttribute("data-status", "executed");
  await expect(proposal.getByTestId("acme-home-result")).toContainText("independently confirmed the saved visibility");
  const after = await owner("owner-after", witness.ownerEndpoint);
  expect(after.value.visible).toBe(next);
  expect(after.value.revision).not.toBe(before.value.revision);
  expect(after.value.revision).toBe(receipt.after.revision);
  const appliedResult = proposal.getByTestId("acme-home-result");
  await expect(appliedResult).toContainText(`to ${after.value.visible ? "visible" : "hidden"}.`);
  await expect(appliedResult.getByTestId("acme-home-result-revision")).toHaveText(after.value.revision);
  result.ownerAfter = after.receipt;
  const layoutAfter = await owner("layout-after", "/_elsa/studio/preferences/home-layout", { "X-Elsa-Studio-Host-Id": witness.hostId });
  expect(unrelated(layoutAfter.value)).toEqual(unrelated(layoutBefore.value));
  result.observations.ownerReadback = true;
  result.observations.unrelatedPreserved = true;
  if (config.journey === "duplicate-execute") {
    const repeated = await page.request.post(execution.response.url(), { data: execution.response.request().postDataJSON() });
    need(repeated.ok(), "Duplicate execute did not return its retained result.");
    const repeatedBody = await repeated.json();
    expect(repeatedBody.data.toolResult).toEqual(execution.value.data.toolResult);
    result.evidence.push(retain("duplicate-execute", { status: repeated.status(), response: repeatedBody }));
    expect((await owner("owner-after-duplicate", witness.ownerEndpoint)).value).toEqual(after.value);
    expect((await owner("layout-after-duplicate", "/_elsa/studio/preferences/home-layout", { "X-Elsa-Studio-Host-Id": witness.hostId })).value).toEqual(layoutAfter.value);
    result.observations = { retainedResult: true, oneMutation: true };
  }
  await screenshot("resource-applied");
  // Applied owner/render evidence can precede receipt-stream authentication and
  // completion. A full navigation here would cancel that still-running turn.
  await expect(dock()).toHaveAttribute("aria-busy", "false");
  await page.goto(studio + "/dashboard");
  if (next) await expect(page.getByTestId("acme-home-widget")).toBeVisible();
  else await expect(page.getByTestId("acme-home-widget")).toHaveCount(0);
  expect((await owner("owner-after-home-reload", witness.ownerEndpoint)).value).toEqual(after.value);
  if (config.journey === "resource") result.observations.homeReload = true;
  await screenshot("home-reloaded");
  await lifecycleFinish();
  // actualAdapter is attached by the Python coordinator only after matching the protocol trace.
  result.readMessage = { url: read.response.url(), status: read.response.status() };
}
async function workflow() {
  const { definitionId, activityId, definitionPath } = config.workflow ?? {};
  need(definitionId && activityId && definitionPath, "Workflow journey needs exact owning IDs and discovered readback path.");
  await page.goto(studio + "/workflows/definitions?definition=" + encodeURIComponent(definitionId));
  // Attribute filtering avoids interpolating an arbitrary activity ID into a CSS selector.
  const activity = page.locator(".react-flow__node").locator("xpath=self::*[@data-id=" + xpathString(activityId) + "]");
  await expect(activity).toBeVisible();
  await expect(page.locator(".wf-editor-save-status")).not.toHaveAttribute("data-pending", "true");
  const before = await owner("owner-before", definitionPath);
  result.ownerBefore = before.receipt;
  const draftId = before.value.draft?.id;
  need(draftId, "The owning workflow must have an actual editable draft.");
  const capabilities = (await owner("workflow-capabilities", "/capabilities")).value;
  const links = capabilities.capabilities.filter(item => item.id === "elsa.api.workflow-design").flatMap(item => item.links);
  const draftLink = links.find(item => item.rel === "workflow-drafts")?.href;
  const validationLink = links.find(item => item.rel === "workflow-draft-validations")?.href;
  need(draftLink && validationLink, "Native draft Save and validation relations are required.");
  owningDraftPath = new URL(localPath(draftLink.replace("{draftId}", encodeURIComponent(draftId))), backend).pathname;
  const validationPath = localPath(validationLink.replace("{draftId}", encodeURIComponent(draftId)));
  const workingBefore = await workingDraft("working-before");
  await nativeValidation("validation-before");
  const position = await activity.getAttribute("style");
  await page.getByRole("button", { name: /^Weaver/ }).click();
  const created = await lifecycleBegin();
  const args = { activityId, x: 480, y: 320 };
  need(!position?.includes("translate(480px, 320px)"), "Workflow baseline must differ from the requested position.");
  const { proposal, sent } = await sendTool("workflow_set_designer_position", args, "workflow");
  await lifecycleCreated(created);
  expect(sent.response.request().postDataJSON().contextAttachments).toEqual(expect.arrayContaining([expect.objectContaining({ contentType: "workflow.definition",
    target: expect.objectContaining({ resourceId: definitionId }), client: expect.objectContaining({ generationId: expect.any(String) }) })]));
  await approve(proposal, "workflow");
  if (config.journey === "client-binding-removed") {
    // Navigate with native SPA routing: the open assistant survives while the editor binding is disposed.
    await page.getByRole("navigation", { name: "Main", exact: true }).getByRole("link", { name: "Home", exact: true }).click();
    await expect(activity).toHaveCount(0);
    const execution = responseFor("/execute"), acknowledged = responseFor("/acknowledgements");
    await proposal.getByRole("button", { name: "Apply", exact: true }).click();
    const delivered = await accepted(execution, "removed-client-execute");
    need(delivered.response.status() === 202, "Removed-client control must reach the actual browser execution boundary.");
    const ack = await accepted(acknowledged, "removed-client-ack");
    const actualAck = ack.response.request().postDataJSON();
    expect(actualAck).toMatchObject({ outcome: "refused", summary: "The originating workflow editor is unavailable." });
    need(actualAck.result == null && actualAck.after == null, "Removed editor unexpectedly returned an applied observation.");
    observedLifecycle(ack, "refused", "The originating client refused the operation before application.");
    // "refused" is the typed operation outcome; a non-executed refusal maps to
    // the proposal's Failed status in both the owning store and Studio client.
    await expect(proposal).toHaveAttribute("data-status", "failed");
    await expect(proposal).toContainText(ack.value.data.summary);
    const after = await owner("owner-after", definitionPath);
    expect(after.value.draft).toEqual(before.value.draft);
    result.ownerAfter = after.receipt;
    expect(owningWrites).toEqual([]);
    expect(requests.filter(item => item.url.endsWith("/execute"))).toHaveLength(1);
    expect(requests.filter(item => item.url.endsWith("/acknowledgements"))).toHaveLength(1);
    result.observations = { ownerObserved: true, noFalseSuccess: true, noReplay: true };
    result.expectedFailureObserved = true;
    result.actual = { stage: "client-execution", diagnostic: actualAck.summary };
    result.status = "failed";
    return;
  }
  const special = ["malformed-result", "lost-confirmation"].includes(config.journey);
  const interception = special ? await interceptAcknowledgement(config.journey) : null;
  const executed = responseFor("/execute");
  const acknowledged = config.journey === "lost-confirmation" ? null : responseFor("/acknowledgements");
  await proposal.getByRole("button", { name: "Apply", exact: true }).click();
  const delivery = await accepted(executed, "workflow-execute");
  need(delivery.response.status() === 202, "Workflow application must be delivered to its originating browser.");
  let intercepted, ack;
  if (interception) {
    intercepted = await interception.completed;
    await interception.dispose();
    if (intercepted.error) throw intercepted.error;
    expect(intercepted.original).toMatchObject({ outcome: "applied", result: { typeId: "workflows.layout-change", data: { ...args, observed: true } } });
  }
  if (acknowledged) ack = await accepted(acknowledged, "workflow-ack");
  if (config.journey === "malformed-result") {
    observedLifecycle(ack, "uncertain", "The reported client effect did not validate against the exact prepared operation. Its outcome is uncertain and will not be replayed.");
    await expect(proposal).toHaveAttribute("data-status", "uncertain");
    await expect(proposal).toContainText(ack.value.data.summary);
    await expect(proposal.getByRole("button", { name: "Retry confirmation", exact: true })).toHaveCount(0);
  } else if (config.journey === "lost-confirmation") {
    await expect(proposal).toHaveAttribute("data-status", "uncertain");
    await expect(proposal).toContainText("Confirmation was not received. Retry sends the same observed receipt without running the change again.");
    await expect(proposal.getByRole("button", { name: "Retry confirmation", exact: true })).toBeEnabled();
    result.evidence.push(retain("lost-confirmation-uncertain-ui", { status: await proposal.getAttribute("data-status"), text: await proposal.innerText() }));
  } else {
    expect(ack.response.request().postDataJSON()).toMatchObject({ outcome: "applied", result: { typeId: "workflows.layout-change", data: { ...args, observed: true } } });
    expect(ack.value.data?.toolResult?.receipt?.scope).toBe("clientWorkingState");
    await expect(proposal).toHaveAttribute("data-status", "executed");
  }
  await expect(activity).toHaveAttribute("style", /translate\(480px, 320px\)/);
  const workingApplied = await workingDraft("working-applied");
  expect(workingApplied.state).toEqual(workingBefore.state);
  expect(workingApplied.activityPresentation).toEqual(workingBefore.activityPresentation);
  const appliedPosition = workingApplied.layout.find(item => item.nodeId === activityId);
  const priorPosition = workingBefore.layout.find(item => item.nodeId === activityId);
  need(priorPosition, "The workflow witness needs an explicit authored layout row.");
  expect(canonicalLayout([appliedPosition])).toEqual(canonicalLayout([{ ...priorPosition, x: args.x, y: args.y }]));
  expect(workingApplied.layout.filter(item => item.nodeId !== activityId)).toEqual(workingBefore.layout.filter(item => item.nodeId !== activityId));
  await nativeValidation("validation-after-local-apply");
  result.observations.workingStateChanged = true;
  expect((await owner("owner-after-local-apply", definitionPath)).value.draft).toEqual(before.value.draft);
  // Observe beyond the native autosave interval, retaining every attempted owning write.
  if (config.journey !== "lost-confirmation") await expect(dock()).toHaveAttribute("aria-busy", "false");
  await page.waitForTimeout(1600);
  expect((await owner("owner-after-autosave-window", definitionPath)).value.draft).toEqual(before.value.draft);
  expect(owningWrites).toEqual([]);
  result.observations.noImplicitSave = true;
  await screenshot("workflow-local");
  if (config.journey === "malformed-result") {
    const after = await owner("owner-after", definitionPath);
    expect(after.value.draft).toEqual(before.value.draft);
    expect(requests.filter(item => item.url.endsWith("/execute"))).toHaveLength(1);
    expect(requests.filter(item => item.url.endsWith("/acknowledgements"))).toHaveLength(1);
    result.ownerAfter = after.receipt;
    result.observations = { ...result.observations, ownerObserved: true, noFalseSuccess: true, noReplay: true, actualLocalApply: true, exactSchemaMutation: true };
    result.expectedFailureObserved = true;
    result.actual = { stage: "client-acknowledgement", diagnostic: ack.value.data.summary };
    result.status = "failed";
    return;
  }
  if (config.journey === "lost-confirmation") {
    const retried = responseFor("/acknowledgements");
    await proposal.getByRole("button", { name: "Retry confirmation", exact: true }).click();
    const confirmed = await accepted(retried, "confirmation-retry");
    expect(confirmed.response.url()).toBe(intercepted.url);
    expect(confirmed.response.request().postData()).toBe(intercepted.originalText);
    expect(confirmed.response.request().postDataJSON()).toEqual(intercepted.original);
    expect(confirmed.value).toEqual(intercepted.value);
    await expect(proposal).toHaveAttribute("data-status", "executed");
    // The late receipt resumes its originating turn; let that continuation settle before Undo.
    if (lifecycle) await expect(dock()).toHaveAttribute("aria-busy", "false");
    expect(await workingDraft("working-after-confirmation-retry")).toEqual(workingApplied);
    await proposal.getByRole("button", { name: "Undo whole change", exact: true }).click();
    await expect(activity).toHaveAttribute("style", position);
    expect(await workingDraft("working-after-confirmation-undo")).toEqual(workingBefore);
    await nativeValidation("validation-after-confirmation-undo");
    const after = await owner("owner-after", definitionPath);
    expect(after.value.draft).toEqual(before.value.draft);
    expect(owningWrites).toEqual([]);
    expect(requests.filter(item => item.url.endsWith("/execute"))).toHaveLength(1);
    expect(requests.filter(item => item.url.endsWith("/acknowledgements"))).toHaveLength(2);
    result.ownerAfter = after.receipt;
    result.observations = { ...result.observations, ownerObserved: true, noFalseSuccess: true, noReplay: true, responseLost: true, uncertain: true,
      sameAcknowledgement: true, sameInvocation: true, retainedResult: true, wholeUndo: true };
    result.expectedFailureObserved = true;
    result.actual = { stage: "client-confirmation-delivery", diagnostic: "net::ERR_FAILED" };
    need(failedRequests.some(item => item.url === diagnosticUrl(intercepted.url) && item.errorText === result.actual.diagnostic), "The real accepted ACK response was not observed as lost browser delivery.");
    await lifecycleFinish();
    result.status = "failed";
    return;
  }
  if (config.journey === "duplicate-ack") {
    const duplicate = await page.request.post(ack.response.url(), { data: ack.response.request().postDataJSON() });
    need(duplicate.ok(), "Identical accepted ACK did not return its retained result.");
    const value = await duplicate.json();
    expect(value.data.toolResult).toEqual(ack.value.data.toolResult);
    result.evidence.push(retain("duplicate-ack", { status: duplicate.status(), response: value }));
    await expect(activity).toHaveAttribute("style", /translate\(480px, 320px\)/);
    expect((await owner("owner-after-duplicate", definitionPath)).value.draft).toEqual(before.value.draft);
    result.observations = { retainedResult: true, oneMutation: true };
    result.ownerAfter = (await owner("owner-after", definitionPath)).receipt;
    return;
  }
  await proposal.getByRole("button", { name: "Undo whole change", exact: true }).click();
  await expect(activity).toHaveAttribute("style", position);
  expect(await workingDraft("working-after-undo")).toEqual(workingBefore);
  await nativeValidation("validation-after-undo");
  expect((await owner("owner-after-undo", definitionPath)).value.draft).toEqual(before.value.draft);
  result.observations.wholeUndo = true;
  // Apply a separately reviewed second invocation before explicitly choosing the native Save action.
  const second = await sendTool("workflow_set_designer_position", args, "workflow-save");
  await approve(second.proposal, "workflow-save");
  const ackAgain = responseFor("/acknowledgements");
  await second.proposal.getByRole("button", { name: "Apply", exact: true }).click();
  await accepted(ackAgain, "workflow-save-ack");
  await expect(second.proposal).toHaveAttribute("data-status", "executed");
  await expect(dock()).toHaveAttribute("aria-busy", "false");
  expect(owningWrites).toEqual([]);
  const saved = page.waitForResponse(response => new URL(response.url()).origin === backend && response.request().method() === "PUT"
    && new URL(response.url()).pathname === owningDraftPath);
  await nativeEnter("explicit-save", page.getByRole("button", { name: "Save", exact: true }), "Save");
  const saveResponse = await saved;
  need(saveResponse.ok(), "Explicit native workflow Save failed.");
  result.evidence.push(retain("explicit-save", { status: saveResponse.status(), request: saveResponse.request().postDataJSON(), response: await saveResponse.json() }));
  const after = await owner("owner-after", definitionPath);
  need(JSON.stringify(after.value.draft) !== JSON.stringify(before.value.draft), "Explicit Save did not change the owning workflow draft.");
  expect(after.value.draft.state).toEqual(workingApplied.state);
  expect(canonicalLayout(after.value.draft.layout)).toEqual(canonicalLayout(workingApplied.layout));
  result.ownerAfter = after.receipt;
  await page.reload();
  await expect(activity).toHaveAttribute("style", /translate\(480px, 320px\)/);
  const validated = await owner("saved-draft-validations", validationPath);
  expect(validated.value).toMatchObject({ draftId, errors: [] });
  await nativeValidation("validation-after-saved-reload");
  result.observations.validated = true;
  result.observations.explicitSaveReadback = true;
  await screenshot("workflow-saved-reloaded");
  await lifecycleFinish();
}
async function rendererUnavailable() {
  const { definitionId, activityId, definitionPath, draftPath, htmlCanary } = config.workflow ?? {};
  need(definitionId && activityId && definitionPath && draftPath && typeof htmlCanary === "string" && htmlCanary.includes("<"),
    "Renderer fallback needs a real workflow with an authored markup-like activity display name.");
  await page.goto(studio + "/workflows/definitions?definition=" + encodeURIComponent(definitionId));
  await expect(page.locator(".react-flow__node").locator("xpath=self::*[@data-id=" + xpathString(activityId) + "]")).toBeVisible();
  owningDraftPath = new URL(localPath(draftPath), backend).pathname;
  const before = await owner("owner-before", definitionPath);
  result.ownerBefore = before.receipt;
  const workingBefore = await workingDraft("working-before");
  need(workingBefore.activityPresentation?.some(item => item.nodeId === activityId && item.displayName === htmlCanary),
    "The markup-like label must come from the actual owning workflow presentation.");
  await page.getByRole("button", { name: /^Weaver/ }).click();
  const selected = command("workflow_read_graph", {});
  const message = responseFor("/messages");
  await expect(dock().getByLabel("Ask Weaver", { exact: true })).toBeEnabled();
  await dock().getByLabel("Ask Weaver", { exact: true }).fill("Read the open workflow graph.");
  await dock().getByRole("button", { name: "Send message", exact: true }).click();
  const sent = await accepted(message, "renderer-read-message");
  const article = dock().getByRole("region", { name: "Module results", exact: true }).locator('article[data-succeeded="true"]').last();
  const fallback = article.locator(":scope > pre");
  await expect(fallback).toBeVisible();
  await expect(dock()).toHaveAttribute("aria-busy", "false");
  const text = await fallback.textContent();
  const payload = JSON.parse(text);
  expect(payload).toMatchObject({ typeId: "workflows.graph", schemaVersion: "1", data: { workflowDefinitionId: definitionId } });
  expect(payload.data.activities).toEqual(expect.arrayContaining([expect.objectContaining({ id: activityId, displayName: htmlCanary })]));
  await expect(fallback.locator("*")).toHaveCount(0);
  expect(await fallback.evaluate(element => element.childElementCount)).toBe(0);
  const composer = dock().getByLabel("Ask Weaver", { exact: true });
  await expect(composer).toBeEnabled();
  await composer.fill("Another explicit request remains available.");
  await expect(dock().getByRole("button", { name: "Send message", exact: true })).toBeEnabled();
  await composer.fill(""); // Checking the common controls does not submit a second intent.
  expect(await workingDraft("working-after")).toEqual(workingBefore);
  const after = await owner("owner-after", definitionPath);
  expect(after.value).toEqual(before.value);
  expect(owningWrites).toEqual([]);
  expect(requests.filter(request => new URL(request.url).pathname.endsWith("/messages"))).toHaveLength(1);
  result.ownerAfter = after.receipt;
  result.evidence.push(retain("structured-fallback", { commandId: selected.commandId, toolName: selected.toolName,
    messageId: sent.value.data?.message?.id, payload, text, childElementCount: await fallback.evaluate(element => element.childElementCount),
    presentation: "native structured JSON fallback for workflows.graph" }));
  result.observations = { safeGenericPresentation: true, commonControls: true, ownerObserved: true };
  await screenshot("renderer-fallback");
}

async function incompatibleModule() {
  const expected = config.incompatibleModule;
  need(expected?.moduleId === witness.moduleId && typeof expected.packageId === "string" && expected.packageId
    && typeof expected.version === "string" && expected.version && expected.requiredSdkVersion === "999.0.0",
  "The incompatible journey requires the actually installed incompatible archive identity.");
  need(config.ownerPath === "/_elsa/studio/preferences/home-layout",
    "Observe the normal persisted owner while the resource backend extension is absent.");
  await page.goto(studio + "/diagnostics/modules");
  await expect(page.getByRole("heading", { name: "Module load status", exact: true })).toBeVisible();
  const before = await owner("owner-before", config.ownerPath, { "X-Elsa-Studio-Host-Id": witness.hostId });
  result.ownerBefore = before.receipt;
  const diagnostic = page.locator(".diagnostic-row").filter({ has: page.locator("strong", { hasText: expected.moduleId }) })
    .filter({ has: page.locator('[data-status="incompatible"]') });
  await expect(diagnostic.first()).toBeVisible();
  await expect(diagnostic.first()).toContainText(expected.requiredSdkVersion);
  const observedStatus = await diagnostic.first().locator('[data-status="incompatible"]').getAttribute("data-status");
  result.evidence.push(retain("incompatible-module-diagnostic", { expectedArchive: expected,
    url: diagnosticUrl(page.url()), status: observedStatus, text: await diagnostic.allTextContents() }));
  await screenshot("incompatible-diagnostic-before-removal");
  await page.goto(studio + witness.route);
  await expect(page.locator(".empty-state")).toContainText("No Studio route is registered for " + witness.route);
  await expect(page.getByRole("link", { name: "Acme status card", exact: true })).toHaveCount(0);
  await expect(page.locator(".acme-home-settings, .acme-home-card, .acme-home-result")).toHaveCount(0);
  await page.getByRole("navigation", { name: "Main", exact: true }).getByRole("link", { name: "Home", exact: true }).click();
  await expect(page.locator(".topbar h1")).toHaveText("Home");
  await expect(page.getByTestId("acme-home-widget")).toHaveCount(0);
  await page.getByRole("navigation", { name: "Main", exact: true }).getByRole("link", { name: "Workflows", exact: true }).click();
  await expect(page.locator(".topbar h1")).toContainText("Workflow");
  await expect(page.locator("main")).not.toContainText("No Studio route is registered");
  await page.getByRole("button", { name: /^Weaver/ }).click();
  await expect(dock().getByTestId("weaver-provider-status")).toBeVisible();
  await expect(dock().getByRole("button", { name: "Close assistant", exact: true })).toBeEnabled();
  await expect(dock().locator(".agent-context-chip").filter({ hasText: "Acme status card" })).toHaveCount(0);
  await expect(dock().getByTestId("acme-home-result")).toHaveCount(0);
  result.evidence.push(retain("common-studio-usable", { url: diagnosticUrl(page.url()), title: await page.locator(".topbar h1").innerText(),
    providerStatus: await dock().getByTestId("weaver-provider-status").innerText(), noPromptSubmitted: requests.length === 0 }));
  await screenshot("incompatible-common-studio-usable");
  await dock().getByRole("button", { name: "Close assistant", exact: true }).click();
  const after = await owner("owner-after", config.ownerPath, { "X-Elsa-Studio-Host-Id": witness.hostId });
  expect(after.value).toEqual(before.value);
  expect(requests).toEqual([]);
  result.ownerAfter = after.receipt;
  result.observations = { ownerObserved: true, noFalseSuccess: true, noReplay: true,
    browserContributionsAbsent: true, commonStudioUsable: true };
  result.status = "failed";
  result.expectedFailureObserved = true;
  result.actual = { stage: "module-admission", diagnostic: observedStatus };
}

async function consumedIntentRemount() {
  need(path.isAbsolute(config.traceFile ?? "") && fs.existsSync(config.traceFile), "Remount proof needs the actual selected provider trace.");
  const offset = fs.statSync(config.traceFile).size;
  const callsFor = commandId => {
    const bytes = fs.readFileSync(config.traceFile).subarray(offset);
    need(bytes.length === 0 || bytes.at(-1) === 10, "A partial provider trace cannot prove no replay.");
    return bytes.toString("utf8").split("\n").filter(Boolean).map(line => JSON.parse(line))
      .filter(row => row.kind === config.provider + ".call" && row.data.commandId === commandId)
      .map(row => ({ kind: row.kind, commandId: row.data.commandId, toolName: row.data.toolName }));
  };
  await page.goto(studio + witness.route);
  await expect(page.getByTestId("acme-home-revision")).toBeVisible();
  const backendReference = new URL(page.url()).searchParams.get("backend");
  need(backendReference, "The prepared backend must be explicit in native Studio resource URLs.");
  const studioRoute = route => {
    const url = new URL(route, studio);
    url.searchParams.set("backend", backendReference);
    return url.toString();
  };
  const before = await owner("owner-before", witness.ownerEndpoint);
  result.ownerBefore = before.receipt;
  const selected = command(witness.readTool, {});
  const message = responseFor("/messages");
  await page.getByRole("button", { name: "Read with Weaver", exact: true }).click();
  const sent = await accepted(message, "consumed-intent");
  const original = sent.response.request().postDataJSON();
  need(typeof original.requestId === "string" && original.requestId && original.source?.moduleId === witness.moduleId,
    "The module action must deliver a correlated shell intent.");
  await expect(dock().getByTestId("acme-home-result")).toContainText("The owning resource reports");
  await expect(dock()).toHaveAttribute("aria-busy", "false");
  expect(callsFor(selected.commandId)).toHaveLength(1);
  const sessionPath = new URL(sent.response.url()).pathname.replace(/\/messages$/, "");
  const sessionBefore = await owner("session-before-remount", sessionPath);
  // Refresh the actual capability document through the module's public SDK action.
  // Keep App state and the consumed shell intent in this document while the registry/dock remount.
  const registryDock = await dock().elementHandle();
  need(registryDock, "The original assistant must exist before capability refresh.");
  const originalTimeOrigin = await page.evaluate(() => performance.timeOrigin);
  const capabilities = page.waitForResponse(response => new URL(response.url()).origin === backend
    && new URL(response.url()).pathname === "/capabilities");
  const registryBootstrap = page.waitForRequest(request => new URL(request.url()).origin === backend
    && new URL(request.url()).pathname === "/_elsa/weaver/bootstrap");
  await nativeEnter("remount-capability-refresh", page.getByRole("button", { name: "Refresh backend capabilities", exact: true }), "Refresh actual backend capabilities");
  need((await capabilities).status() === 200, "The real capability read failed.");
  await expect.poll(() => registryDock.evaluate(element => element.isConnected)).toBe(false);
  await expect(dock()).toBeVisible();
  need((await (await registryBootstrap).response())?.status() === 200, "The automatically remounted assistant bootstrap failed.");
  await expect(dock().getByLabel("Ask Weaver", { exact: true })).toBeEnabled();
  await expect(dock()).toHaveAttribute("aria-busy", "false");
  expect(await page.evaluate(() => performance.timeOrigin)).toBe(originalTimeOrigin);
  expect(requests.filter(request => new URL(request.url).pathname.endsWith("/messages"))).toHaveLength(1);
  expect(callsFor(selected.commandId)).toHaveLength(1);
  result.evidence.push(retain("capability-registry-remount", { sameDocument: true, automaticDockRemount: true,
    realCapabilityRead: true, originalTimeOrigin, commandId: selected.commandId, providerCalls: callsFor(selected.commandId) }));
  // Native SPA navigation keeps that consumed-intent App state until Studio feature Apply retires the document.
  await page.getByLabel("Search modules", { exact: true }).fill("Features");
  await page.getByRole("navigation", { name: "Settings", exact: true }).getByRole("link", { name: "Features", exact: true }).click();
  await expect(page).toHaveURL(studioRoute("/features"));
  await page.getByLabel("Search modules", { exact: true }).fill("");
  await page.getByRole("tab", { name: "Studio", exact: true }).click();
  const tokenResponse = await page.request.get(backend + "/_elsa/identity/token");
  need(tokenResponse.status() === 200, "Normal cookie-backed Studio management token exchange failed.");
  const token = (await tokenResponse.json()).accessToken;
  need(typeof token === "string" && token, "Normal token exchange returned no access token.");
  const hostRead = async endpoint => {
    const response = await page.request.get(studio + endpoint, { headers: { Authorization: "Bearer " + token } });
    need(response.status() === 200, "Native Studio observation failed HTTP " + response.status());
    return response.json();
  };
  const generationBefore = await hostRead("/__qualification/generation");
  const catalog = await hostRead("/modularity/features");
  need(typeof catalog.revision === "string" && catalog.revision.length > 0, "The owning Studio feature catalog has no configuration revision.");
  const target = catalog.features.find(feature => feature.id === witness.studioFeature);
  need(target?.enabled === true, "The installed resource Studio feature must be enabled before its real remount transition.");
  const timeOrigin = await page.evaluate(() => performance.timeOrigin);
  const moduleEvents = [];
  await page.exposeFunction("observeQualificationModulesChanged", event => moduleEvents.push(event));
  await page.evaluate(() => window.addEventListener("elsa-studio:modules-changed", event =>
    void window.observeQualificationModulesChanged({ type: event.type, timeStamp: event.timeStamp })));
  await page.getByLabel("Filter features", { exact: true }).fill(target.id);
  await nativeEnter("remount-disable-feature", page.getByRole("switch", { name: "Disable " + (target.displayName || target.id), exact: true }), "Disable resource Studio feature");
  await nativeEnter("remount-review", page.getByRole("button", { name: "Review changes", exact: true }), "Review changes");
  const dialog = page.locator('[role="dialog"][aria-label="Feature impact review"]');
  await expect(dialog).toBeVisible();
  const applied = page.waitForResponse(response => new URL(response.url()).origin === studio
    && new URL(response.url()).pathname === "/modularity/features/apply" && response.request().method() === "POST");
  await nativeEnter("remount-apply", dialog.getByRole("button", { name: "Apply", exact: true }), "Apply reviewed Studio feature change");
  const appliedResponse = await applied;
  need(appliedResponse.status() === 200, "The native Studio feature apply was refused.");
  await expect.poll(() => moduleEvents.length).toBe(1);
  // Workspace restoration can leave Home before a URL waiter observes it.
  // The document epoch is the retirement boundary; route/backend checks follow below.
  await page.waitForFunction(previous => performance.timeOrigin !== previous, timeOrigin);
  expect(await page.evaluate(() => performance.timeOrigin)).not.toBe(timeOrigin);
  // Apply retires its document, so Chromium may release the old response body.
  // Read the owning catalog after navigation; the serving generation is checked below.
  const catalogAfter = await hostRead("/modularity/features");
  need(typeof catalogAfter.revision === "string" && catalogAfter.revision.length > 0 && catalogAfter.revision !== catalog.revision,
    "Native feature apply did not change the owning configuration revision.");
  expect(catalogAfter.features.find(feature => feature.id === target.id)?.enabled).toBe(false);
  result.evidence.push(retain("native-feature-apply-readback", { url: diagnosticUrl(appliedResponse.url()),
    status: appliedResponse.status(), featureId: target.id, enabledBefore: target.enabled, enabledAfter: false,
    catalogRevisionBefore: catalog.revision, catalogRevisionAfter: catalogAfter.revision,
    configurationReadbackVerified: true }));
  await expect(dock()).toHaveCount(0);
  // Connection startup renders a temporary searchable shell. Its query is discarded
  // when the connected AppContent mounts; the launcher exists only in that ready shell.
  await expect(page.getByRole("button", { name: "Weaver", exact: true })).toBeEnabled();
  await page.getByLabel("Search modules", { exact: true }).fill("Features");
  await page.getByRole("navigation", { name: "Settings", exact: true }).getByRole("link", { name: "Features", exact: true }).click();
  await expect(page).toHaveURL(studioRoute("/features"));
  await page.getByLabel("Search modules", { exact: true }).fill("");
  const bootstrap = page.waitForRequest(request => new URL(request.url()).origin === backend
    && new URL(request.url()).pathname === "/_elsa/weaver/bootstrap");
  await nativeEnter("remount-reopen-weaver", page.getByRole("button", { name: "Weaver", exact: true }), "Reopen Weaver in the new document");
  const bootstrapResponse = await (await bootstrap).response();
  need(bootstrapResponse?.status() === 200, "The remounted assistant bootstrap failed.");
  const bootstrapValue = await bootstrapResponse.json();
  expect(bootstrapValue).toMatchObject({ error: null, data: { enabled: true, providerStatus: "available", provider: { isAvailable: true } } });
  result.evidence.push(retain("remount-bootstrap", { url: diagnosticUrl(bootstrapResponse.url()), status: bootstrapResponse.status(),
    enabled: bootstrapValue.data.enabled, providerStatus: bootstrapValue.data.providerStatus,
    providerId: bootstrapValue.data.provider.providerId, isAvailable: bootstrapValue.data.provider.isAvailable }));
  // Features contributes no Weaver capability. Verify the new document policy before moving
  // its explicitly reopened dock to a route where any replay could run.
  await expect(page).toHaveURL(studioRoute("/features"));
  await expect(dock().getByTestId("weaver-provider-status")).toContainText("No Weaver capabilities are enabled for this screen.");
  await expect(dock().getByLabel("Ask Weaver", { exact: true })).toBeDisabled();
  await expect(dock()).toHaveAttribute("aria-busy", "false");
  const featuresState = { url: diagnosticUrl(page.url()), providerStatus: await dock().getByTestId("weaver-provider-status").innerText(),
    composerDisabled: await dock().getByLabel("Ask Weaver", { exact: true }).isDisabled(), busy: await dock().getAttribute("aria-busy") };
  result.evidence.push(retain("remount-features-state", featuresState));
  await page.getByRole("navigation", { name: "Main", exact: true }).getByRole("link", { name: "Workflows", exact: true }).click();
  await expect(page).toHaveURL(studioRoute("/workflows/definitions"));
  await expect(dock().getByLabel("Ask Weaver", { exact: true })).toBeEnabled();
  await expect(dock()).toHaveAttribute("aria-busy", "false");
  const workflowsState = { url: diagnosticUrl(page.url()), providerStatus: await dock().getByTestId("weaver-provider-status").innerText(),
    composerEnabled: await dock().getByLabel("Ask Weaver", { exact: true }).isEnabled(), busy: await dock().getAttribute("aria-busy") };
  result.evidence.push(retain("remount-workflows-ready", workflowsState));
  const windowStartedAt = Date.now();
  await page.waitForTimeout(1000);
  const generationAfter = await hostRead("/__qualification/generation");
  need(typeof generationBefore.generation === "string" && generationBefore.generation.trim().length > 0
    && typeof generationAfter.generation === "string" && generationAfter.generation.trim().length > 0
    && generationAfter.generation !== generationBefore.generation,
    "The actual Studio serving generation did not change.");
  expect(await page.evaluate(() => performance.timeOrigin)).not.toBe(timeOrigin);
  expect(requests.filter(request => new URL(request.url).pathname.endsWith("/messages"))).toHaveLength(1);
  expect(callsFor(selected.commandId)).toHaveLength(1);
  const sessionAfter = await owner("session-after-remount", sessionPath);
  const userMessages = session => session.value.data.messages.filter(message => String(message.role).toLowerCase() === "user")
    .map(message => ({ id: message.id, requestId: message.requestId, source: message.source, content: message.content }));
  expect(userMessages(sessionAfter)).toEqual(userMessages(sessionBefore));
  const after = await owner("owner-after", witness.ownerEndpoint);
  expect(after.value).toEqual(before.value);
  result.ownerAfter = after.receipt;
  result.evidence.push(retain("native-intent-remount", { nativeOperation: "Studio feature apply", featureId: target.id,
    enabledBefore: target.enabled, enabledAfter: false, catalogRevisionBefore: catalog.revision,
    catalogRevisionAfter: catalogAfter.revision, applyHttpStatus: appliedResponse.status(),
    configurationReadbackVerified: true, reloadObserved: generationAfter.generation !== generationBefore.generation,
    generationBefore, generationAfter, moduleEvents, documentReset: true, timeOriginBefore: timeOrigin, timeOriginAfter: await page.evaluate(() => performance.timeOrigin), featuresState, workflowsState, originalRequestId: original.requestId, originalSource: original.source,
    commandId: selected.commandId, providerCalls: callsFor(selected.commandId), observationWindowMilliseconds: Date.now() - windowStartedAt }));
  result.observations = { nativeReconciliation: true, capabilityRegistryRemount: true, moduleDocumentReset: true, noResubmission: true };
  await screenshot("native-remount-without-replay");
}

// The reads the read-only journey depends on: owner-link discovery, the definition and its draft validations, the
// activity catalog and the expression descriptors the editor loads. Any other refused read (for example runtime
// executables or publishing profiles the reader is not granted) is retained as evidence and does not fail the journey.
function requireJourneyReads() {
  const essential = refusedReads.filter(item => item.path === "/capabilities" || item.path === config.workflow.definitionPath
    || item.path.startsWith("/design/activities/") || item.path.startsWith("/design/workflows/") || item.path.includes("expression"));
  need(essential.length === 0, "The read-only actor was refused backend reads it needs: " + essential.map(item => item.status + " " + item.path).join(", "));
}
// Spec 1193 T012: a read-only actor (workflows/design:read and activities/design:read, never manage) explains the
// workflow through the owning catalog and permissions, and is never offered the layout change: no proposal, no node
// move, no owning write. The protocol commands are retained per command; the driver checks the adapter trace.
async function workflowReadOnly() {
  const { definitionId, activityId, definitionPath } = config.workflow ?? {};
  need(definitionId && activityId && definitionPath, "The read-only journey needs exact owning IDs and the discovered readback path.");
  owningDraftPath = config.workflow.draftPath;
  await page.goto(studio + "/workflows/definitions?definition=" + encodeURIComponent(definitionId));
  const activity = page.locator(".react-flow__node").locator("xpath=self::*[@data-id=" + xpathString(activityId) + "]");
  await expect(activity).toBeVisible();
  await expect(page.locator(".wf-editor-save-status")).not.toHaveAttribute("data-pending", "true");
  const before = await owner("owner-before", definitionPath);
  result.ownerBefore = before.receipt;
  const position = await activity.getAttribute("style");
  requireJourneyReads();
  await page.getByRole("button", { name: /^Weaver/ }).click();
  const ask = async (toolName, args, name) => {
    command(toolName, args);
    await expect(dock().getByLabel("Ask Weaver", { exact: true })).toBeEnabled();
    await dock().getByLabel("Ask Weaver", { exact: true }).fill(JSON.stringify({ toolName, arguments: args }));
    const message = responseFor("/messages");
    await dock().getByRole("button", { name: "Send message", exact: true }).click();
    return accepted(message, name + "-message");
  };
  const explained = await ask("workflow_read_graph", {}, "explain");
  // The accepted message's admitted context, as Studio's agent client reads it: data.message.context.
  const admitted = explained.value.data?.message?.context;
  need(Array.isArray(admitted), "The accepted message carries no admitted context.");
  const permissions = admitted.find(item => item.contentType === "workflow.permissions");
  const catalog = admitted.find(item => item.contentType === "workflow.catalog");
  expect(permissions?.content).toMatchObject({ workflowDefinitionId: definitionId, read: "allowed", manage: "denied",
    tools: ["workflow_read_graph"], capabilities: ["workflow.explain"] });
  expect(catalog?.content).toMatchObject({ status: "available" });
  need(Array.isArray(catalog.content.entries) && catalog.content.entries.length > 0, "The reader's catalog has no entries.");
  await expect(dock().locator(".agent-context-chip", { hasText: "Workflow permissions" })).toBeVisible();
  const answer = dock().locator('.weaver-message[data-role="assistant"]').last();
  await expect(answer).toHaveAttribute("data-status", "completed");
  await expect(answer).toContainText("Tool outcome received");
  result.evidence.push(retain("explain-outcome", { status: await answer.getAttribute("data-status"), text: await answer.innerText() }));
  await screenshot("read-only-explained");

  // The layout tool is not offered to this actor, so the actual adapter cannot call it: the turn ends without a proposal.
  const refused = await ask("workflow_set_designer_position", { activityId, x: 480, y: 320 }, "layout");
  const ended = page.locator('.weaver-alert, .weaver-message[data-role="assistant"][data-status="failed"]').first();
  await expect(ended).toBeVisible();
  result.evidence.push(retain("layout-outcome", { text: await ended.innerText(), acceptedMessageId: refused.value.data?.message?.id ?? null }));
  await expect(dock().locator(".agent-proposal")).toHaveCount(0);
  await expect(activity).toHaveAttribute("style", position);
  const after = await owner("owner-after", definitionPath);
  result.ownerAfter = after.receipt;
  expect(after.value.draft).toEqual(before.value.draft);
  expect(owningWrites).toEqual([]);
  need(!requests.some(item => /\/(approve|execute|acknowledgements)$/.test(new URL(item.url).pathname)), "A read-only actor reached a review or effect boundary.");
  // Every refused read is retained; only a read this journey depends on fails it. Studio may degrade an optional one.
  result.evidence.push(retain("refused-reads", refusedReads));
  requireJourneyReads();
  await screenshot("read-only-layout-not-offered");
  result.observations = { permissionsDisclosed: { manage: "denied", tools: permissions.content.tools }, catalogAvailable: true,
    explainCompleted: true, layoutOffered: false, proposalCount: 0, nodeMoved: false, draftUnchanged: true, owningWrites: 0 };
}

// Spec 1193 T022: one reviewed Operation Batch in the actual packaged editor. Four refused controls first, each with zero
// working-state change (an edit after the proposal, a selection change, a remount); then the batch itself, seven
// operations of six kinds with three in a nested flowchart and a hand edit in its base revision, observed through the designer's own
// export, validation, autosave, Undo and Redo; and last an approval from a second tab. Weaver never saves or publishes.
const BATCH_TOOL = "workflow_apply_operation_batch";
const READ_TOOL = "workflow_read_graph";
async function workflowBatch() {
  const wf = config.workflow ?? {};
  const { definitionId, definitionPath, nodes, batch, followUp } = wf;
  need(definitionId && definitionPath && wf.draftPath && nodes?.a && nodes?.b && nodes?.nested && nodes?.inner && batch?.operations && followUp?.operations,
    "The batch journey needs exact owning IDs, node IDs and both prepared batches.");
  owningDraftPath = wf.draftPath;
  const editorUrl = studio + "/workflows/definitions?definition=" + encodeURIComponent(definitionId);
  const node = (id, target = page) => target.locator(".react-flow__node").locator("xpath=self::*[@data-id=" + xpathString(id) + "]");
  const commands = {};
  await page.goto(editorUrl);
  for (const id of [nodes.a, nodes.b, nodes.nested]) await expect(node(id)).toBeVisible();
  await expect(page.locator(".wf-editor-save-status")).not.toHaveAttribute("data-pending", "true");
  const before = await owner("owner-before", definitionPath);
  result.ownerBefore = before.receipt;
  await nativeValidation("validation-before");
  const initial = await workingDraft("working-initial");
  await page.getByRole("button", { name: /^Weaver/ }).click();
  const timeOrigin = await page.evaluate(() => performance.timeOrigin);

  const select = async (id, name) => {
    // The node header, clear of any slot-entry control in a container's body.
    await node(id).click({ position: { x: 12, y: 12 } });
    await expect(node(id)).toHaveClass(/\bselected\b/);
    result.evidence.push(retain(name + "-interaction", { method: "pointer-click", nodeId: id }));
  };
  const drag = async (id, dx, dy, name) => {
    const box = await node(id).boundingBox();
    need(box, "The node to move by hand is not on the canvas: " + id);
    const x = box.x + 12, y = box.y + 12;
    await page.mouse.move(x, y); await page.mouse.down();
    await page.mouse.move(x + dx, y + dy, { steps: 12 }); await page.mouse.up();
    result.evidence.push(retain(name + "-interaction", { method: "pointer-drag", nodeId: id, dx, dy }));
  };
  const settled = () => expect(dock()).toHaveAttribute("aria-busy", "false");
  // The tool's arguments are exactly the operations: the config's expectations (added id, version, literal, ...) stay
  // here, since the strict batch schema refuses any other field.
  const request = async (prepared, name) => {
    await settled();
    const args = { operations: prepared.operations };
    const selected = command(BATCH_TOOL, args);
    commands[name] = selected.commandId;
    const cards = dock().locator(".agent-proposal"), before = await cards.count();
    await expect(dock().getByLabel("Ask Weaver", { exact: true })).toBeEnabled();
    await dock().getByLabel("Ask Weaver", { exact: true }).fill(JSON.stringify({ toolName: BATCH_TOOL, arguments: args }));
    const message = responseFor("/messages");
    await dock().getByRole("button", { name: "Send message", exact: true }).click();
    const sent = await accepted(message, name + "-message");
    // Wait on the turn itself, not a fixed delay: it ends with a new card awaiting review or settles without one, and
    // then the dock's own text says why (a refusal at preparation, a provider error).
    await expect.poll(async () => await cards.count() > before || await dock().getAttribute("aria-busy") === "false", { timeout: 120000 }).toBe(true);
    if (await cards.count() <= before) {
      const text = (await dock().innerText()).slice(-800);
      result.evidence.push(retain(name + "-no-proposal", { text }));
      need(false, name + ": the batch turn ended without a proposal: " + text);
    }
    const proposal = cards.last();
    await expect(proposal).toHaveAttribute("data-status", "awaiting-approval");
    await expect(proposal.getByRole("button", { name: "Approve", exact: true })).toBeEnabled();
    result.evidence.push(retain(name + "-review", { text: await proposal.innerText(), status: await proposal.getAttribute("data-status") }));
    return { proposal, sent, commandId: selected.commandId };
  };
  const apply = async (proposal, name, target = page) => {
    const execute = responseFor("/execute"), acknowledged = responseFor("/acknowledgements");
    await proposal.getByRole("button", { name: "Apply", exact: true }).click();
    const delivery = await accepted(execute, name + "-execute");
    need(delivery.response.status() === 202, name + ": the change was not delivered to a browser for application.");
    const ack = await accepted(acknowledged, name + "-ack");
    return { ack, sent: ack.response.request().postDataJSON() };
  };
  const refused = async (review, name, reason) => {
    await approve(review.proposal, name);
    const { ack, sent } = await apply(review.proposal, name);
    expect(sent).toMatchObject({ outcome: "refused" });
    need(typeof sent.summary === "string" && reason.test(sent.summary), name + ": refused for another reason: " + sent.summary);
    need(sent.result == null && sent.after == null, name + ": a refused change reported an applied observation.");
    expect(ack.value.data).toMatchObject({ executed: false, status: "refused" });
    await expect(review.proposal).toHaveAttribute("data-status", "failed");
    const working = await workingDraft(name + "-working");
    need(!JSON.stringify(working.state).includes(batch.addedNodeId), name + ": refused batch content reached the working draft.");
    result.evidence.push(retain(name + "-outcome", { summary: sent.summary, status: await review.proposal.getAttribute("data-status") }));
    await settled();
    return { summary: sent.summary, working };
  };
  // A hand edit's ordinary autosave: the owning write happened and the editor reports no save pending.
  const settledAutosave = async (writesBefore, what) => {
    await expect.poll(() => owningWrites.length, { timeout: 15000, message: "The " + what + " was never autosaved." }).toBeGreaterThan(writesBefore);
    await expect(page.locator(".wf-editor-save-status")).not.toHaveAttribute("data-pending", "true");
  };
  const sameDraft = async (name, expected) => expect((await owner(name, definitionPath)).value.draft).toEqual(expected);

  // Control 1: a hand edit after the proposal changes the working-state revision; the selection stays as reviewed.
  await select(nodes.nested, "stale-select");
  const stale = await request(batch, "staleEdit");
  const writesBeforeStaleEdit = owningWrites.length;
  await drag(nodes.nested, 60, 40, "stale-hand-edit");
  await expect(node(nodes.nested)).toHaveClass(/\bselected\b/);
  const staleOutcome = await refused(stale, "staleEdit", /revision changed after review, so this change is stale/);
  need(JSON.stringify(staleOutcome.working.layout) !== JSON.stringify(initial.layout), "The hand edit after the proposal was not in the working state.");
  // The hand edit is ordinary unsaved work until its debounced autosave lands; Studio rightly refuses to navigate away
  // from unsaved work, so the remount below needs it settled. Wait on the editor's own write and save state.
  await settledAutosave(writesBeforeStaleEdit, "stale hand edit");
  expect((await owner("owner-after-stale-autosave", definitionPath)).value.draft.layout).toEqual(staleOutcome.working.layout);
  // Control 2: a selection change rotates the editor binding before dispatch.
  const selection = await request(batch, "selectionChange");
  await select(nodes.a, "selection-change");
  // The binding rotates, as on a remount: the editor reports its originating target gone before anything is dispatched.
  await refused(selection, "selectionChange", /selection changed\. Nothing was applied/);
  // Control 3: the editor remounts inside the same document; the dock and its proposal survive, the binding does not.
  const remount = await request(batch, "remount");
  // Nothing unsaved may be pending, or the native navigation guard keeps the editor (correctly) and nothing remounts.
  await expect(page.locator(".wf-editor-save-status")).not.toHaveAttribute("data-pending", "true");
  const writesBeforeRemount = owningWrites.length;
  await page.getByRole("navigation", { name: "Main", exact: true }).getByRole("link", { name: "Home", exact: true }).click();
  await expect(node(nodes.a), "Studio kept the editor open: its navigation guard refused to leave (unsaved or pending work), "
    + "save status '" + (await page.locator(".wf-editor-save-status").innerText().catch(() => "")) + "'").toHaveCount(0);
  need(owningWrites.length === writesBeforeRemount, "Leaving the editor for the remount wrote the owning draft.");
  await page.goBack();
  await expect(node(nodes.a)).toBeVisible();
  need(await page.evaluate(() => performance.timeOrigin) === timeOrigin, "The remount control reloaded the document instead of remounting the editor.");
  await refused(remount, "remount", /remounted.*Nothing was applied/);

  // The batch. A hand edit just before the request is part of its base revision; its ordinary autosave settles first.
  const writesBeforeHandEdit = owningWrites.length;
  await drag(nodes.nested, 40, 30, "base-hand-edit");
  await settledAutosave(writesBeforeHandEdit, "base hand edit");
  const base = await workingDraft("working-before-batch");
  const ownerBase = (await owner("owner-before-batch", definitionPath)).value.draft;
  const nestedRow = rows => rows.find(row => row.nodeId === nodes.nested);
  expect([nestedRow(ownerBase.layout)?.x, nestedRow(ownerBase.layout)?.y]).toEqual([nestedRow(base.layout)?.x, nestedRow(base.layout)?.y]);
  const writesBeforeBatch = owningWrites.length;
  const positive = await request(batch, "applied");
  const reviewText = await positive.proposal.innerText();
  need(/Destructive/.test(reviewText) && /Review required/.test(reviewText), "The review does not label each operation's risk.");
  // The admitted context, as the accepted message records it; targets compare by identity, never by display name or route.
  const admittedDraft = (positive.sent.value.data?.message?.context ?? []).find(item => item.contentType === "workflow.definition");
  need(admittedDraft && admittedDraft.target?.moduleId === "Elsa.Studio.Workflows" && admittedDraft.target?.resourceType === "workflow-definition"
    && admittedDraft.target?.resourceId === definitionId && /^sha256:[0-9a-f]{64}$/.test(admittedDraft.target?.revision ?? "")
    && Array.isArray(admittedDraft.content?.structure?.containers), "The admitted working-state context lost its identity, SHA-256 revision or structure.");
  await approve(positive.proposal, "applied");
  const { ack, sent } = await apply(positive.proposal, "applied");
  expect(sent).toMatchObject({ outcome: "applied", after: { resourceId: definitionId }, result: { typeId: "workflows.batch-result", schemaVersion: "1",
    data: { observed: true, historyEntry: true, appliedCount: batch.operations.length, addedNodeIds: [batch.addedNodeId], baseRevision: admittedDraft.target.revision } } });
  expect(ack.value.data?.toolResult?.receipt?.scope).toBe("clientWorkingState");
  await expect(positive.proposal).toHaveAttribute("data-status", "executed");
  await settled();
  const applied = await workingDraft("working-applied");
  const root = applied.state.rootActivity, children = root.structure.payload.activities;
  const nested = children.find(child => child.nodeId === nodes.nested);
  const greet = nested?.structure?.payload?.activities?.find(child => child.nodeId === batch.addedNodeId);
  need(greet && greet.activityVersionId === batch.writeLineVersionId, "The added activity is not in the nested flowchart.");
  const innerPairs = (nested.structure.payload.connections ?? []).map(edge => edge.source.nodeId + ">" + edge.target.nodeId);
  expect(innerPairs).toEqual([nodes.inner + ">" + batch.addedNodeId]);
  need(JSON.stringify(greet).includes(batch.literal), "The added activity's declared input was not set.");
  need((applied.activityPresentation ?? []).some(row => row.nodeId === nodes.a && row.displayName === batch.displayName), "The display name update is missing.");
  const pairs = (root.structure.payload.connections ?? []).map(edge => edge.source.nodeId + ">" + edge.target.nodeId).sort();
  expect(pairs).toEqual([nodes.a + ">" + nodes.b, nodes.b + ">" + nodes.nested].sort());
  const bRow = applied.layout.find(row => row.nodeId === nodes.b);
  expect([bRow?.x, bRow?.y]).toEqual([batch.position.x, batch.position.y]);
  await nativeValidation("validation-after-batch");
  // Dirty: the working draft differs from the owner's, which still holds the base.
  need(JSON.stringify(applied.state) !== JSON.stringify(ownerBase.state), "The applied batch did not leave the working draft dirty.");
  await page.waitForTimeout(1600);
  await sameDraft("owner-after-autosave-window", ownerBase);
  expect(owningWrites.length).toBe(writesBeforeBatch);
  // One Undo restores exactly the base, hand edit included; the review card's Undo is designer-history Undo.
  await positive.proposal.getByRole("button", { name: "Undo whole change", exact: true }).click();
  expect(await workingDraft("working-after-undo")).toEqual(base);
  await nativeValidation("validation-after-undo");
  // Redo restores the whole batch and holds autosave again.
  await nativeEnter("designer-redo", page.getByRole("button", { name: "Redo", exact: true }), "Redo");
  expect(await workingDraft("working-after-redo")).toEqual(applied);
  await page.waitForTimeout(1600);
  await sameDraft("owner-after-redo-window", ownerBase);
  expect(owningWrites.length).toBe(writesBeforeBatch);

  // Control 4: approving from a second tab of the same Studio. Its dock reopens the session, but the invocation belongs
  // to the first tab's binding, so it is refused before anything is applied; the first tab's working draft is unchanged.
  await request(followUp, "secondTab");
  const second = await context.newPage();
  try {
    await second.goto(editorUrl);
    await expect(node(nodes.a, second)).toBeVisible();
    await second.getByRole("button", { name: /^Weaver/ }).click();
    const otherDock = second.getByRole("region", { name: "Weaver assistant", exact: true });
    const card = otherDock.locator(".agent-proposal").last();
    await expect(card).toHaveAttribute("data-status", "awaiting-approval");
    const approved = second.waitForResponse(response => new URL(response.url()).origin === backend && response.url().endsWith("/approve"));
    await card.getByRole("button", { name: "Approve", exact: true }).click();
    need((await approved).ok(), "The second tab could not approve the proposal.");
    const execute = second.waitForResponse(response => new URL(response.url()).origin === backend && response.url().endsWith("/execute"));
    const acknowledged = second.waitForResponse(response => new URL(response.url()).origin === backend && response.url().endsWith("/acknowledgements"));
    await card.getByRole("button", { name: "Apply", exact: true }).click();
    need((await execute).status() === 202, "The second tab's approval was not delivered to a browser.");
    const otherAck = (await acknowledged).request().postDataJSON();
    result.evidence.push(retain("secondTab-ack", otherAck));
    expect(otherAck).toMatchObject({ outcome: "refused", summary: "The originating Studio context is unavailable." });
    need(otherAck.result == null && otherAck.after == null, "The second tab reported an applied observation.");
    await expect(card).toHaveAttribute("data-status", "failed");
  } finally { await second.close(); }
  expect(await workingDraft("working-after-second-tab")).toEqual(applied);
  await sameDraft("owner-after", ownerBase);
  result.ownerAfter = (await owner("owner-after-final", definitionPath)).receipt;
  expect(owningWrites.length).toBe(writesBeforeBatch);
  need(publishAttempts.length === 0, "The batch journey attempted to publish.");
  await screenshot("workflow-batch-applied");
  result.commands = commands;
  result.observations = { staleEdit: "refused", selectionChange: "refused", remount: "refused", secondTab: "refused", applied: true,
    operationCount: batch.operations.length, nestedScope: true, handEditInBase: true, validated: true, dirty: true, autosaveSuspended: true,
    undoRestored: true, redoRestored: true, noSaveOrPublish: true };
}

// Spec 1193 T028: explaining the active draft in the actual packaged editor. With autosave off for this editor (component
// state, not a stored preference), the reader selects the activity whose input the designer's own validation flags and
// asks Weaver to read the draft: the admitted context names that input's expression type, the server's unavailable
// interpretation and the designer's diagnostic for it. A hand edit then changes the unsaved working state, and the next
// read is attributed to the new revision with the moved position, equal to the editor's own export. Nothing is saved.
async function workflowExplain() {
  const wf = config.workflow ?? {};
  const { definitionId, definitionPath, nodes, explain } = wf;
  need(definitionId && definitionPath && wf.draftPath && nodes?.a && nodes?.b && nodes?.nested && explain?.property && explain?.moveNode,
    "The explanation journey needs exact owning IDs, node IDs and the expected selected input.");
  owningDraftPath = wf.draftPath;
  const node = id => page.locator(".react-flow__node").locator("xpath=self::*[@data-id=" + xpathString(id) + "]");
  const commands = {};
  await page.goto(studio + "/workflows/definitions?definition=" + encodeURIComponent(definitionId));
  for (const id of [nodes.a, nodes.b, nodes.nested]) await expect(node(id)).toBeVisible();
  await expect(page.locator(".wf-editor-save-status")).not.toHaveAttribute("data-pending", "true");
  const before = await owner("owner-before", definitionPath);
  result.ownerBefore = before.receipt;
  // Wait on the editor's own state: the designer's structural checks render at once, the server's draft validations
  // arrive later, and the snapshot discloses whatever the panel shows when the question is sent (observed natively on
  // 04cd44f7c). The seeded unresolved variable is named in the panel only once the server's validations are in.
  const panel = page.locator(".wf-validation");
  need(typeof explain.diagnosticMarker === "string" && explain.diagnosticMarker, "The explanation journey needs the seeded diagnostic's marker.");
  await expect(panel).toContainText(explain.diagnosticMarker, { timeout: 30000 });
  result.evidence.push(retain("validation-panel", { text: await panel.innerText() }));
  // Autosave off, through the editor's own menu; the hand edit below then stays in the unsaved working state.
  await nativeEnter("autosave-menu", page.getByRole("button", { name: "More workflow actions", exact: true }), "More workflow actions");
  const menu = page.getByRole("menu", { name: "More workflow actions", exact: true });
  const autosave = menu.getByRole("menuitemcheckbox", { name: /^Autosave\b/ });
  await expect(autosave).toBeFocused();
  await expect(autosave).toHaveAttribute("aria-checked", "true");
  await nativeEnter("autosave-off", autosave, "Autosave");
  await expect(autosave).toHaveAttribute("aria-checked", "false");
  await page.keyboard.press("Escape");
  await expect(menu).toBeHidden();
  await page.getByRole("button", { name: /^Weaver/ }).click();
  const ask = async name => {
    await expect(dock()).toHaveAttribute("aria-busy", "false");
    const selected = command(READ_TOOL, {});
    commands[name] = selected.commandId;
    await expect(dock().getByLabel("Ask Weaver", { exact: true })).toBeEnabled();
    const answers = dock().locator('.weaver-message[data-role="assistant"]'), earlier = await answers.count();
    await dock().getByLabel("Ask Weaver", { exact: true }).fill(JSON.stringify({ toolName: READ_TOOL, arguments: {} }));
    const message = responseFor("/messages");
    await dock().getByRole("button", { name: "Send message", exact: true }).click();
    const sent = await accepted(message, name + "-message");
    // The accepted message's admitted context, as Studio's agent client reads it: data.message.context.
    const admitted = sent.value.data?.message?.context;
    need(Array.isArray(admitted), name + ": the accepted message carries no admitted context.");
    const drafts = admitted.filter(item => item.contentType === "workflow.definition");
    need(drafts.length === 1 && drafts[0].target?.resourceId === definitionId, name + ": the message did not admit exactly the open working draft.");
    // This turn's own answer, never an earlier turn's completed one.
    await expect.poll(() => answers.count(), { timeout: 120000 }).toBeGreaterThan(earlier);
    const answer = answers.nth(earlier);
    await expect(answer).toHaveAttribute("data-status", "completed", { timeout: 120000 });
    await expect(answer).toContainText("Tool outcome received");
    result.evidence.push(retain(name + "-outcome", { status: await answer.getAttribute("data-status"), revision: drafts[0].target.revision }));
    return drafts[0];
  };

  await node(nodes.b).click({ position: { x: 12, y: 12 } });
  await expect(node(nodes.b)).toHaveClass(/\bselected\b/);
  result.evidence.push(retain("select-interaction", { method: "pointer-click", nodeId: nodes.b }));
  const first = await ask("before");
  const content = first.content ?? {};
  need(content.selectedNodeId === nodes.b && content.selectedActivity?.nodeId === nodes.b, "The admitted context does not describe the selected activity.");
  const property = (content.selectedActivity.properties ?? []).find(item => item.referenceKey === explain.property);
  need(property?.value?.kind === "expression" && property.value.expressionType === explain.expressionType && property.value.interpretation === explain.interpretation,
    "The selected input's expression was not disclosed by type, content and the unavailable interpretation: " + JSON.stringify(property ?? null));
  const diagnostic = (content.diagnostics ?? []).find(item => item.nodeId === nodes.b && item.property === explain.property && item.type === explain.diagnosticType);
  need(diagnostic, "The designer's diagnostic for the selected input was not disclosed.");
  // The packaged consumer has no design-authored (graph) activity: its CLR activities, though recommendations give them
  // definition identities, are never disclosed as graph activities.
  need(!("graphActivities" in content), "A source-owned activity was disclosed as a graph activity.");

  // A hand edit with autosave off: the working state changes and stays unsaved.
  const writes = owningWrites.length;
  const position = (content.positions ?? []).find(item => item.nodeId === explain.moveNode);
  const box = await node(explain.moveNode).boundingBox();
  need(box && position, "The node to move by hand is not on the canvas or not in the admitted positions.");
  await page.mouse.move(box.x + 12, box.y + 12); await page.mouse.down();
  await page.mouse.move(box.x + 72, box.y + 52, { steps: 12 }); await page.mouse.up();
  result.evidence.push(retain("move-interaction", { method: "pointer-drag", nodeId: explain.moveNode, dx: 60, dy: 40 }));
  const second = await ask("after");
  need(second.target.revision !== first.target.revision, "The read after the hand edit was attributed to the earlier revision.");
  const moved = (second.content?.positions ?? []).find(item => item.nodeId === explain.moveNode);
  need(moved && (moved.x !== position.x || moved.y !== position.y), "The admitted position did not follow the hand edit.");
  const working = await workingDraft("working-after");
  const exported = (working.layout ?? []).find(item => item.nodeId === explain.moveNode);
  need(exported && exported.x === moved.x && exported.y === moved.y, "The admitted position is not the editor's working state.");
  need(owningWrites.length === writes, "The hand edit was saved although autosave was off.");

  // A credential canary typed into the editor's own masked field of a password-hinted input, as an unsaved edit after the
  // export above, so it exists only in Studio's working state. The read discloses the input only as withheld.
  const canary = explain.canary;
  need(typeof canary === "string" && canary.length >= 24 && explain.maskedNode && explain.maskedLabel && explain.maskedProperty,
    "The explanation journey needs the masked input and its canary.");
  // The dock shares the editor's width, so the inspector may be collapsed beside it: close the dock, select the activity
  // and expand the inspector through its own control, type, then reopen the dock (the conversation is kept).
  await dock().getByRole("button", { name: "Close assistant", exact: true }).click();
  await expect(dock()).toBeHidden();
  await node(explain.maskedNode).click({ position: { x: 12, y: 12 } });
  await expect(node(explain.maskedNode)).toHaveClass(/\bselected\b/);
  const expand = page.getByRole("button", { name: "Expand inspector panel", exact: true });
  if (await expand.count()) await nativeEnter("inspector-expand", expand, "Expand inspector panel");
  const field = page.locator('input[type="password"][aria-label=' + JSON.stringify(explain.maskedLabel) + ']');
  await expect(field).toHaveCount(1);
  await field.fill(canary);
  result.evidence.push(retain("masked-interaction", { method: "keyboard-fill", nodeId: explain.maskedNode, input: explain.maskedProperty, length: canary.length }));
  await page.getByRole("button", { name: /^Weaver/ }).click();
  await expect(dock()).toBeVisible();
  const masked = await ask("masked");
  const maskedContent = masked.content ?? {};
  need(maskedContent.selectedNodeId === explain.maskedNode && maskedContent.selectedActivity?.nodeId === explain.maskedNode,
    "The admitted context does not describe the masked activity.");
  const withheld = (maskedContent.selectedActivity.properties ?? []).find(item => item.referenceKey === explain.maskedProperty);
  need(withheld?.isSensitive === true && withheld.value?.kind === "withheld" && Object.keys(withheld.value).sort().join() === "expressionType,kind",
    "The typed masked value was not disclosed only as withheld: " + JSON.stringify(withheld?.value?.kind ?? null));
  // In-page search, raw and unredacted: no request the page sent to the agent API (any method, URL and query included), no
  // response body it read, and no byte of any agent stream it received carries the canary.
  await Promise.all(agentResponseReads);
  // An empty body proves nothing: every captured stream must have been captured without error, and at least one must be
  // an actual agent event stream whose bytes were read, before its absence of the canary counts.
  const captured = await streamCapture();
  need(captured.every(item => item.captureError == null && item.captureValidationError == null)
    && captured.some(item => item.captureMethod !== "not-sse" && typeof item.rawBody === "string" && item.rawBody.startsWith("data: ")),
    "The in-page search has no complete, error-free agent stream capture to search.");
  need(agentTraffic.some(item => item.method === "POST" && item.url.endsWith("/messages")), "The in-page search observed no agent message to search.");
  need(!JSON.stringify(agentTraffic).includes(canary) && !captured.some(item => item.rawBody.includes(canary)) && !JSON.stringify(masked).includes(canary),
    "The typed canary left the browser's working state.");
  need(owningWrites.length === writes, "The masked edit was saved although autosave was off.");
  const after = await owner("owner-after", definitionPath);
  result.ownerAfter = after.receipt;
  // A digest, never a diff: a failed comparison must not print the owning draft (or a leaked value in it) to stdout.
  need(digest(JSON.stringify(after.value.draft)) === digest(JSON.stringify(before.value.draft)), "The explanation journey changed the owning draft.");
  need(owningWrites.length === 0, "The explanation journey wrote the owning draft.");
  await screenshot("explained");
  result.commands = commands;
  result.observations = { explainCompleted: true, selectedExpressionType: property.value.expressionType, interpretation: property.value.interpretation,
    diagnosticOnSelectedInput: true, autosaveOff: true, revisionChanged: true, positionFollowed: true, maskedTyped: true, maskedWithheld: true,
    canaryAbsentInPage: true, draftUnchanged: true, owningWrites: owningWrites.length };
}

// Spec 1193 T036: a reviewed Weaver tool on a library page, in the actual packaged Studio. The command is selected, the dock
// sends it, and the turn ends with a card awaiting review (or the dock says why it did not).
async function reviewedTool(toolName, args, name, commands) {
  await expect(dock()).toHaveAttribute("aria-busy", "false");
  const selected = command(toolName, args);
  commands[name] = selected.commandId;
  const cards = dock().locator(".agent-proposal"), before = await cards.count();
  await expect(dock().getByLabel("Ask Weaver", { exact: true })).toBeEnabled();
  await dock().getByLabel("Ask Weaver", { exact: true }).fill(JSON.stringify({ toolName, arguments: args }));
  const message = responseFor("/messages");
  await nativeEnter(name + "-send", dock().getByRole("button", { name: "Send message", exact: true }), "Send message");
  const sent = await accepted(message, name + "-message");
  await expect.poll(async () => await cards.count() > before || await dock().getAttribute("aria-busy") === "false", { timeout: 120000 }).toBe(true);
  if (await cards.count() <= before) {
    const text = (await dock().innerText()).slice(-800);
    result.evidence.push(retain(name + "-no-proposal", { text }));
    need(false, name + ": the turn ended without a proposal: " + text);
  }
  const proposal = cards.last();
  await expect(proposal).toHaveAttribute("data-status", "awaiting-approval");
  result.evidence.push(retain(name + "-review", { text: await proposal.innerText(), status: await proposal.getAttribute("data-status") }));
  return { proposal, sent };
}
// In these journeys the nonmodal dock overlaps page controls and an open page dialog overlaps the dock, so every control,
// on the page and in the dock, is activated through the keyboard: the same native control, never a pointer that another
// surface intercepts (as the witness on c58c9f149 observed for the list's Create workflow button).
async function approveAndApply(proposal, name) {
  const response = responseFor("/approve");
  await nativeEnter(name + "-approve", proposal.getByRole("button", { name: "Approve", exact: true }), "Approve");
  await accepted(response, name + "-approve");
  await expect(proposal).toHaveAttribute("data-status", "approved");
  const execute = responseFor("/execute"), acknowledged = responseFor("/acknowledgements");
  await nativeEnter(name + "-apply", proposal.getByRole("button", { name: "Apply", exact: true }), "Apply");
  const delivery = await accepted(execute, name + "-execute");
  need(delivery.response.status() === 202, name + ": the change was not delivered to the originating page.");
  const ack = await accepted(acknowledged, name + "-ack");
  await expect(proposal).toHaveAttribute("data-status", "executed");
  return { ack, sent: ack.response.request().postDataJSON() };
}

// The library list page: a reviewed narrowing applied and reverted by the list page, and a reviewed prefill of the open create
// dialog, whose Create is never pressed. No owner write follows either change.
async function workflowLibrary() {
  const wf = config.workflow ?? {};
  const { library } = wf;
  need(wf.definitionId && library?.search && library?.pagePath && library?.prefill?.name, "The library journey needs a seeded definition, its search and the page link.");
  const commands = {};
  await page.goto(studio + "/workflows/definitions");
  const searchBox = page.getByPlaceholder("Search definitions", { exact: true });
  await expect(page.getByRole("table", { name: "Workflow definitions", exact: true })).toBeVisible();
  await expect(searchBox).toHaveValue("");
  const before = await owner("owner-before", library.pagePath + "?pageSize=100");
  result.ownerBefore = before.receipt;
  await nativeEnter("open-weaver", page.getByRole("button", { name: /^Weaver/ }), "Weaver");
  const writesBefore = designWrites.length;
  // The list is reviewed only once it is settled: its rows current and its permissions read (aria-busy="false"). A page whose
  // permissions arrive after the review moves its revision and refuses the reviewed change as stale (the 6231b4434 witness).
  await expect(page.getByRole("table", { name: "Workflow definitions", exact: true })).toHaveAttribute("aria-busy", "false", { timeout: 30000 });
  // Narrowing: the list page applies exactly the approved change and the user can revert it.
  const narrow = await reviewedTool("workflow_library_set_list_state", { search: library.search }, "narrow", commands);
  const listContext = (narrow.sent.value.data?.message?.context ?? []).find(item => item.contentType === "workflows.library");
  need(listContext?.target?.resourceType === "workflow-library" && /^sha256:[0-9a-f]{64}$/.test(listContext?.target?.revision ?? ""),
    "The admitted list context lost its identity or SHA-256 revision.");
  const narrowed = await approveAndApply(narrow.proposal, "narrow");
  expect(narrowed.sent).toMatchObject({ outcome: "applied", result: { typeId: "workflows.list-state", schemaVersion: "1",
    data: { observed: true, change: { search: library.search }, baseRevision: listContext.target.revision } } });
  await expect(searchBox).toHaveValue(library.search);
  await nativeEnter("narrow-revert", narrow.proposal.getByRole("button", { name: "Revert", exact: true }), "Revert");
  await expect(searchBox).toHaveValue("");
  result.evidence.push(retain("narrow-reverted", { search: await searchBox.inputValue() }));
  // The create dialog: the prefill fills the open dialog only.
  await nativeEnter("open-create-dialog", page.getByRole("button", { name: "Create workflow", exact: true }).first(), "Create workflow");
  const nameField = page.locator('input[aria-label="Display name"]');
  await expect(nameField).toBeVisible();
  // Likewise the open dialog is reviewed only once it has read the saved templates it offers.
  await expect(page.getByRole("combobox", { name: "Starting point", exact: true })).toHaveAttribute("aria-busy", "false", { timeout: 30000 });
  const prefill = await reviewedTool("workflow_create_dialog_prefill", { name: library.prefill.name }, "prefill", commands);
  const filled = await approveAndApply(prefill.proposal, "prefill");
  expect(filled.sent).toMatchObject({ outcome: "applied", result: { typeId: "workflows.create-dialog-prefill", schemaVersion: "1",
    data: { observed: true, change: { name: library.prefill.name } } } });
  await expect(nameField).toHaveValue(library.prefill.name);
  const after = await owner("owner-after", library.pagePath + "?pageSize=100");
  result.ownerAfter = after.receipt;
  need(after.value.totalCount === before.value.totalCount, "A workflow was created although Create was never pressed.");
  need(designWrites.length === writesBefore, "A page working-state change wrote to the workflow design API.");
  await screenshot("workflow-library-prefilled");
  result.commands = commands;
  result.observations = { listNarrowed: true, listReverted: true, dialogPrefilled: true, createNotPressed: true, noLibraryWrite: true };
}

// The template editor: an approved batch applied to the template's working state as one history step, autosave held, and
// persisted only by the user's own Save with the template's expected revision. No workflow definition is written.
async function workflowTemplates() {
  const wf = config.workflow ?? {};
  const { template, nodes, definitionPath } = wf;
  need(template?.id && template?.revision && template?.path && template?.operations && nodes?.a && definitionPath, "The template journey needs the seeded template and nodes.");
  const commands = {};
  const node = id => page.locator(".react-flow__node").locator("xpath=self::*[@data-id=" + xpathString(id) + "]");
  await page.goto(studio + "/workflows/templates?template=" + encodeURIComponent(template.id));
  await expect(node(nodes.a)).toBeVisible();
  await expect(page.locator(".wf-editor-save-status")).not.toHaveAttribute("data-pending", "true");
  const before = await owner("owner-before", template.path);
  result.ownerBefore = before.receipt;
  need(before.value.revision === template.revision, "The template changed before the journey.");
  const definitionBefore = await owner("definition-before", definitionPath);
  await nativeEnter("open-weaver", page.getByRole("button", { name: /^Weaver/ }), "Weaver");
  const writesBefore = designWrites.length;
  const batch = await reviewedTool("workflow_apply_operation_batch", { operations: template.operations }, "applied", commands);
  const editor = (batch.sent.value.data?.message?.context ?? []).find(item => item.contentType === "workflows.template-editor");
  need(editor?.target?.resourceType === "workflow-template" && editor?.target?.resourceId === template.id && editor?.content?.templateRevision === template.revision
    && !("workflowDefinitionId" in (editor?.content ?? {})), "The admitted template context is not the template with its persisted revision.");
  const applied = await approveAndApply(batch.proposal, "applied");
  expect(applied.sent).toMatchObject({ outcome: "applied", after: { resourceType: "workflow-template", resourceId: template.id },
    result: { typeId: "workflows.batch-result", schemaVersion: "1", data: { observed: true, historyEntry: true, appliedCount: template.operations.length } } });
  await expect(node(nodes.a)).toContainText(template.displayName);
  // Autosave is held while the batch is unsaved: beyond the autosave interval the owner still holds the persisted revision.
  await page.waitForTimeout(1600);
  need((await owner("owner-after-autosave-window", template.path)).value.revision === template.revision, "Autosave persisted the batch.");
  need(designWrites.length === writesBefore, "The applied batch wrote to the design API before the user's Save.");
  // One history step: the card's Undo removes the whole batch, Redo restores it.
  await nativeEnter("batch-undo", batch.proposal.getByRole("button", { name: "Undo whole change", exact: true }), "Undo whole change");
  await expect(node(nodes.a)).not.toContainText(template.displayName);
  await nativeEnter("designer-redo", page.getByRole("button", { name: "Redo", exact: true }), "Redo");
  await expect(node(nodes.a)).toContainText(template.displayName);
  // The user's own Save, with the template's expected revision.
  const saved = page.waitForResponse(response => new URL(response.url()).origin === backend && new URL(response.url()).pathname === template.path && response.request().method() === "PUT");
  // The toolbar's Save (accessible name "Save", title "Save workflow"), the designer's own explicit save.
  await nativeEnter("template-save", page.locator("button.wf-save-button"), "Save workflow");
  const response = await saved;
  need(response.ok(), "The user's template Save failed: " + response.status());
  const body = response.request().postDataJSON();
  result.evidence.push(retain("template-save-request", body));
  need(body?.expectedRevision === template.revision, "The template Save did not carry the template's expected revision.");
  const after = await owner("owner-after", template.path);
  result.ownerAfter = after.receipt;
  need(after.value.revision !== template.revision && JSON.stringify(after.value.content).includes(template.displayName), "The saved template lacks the batch.");
  const definitionAfter = await owner("definition-after", definitionPath);
  expect(definitionAfter.value.draft).toEqual(definitionBefore.value.draft);
  need(designWrites.every(write => write.path === template.path), "A template journey wrote another design resource: " + JSON.stringify(designWrites.map(item => item.path)));
  await screenshot("workflow-templates-saved");
  result.commands = commands;
  result.observations = { applied: true, templateTarget: true, oneHistoryStep: true, autosaveHeld: true, savedWithExpectedRevision: true, definitionUntouched: true };
}
// Spec 1193 T041: a read-only inspection page's own Explain action. The page contributes the exact resource it shows as
// context; the Weaver Workflows prompt names the read tool; the selected command makes the actual adapter call it. The dock
// overlaps page controls, so every control is activated through the keyboard, and each wait is on state, never on time.
async function inspectionExplain(name, explainButton, toolName, args, kind, target, commands) {
  await expect(dock()).toHaveAttribute("aria-busy", "false");
  const selected = command(toolName, args);
  commands[name] = selected.commandId;
  const answers = dock().locator('.weaver-message[data-role="assistant"]'), earlier = await answers.count();
  const message = responseFor("/messages");
  await nativeEnter(name + "-explain", explainButton, "Explain");
  const sent = await accepted(message, name + "-message");
  // The accepted message's admitted context, as Studio's agent client reads it: data.message.context.
  const admitted = (sent.value.data?.message?.context ?? []).filter(item => item.contentType === kind);
  need(admitted.length === 1 && ["authoritativeRead", "AuthoritativeRead"].includes(admitted[0].origin)
    && admitted[0].target?.resourceType === target.resourceType && admitted[0].target?.resourceId === target.resourceId
    && admitted[0].target?.revision === target.revision, name + ": the message did not admit the server's read of exactly the shown resource.");
  await expect.poll(() => answers.count(), { timeout: 120000 }).toBeGreaterThan(earlier);
  const answer = answers.nth(earlier);
  await expect(answer).toHaveAttribute("data-status", "completed", { timeout: 120000 });
  await expect(answer).toContainText("Tool outcome received");
  result.evidence.push(retain(name + "-outcome", { status: await answer.getAttribute("data-status"), target: admitted[0].target }));
  return admitted[0];
}

// The editor's version history: selecting a version discloses it, and its Explain action reads it with a comparison.
async function workflowVersionHistory() {
  const wf = config.workflow ?? {};
  const version = wf.version ?? {};
  need(wf.definitionId && wf.definitionPath && version.id && version.version && version.path && version.compareToVersionId,
    "The version history journey needs the seeded definition and the exact versions.");
  owningDraftPath = wf.draftPath;
  const commands = {};
  await page.goto(studio + "/workflows/definitions?definition=" + encodeURIComponent(wf.definitionId));
  await expect(page.locator(".wf-editor-save-status")).not.toHaveAttribute("data-pending", "true");
  const before = await owner("owner-before", version.path);
  result.ownerBefore = before.receipt;
  const definitionBefore = await owner("definition-before", wf.definitionPath);
  await nativeEnter("open-weaver", page.getByRole("button", { name: /^Weaver/ }), "Weaver");
  await expect(dock()).toBeVisible();
  await nativeEnter("versions-tab", page.getByRole("tab", { name: "Versions", exact: true }), "Versions");
  const select = page.getByRole("button", { name: "Select version " + version.version, exact: true });
  await expect(select).toBeVisible();
  await nativeEnter("select-version", select, "Select version");
  await expect(select).toHaveAttribute("aria-pressed", "true");
  const explain = page.getByRole("button", { name: "Explain version v" + version.version, exact: true });
  await expect(explain).toBeVisible();
  const admitted = await inspectionExplain("explain", explain, "workflow_read_version", { compareToVersionId: version.compareToVersionId }, "workflows.version",
    { resourceType: "workflow-version", resourceId: version.id, revision: version.id }, commands);
  need(admitted.content?.versionId === version.id && admitted.content?.definitionId === wf.definitionId, "The admitted version is not the selected version.");
  const after = await owner("owner-after", version.path);
  result.ownerAfter = after.receipt;
  expect(after.value).toEqual(before.value);
  const definitionAfter = await owner("definition-after", wf.definitionPath);
  expect(definitionAfter.value.draft).toEqual(definitionBefore.value.draft);
  need(inspectionEffects().length === 0 && owningWrites.length === 0, "The version history journey changed an owner: " + JSON.stringify(inspectionEffects()));
  await screenshot("workflow-version-history-explained");
  result.commands = commands;
  result.observations = { versionSelected: true, versionContext: true, explainCompleted: true, readOnly: true };
}

// The Executable Inspector: the shown Executable, bound to its Artifact Hash, is disclosed and its Explain action reads it.
async function workflowExecutableInspector() {
  const executable = (config.workflow ?? {}).executable ?? {};
  need(executable.artifactId && executable.artifactHash && executable.path, "The Executable Inspector journey needs the seeded Executable.");
  const commands = {};
  await page.goto(studio + "/workflows/executables/" + encodeURIComponent(executable.artifactId));
  const explain = page.locator(".wf-toolbar").getByRole("button", { name: "Explain", exact: true });
  await expect(explain).toBeVisible({ timeout: 30000 });
  const before = await owner("owner-before", executable.path);
  result.ownerBefore = before.receipt;
  need(before.value.artifactHash === executable.artifactHash, "The seeded Executable changed before the journey.");
  await nativeEnter("open-weaver", page.getByRole("button", { name: /^Weaver/ }), "Weaver");
  await expect(dock()).toBeVisible();
  const admitted = await inspectionExplain("explain", explain, "workflow_read_executable", {}, "workflows.executable",
    { resourceType: "workflow-executable", resourceId: executable.artifactId, revision: executable.artifactHash }, commands);
  need(admitted.content?.artifactHash === executable.artifactHash, "The admitted Executable is not bound to its Artifact Hash.");
  const after = await owner("owner-after", executable.path);
  result.ownerAfter = after.receipt;
  expect(after.value.artifactHash).toEqual(before.value.artifactHash);
  expect(after.value.references).toEqual(before.value.references);
  need(inspectionEffects().length === 0, "The Executable Inspector journey changed an owner: " + JSON.stringify(inspectionEffects()));
  await screenshot("workflow-executable-inspector-explained");
  result.commands = commands;
  result.observations = { executableContext: true, hashBound: true, explainCompleted: true, readOnly: true };
}
// Spec 1193 T045: the run page discloses the exact Run and the activity execution selected on it; its Explain action reads it
// with workflow_read_run. The page's own agent traffic (every request, every non-stream response) and every agent stream
// byte are searched, raw, for the Run canary, which exists only in the seeded workflow's sensitive input.
async function workflowRun() {
  const run = (config.workflow ?? {}).run ?? {};
  need(run.workflowExecutionId && run.path && run.activityExecutionId && typeof run.canary === "string" && run.canary.length >= 24,
    "The Run journey needs the seeded Run, its selected activity execution and the canary.");
  const commands = {};
  await page.goto(studio + "/workflows/instances/" + encodeURIComponent(run.workflowExecutionId) + "?selected=" + encodeURIComponent(run.activityExecutionId));
  // Verify the exact occurrence through the same copyable execution detail a person can inspect.
  const panel = page.getByLabel("Run details panel", { exact: true });
  await nativeEnter("inspect-run-activity", panel.getByRole("tab", { name: "Activity", exact: true }), "Activity");
  const technical = panel.locator(".wf-activity-execution-details");
  await expect(technical).toBeVisible({ timeout: 30000 });
  if (await technical.getAttribute("open") === null)
    await nativeEnter("inspect-run-execution-details", technical.locator("summary"), "Execution details");
  const selectedExecutionId = technical.locator(".wf-activity-meta-item")
    .filter({ has: page.locator("dt").filter({ hasText: /^Activity Execution ID$/ }) }).locator("dd code");
  await expect(selectedExecutionId).toBeVisible({ timeout: 30000 });
  await expect(selectedExecutionId).toHaveText(run.activityExecutionId, { timeout: 30000 });
  const before = await owner("owner-before", run.path);
  result.ownerBefore = before.receipt;
  need(before.value.instance?.workflowExecutionId === run.workflowExecutionId, "The seeded Run is not the run API's.");
  await nativeEnter("open-weaver", page.getByRole("button", { name: /^Weaver/ }), "Weaver");
  await expect(dock()).toBeVisible();
  await expect(dock()).toHaveAttribute("aria-busy", "false");
  await nativeEnter("open-run-actions", page.getByRole("button", { name: "More run actions", exact: true }), "More run actions");
  const explain = page.getByRole("menu", { name: "More run actions", exact: true }).getByRole("menuitem", { name: "Explain", exact: true });
  await expect(explain).toBeFocused();
  const admitted = await inspectionExplain("explain", explain, "workflow_read_run", {}, "workflows.run",
    { resourceType: "workflow-run", resourceId: run.workflowExecutionId, revision: run.activityExecutionId }, commands);
  need(admitted.content?.workflowExecutionId === run.workflowExecutionId && admitted.content?.activityExecutionId === run.activityExecutionId,
    "The admitted Run is not the shown Run and selection.");
  await Promise.all(agentResponseReads);
  // As in the explain journey: only complete, error-free captures with a real agent event stream make an absent canary count.
  const captured = await streamCapture();
  need(captured.every(item => item.captureError == null && item.captureValidationError == null)
    && captured.some(item => item.captureMethod !== "not-sse" && typeof item.rawBody === "string" && item.rawBody.startsWith("data: ")),
    "The in-page search has no complete, error-free agent stream capture to search.");
  need(agentTraffic.some(item => item.method === "POST" && item.url.endsWith("/messages")), "The in-page search observed no agent message to search.");
  need(!JSON.stringify(agentTraffic).includes(run.canary) && !captured.some(item => (item.rawBody ?? "").includes(run.canary)) && !JSON.stringify(admitted).includes(run.canary),
    "The Run canary left the run page.");
  const after = await owner("owner-after", run.path);
  result.ownerAfter = after.receipt;
  expect(after.value.instance).toEqual(before.value.instance);
  need(inspectionEffects().length === 0, "The Run journey changed an owner: " + JSON.stringify(inspectionEffects()));
  await screenshot("workflow-run-explained");
  result.commands = commands;
  result.observations = { runContext: true, selectionBound: true, explainCompleted: true, readOnly: true, canaryAbsentInPage: true };
}
function xpathString(value) {
  need(typeof value === "string" && !value.includes("'"), "Workflow ID must be a supported native identifier.");
  return "'" + value + "'";
}
try {
  if (config.journey === "sdk-incompatible") await incompatibleModule();
  else if (config.journey === "renderer-unavailable") await rendererUnavailable();
  else if (config.journey === "consumed-intent-remount") await consumedIntentRemount();
  else if (config.journey === "workflow-read-only") await workflowReadOnly();
  else if (config.journey === "workflow-batch") await workflowBatch();
  else if (config.journey === "workflow-explain") await workflowExplain();
  else if (config.journey === "workflow-library") await workflowLibrary();
  else if (config.journey === "workflow-templates") await workflowTemplates();
  else if (config.journey === "workflow-version-history") await workflowVersionHistory();
  else if (config.journey === "workflow-executable-inspector") await workflowExecutableInspector();
  else if (config.journey === "workflow-run") await workflowRun();
  else if (config.journey === "autonomy-size" || lifecycle) {
    const bounded = config.journey === "autonomy-size" ? autonomySize : config.journey === "resource" ? resource : workflow;
    let deadline;
    try {
      await Promise.race([bounded(), new Promise((_resolve, reject) => {
        deadline = setTimeout(() => {
          // Stop pending UI actions in this owned context; never let an expired
          // journey submit a late intent while failure evidence is captured.
          void context.close().catch(error => { result.deadlineCleanupError = redact({ type: error.name, message: error.message }); });
          reject(new Error("The bounded autonomy browser journey expired after 180 seconds; no retry."));
        }, 180000);
      })]);
    } finally { clearTimeout(deadline); }
  }
  else if (["workflow", "duplicate-ack", "client-binding-removed", "malformed-result", "lost-confirmation"].includes(config.journey)) await workflow();
  else await resource();
  need(pageErrors.length === 0, "Studio raised browser errors: " + pageErrors.join("; "));
  if (!result.expectedFailureObserved) result.status = "passed";
} catch (error) {
  result.status = "failed";
  result.failureDiagnostic = retainFailureDiagnostic(error);
  result.failure = redact({ type: error.name, message: error.message, ...(error.autonomyCause ? { cause: error.autonomyCause } : {}),
    ...(config.journey === "workflow-read-only" && refusedReads.length ? { refusedReads: refusedReads.map(item => item.status + " " + item.path) } : {}) });
  // A precise autonomy cause is a real failure, never an expected one.
  if (error.autonomyCause) result.actual = { stage: "session-mode", diagnostic: error.autonomyCause };
  try { await failureDom(); } catch (diagnosticError) {
    result.evidence.push(retain("failure-dom-unavailable", { type: diagnosticError.name, message: diagnosticError.message }));
  }
  try { await screenshot("failure"); } catch { /* Retain the original failure if the browser has gone away. */ }
} finally {
  result.completedAt = new Date().toISOString();
  result.evidence.push(retain("agent-streams", await streamCapture()));
  await Promise.all(pendingOwnerReads);
  result.evidence.push(retain("resource-dispatch-boundaries", { resourceInteractions, moduleOwnerReads }));
  result.evidence.push(retain("agent-requests", requests));
  if (autonomyCapture) result.evidence.push(retain("autonomy-network", autonomyCapture()));
  if (lifecycle || config.journey === "workflow-batch") result.evidence.push(retain("publish-attempts", publishAttempts));
  result.evidence.push(retain("owning-write-attempts", owningWrites));
  if (["workflow-library", "workflow-templates", "workflow-version-history", "workflow-executable-inspector", "workflow-run"].includes(config.journey))
    result.evidence.push(retain("design-write-attempts", designWrites));
  result.evidence.push(retain("browser-diagnostics", { consoleMessages, failedRequests }));
  result.pageErrors = redact(pageErrors);
  retain("observation", result);
  await context.close();
  await browser.close();
}
console.log(JSON.stringify({ status: result.status, journey: result.journey, expectedFailureObserved: !!result.expectedFailureObserved,
  observations: result.observations, failure: result.failure, evidence: path.join(config.output, "observation.json") }));
if (result.failure || result.status !== "passed" && !result.expectedFailureObserved) process.exitCode = 1;
