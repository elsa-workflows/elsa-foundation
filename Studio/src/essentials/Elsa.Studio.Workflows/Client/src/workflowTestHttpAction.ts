import type { StudioTestStimulusEditorContribution } from "@elsa-workflows/studio-sdk";
import { asRecord } from "./workflowTestStimulusEditorUtils";

export async function sendHttpRequest({ backend, action, data }: Parameters<NonNullable<StudioTestStimulusEditorContribution["executeAction"]>>[0]) {
  const relativeUrl = action.relativeUrl;
  if (!relativeUrl || !relativeUrl.startsWith("/") || relativeUrl.startsWith("//")) {
    throw new Error("The prepared HTTP action did not contain a safe server-relative URL.");
  }
  if (!backend.http.requestResponse) throw new Error("This Studio host does not expose the authenticated HTTP transport.");
  const base = new URL(backend.baseUrl || window.location.href, window.location.href);
  const url = new URL(relativeUrl, base.origin);
  if (url.origin !== base.origin) throw new Error("The prepared HTTP action must stay on the Studio API host.");
  const body = asRecord(data) ?? {};
  const query = asRecord(body.query) ?? {};
  for (const [key, raw] of Object.entries(query)) {
    const values = Array.isArray(raw) ? raw : [raw];
    for (const value of values) if (typeof value === "string") url.searchParams.append(key, value);
  }
  const headers = new Headers();
  for (const [key, raw] of Object.entries(asRecord(body.headers) ?? {})) {
    const values = Array.isArray(raw) ? raw : [raw];
    for (const value of values) if (typeof value === "string") headers.append(key, value);
  }
  const method = (action.method || "POST").toUpperCase();
  const textBody = typeof body.body === "string" ? body.body : undefined;
  const startedAt = performance.now();
  const controller = new AbortController();
  const timeoutMs = typeof body.timeoutMs === "number" && Number.isFinite(body.timeoutMs)
    ? Math.max(1000, Math.min(body.timeoutMs, 120000))
    : 30000;
  const timeout = window.setTimeout(() => controller.abort(), timeoutMs);
  try {
    const response = await backend.http.requestResponse(url.href, {
      method,
      headers,
      ...(textBody !== undefined && !["GET", "HEAD"].includes(method) ? { body: textBody } : {}),
      signal: controller.signal,
      retryOnUnauthorized: false
    });
    const responseText = await response.text();
    return {
      kind: "http-exchange",
      method,
      status: response.status,
      statusText: response.statusText,
      ok: response.ok,
      headers: [...response.headers.entries()],
      body: responseText,
      durationMs: Math.round(performance.now() - startedAt)
    };
  } catch {
    return {
      kind: "http-exchange",
      method,
      error: controller.signal.aborted ? `Request timed out after ${timeoutMs} ms.` : "The HTTP request failed.",
      durationMs: Math.round(performance.now() - startedAt)
    };
  } finally {
    window.clearTimeout(timeout);
  }
}
