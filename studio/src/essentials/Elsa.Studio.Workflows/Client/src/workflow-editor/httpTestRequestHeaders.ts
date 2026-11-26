import { parseHttpTestRequestHeaders, type HttpTestRequestHeaderValues } from "../httpTestHeaderPolicy";

export interface HttpTestRequestHeader {
  id: number;
  name: string;
  value: string;
}

interface HeaderOptions {
  configuredHeaders?: HeadersInit;
  rejectContentTypeHeader?: boolean;
  ignoreBlankRows?: boolean;
  contentTypeFieldValue?: string | null;
  addDefaultAccept?: boolean;
}

export class HttpTestRequestHeaderPolicyError extends Error {
  constructor(message: string) {
    super(message);
    this.name = "HttpTestRequestHeaderPolicyError";
  }
}

export function makeValidatedHttpTestRequestHeaders(
  headers: HttpTestRequestHeaderValues[],
  options: HeaderOptions = {}
): Headers {
  const requestHeaders = options.ignoreBlankRows
    ? headers.filter(({ name, values }) => name.trim() || values.some(value => value !== ""))
    : headers;
  const parsed = parseHttpTestRequestHeaders(requestHeaders);
  if (!parsed.headers) throw new HttpTestRequestHeaderPolicyError(parsed.error ?? "Request headers could not be validated.");

  let configured: Headers;
  try {
    configured = new Headers(options.configuredHeaders);
  } catch {
    throw new HttpTestRequestHeaderPolicyError("Studio host headers could not be validated. Refresh Studio and try again.");
  }
  for (const [index, { name }] of requestHeaders.entries()) {
    const normalizedName = name.trim();
    if (options.rejectContentTypeHeader && normalizedName.toLowerCase() === "content-type") {
      throw new HttpTestRequestHeaderPolicyError("Set Content-Type using the Request content type field.");
    }
    if (configured.has(normalizedName)) {
      throw new HttpTestRequestHeaderPolicyError(`Header ${index + 1} is configured by Studio and cannot be overridden.`);
    }
  }

  const contentType = options.contentTypeFieldValue?.trim() ?? "";
  if (contentType) {
    if (configured.has("content-type")) {
      throw new HttpTestRequestHeaderPolicyError("Request Content-Type is configured by Studio and cannot be overridden.");
    }
    try {
      parsed.headers.set("Content-Type", contentType);
    } catch {
      throw new HttpTestRequestHeaderPolicyError("The request content type has an invalid name or value.");
    }
  }
  if (options.addDefaultAccept && !parsed.headers.has("accept") && !configured.has("accept")) {
    parsed.headers.set("Accept", "*/*");
  }
  return parsed.headers;
}

export function makeHttpTestRequestHeaders(
  rows: HttpTestRequestHeader[],
  contentType: string | null,
  configuredHeaders?: HeadersInit
): Headers {
  return makeValidatedHttpTestRequestHeaders(rows.map(row => ({ name: row.name, values: [row.value] })), {
    configuredHeaders,
    rejectContentTypeHeader: true,
    ignoreBlankRows: true,
    contentTypeFieldValue: contentType,
    addDefaultAccept: true
  });
}
