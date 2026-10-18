export interface HttpTestRequestHeaderValues {
  name: string;
  values: string[];
}

const managedHeaders = new Set(["authorization", "cookie", "cookie2", "proxy-authorization"]);
// Fetch forbidden request-header names, plus User-Agent because Chromium silently drops overrides.
const browserControlledHeaders = new Set([
  "accept-charset", "accept-encoding", "access-control-request-headers", "access-control-request-method",
  "connection", "content-length", "date", "dnt", "expect", "host", "keep-alive", "origin", "referer",
  "set-cookie", "te", "trailer", "transfer-encoding", "upgrade", "user-agent", "via"
]);
const methodOverrideHeaders = new Set(["x-http-method", "x-http-method-override", "x-method-override"]);

export function toHttpTestRequestHeaderValues(headers: Record<string, unknown>): HttpTestRequestHeaderValues[] {
  return Object.entries(headers).map(([name, value]) => ({ name, values: Array.isArray(value) ? value as string[] : [value as string] }));
}

export function parseHttpTestRequestHeaders(headers: HttpTestRequestHeaderValues[]): { headers?: Headers; error?: string } {
  const result = new Headers();
  for (const [index, header] of headers.entries()) {
    const name = typeof header?.name === "string" ? header.name.trim() : "";
    const values = Array.isArray(header?.values) ? header.values : [];
    if (!name || !Array.isArray(header?.values) || values.some(value => typeof value !== "string")) {
      return { error: invalidHeader(index) };
    }

    const lowerName = name.toLowerCase();
    if (managedHeaders.has(lowerName)) return { error: "Authentication headers are managed by the browser or Studio transport." };
    if (browserControlledHeaders.has(lowerName) || lowerName.startsWith("proxy-") || lowerName.startsWith("sec-")) {
      return { error: `Header ${index + 1} (${name}) is controlled by the browser and cannot be set here.` };
    }
    if (methodOverrideHeaders.has(lowerName) && values.some(hasForbiddenMethod)) {
      return { error: `Header ${index + 1} (${name}) cannot override a browser-forbidden HTTP method.` };
    }

    try {
      if (!values.length) new Headers([[name, ""]]);
      for (const value of values) result.append(name, value);
    } catch {
      return { error: invalidHeader(index) };
    }
  }

  return { headers: result };
}

function hasForbiddenMethod(value: string) {
  return value.split(",").some(method => ["connect", "trace", "track"].includes(method.trim().replace(/^"|"$/g, "").toLowerCase()));
}

function invalidHeader(index: number) {
  return `Header ${index + 1} has an invalid name or value.`;
}
