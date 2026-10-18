import { describe, expect, it } from "vitest";
import { makeHttpTestRequestHeaders } from "../workflow-editor/httpTestRequestHeaders";

describe("HTTP test request headers", () => {
  it("ignores wholly blank rows and supplies the default Accept and body content type", () => {
    const headers = makeHttpTestRequestHeaders([
      { id: 1, name: "", value: "" },
      { id: 2, name: "  ", value: "" }
    ], " application/json ");

    expect(headers.get("Accept")).toBe("*/*");
    expect(headers.get("Content-Type")).toBe("application/json");
  });

  it("uses a custom Accept value and appends duplicate header names", () => {
    const headers = makeHttpTestRequestHeaders([
      { id: 1, name: "accept", value: "text/plain" },
      { id: 2, name: "Accept", value: "application/json" },
      { id: 3, name: "X-Tag", value: "one" },
      { id: 4, name: "x-tag", value: "two" }
    ], null);

    expect(headers.get("Accept")).toBe("text/plain, application/json");
    expect(headers.get("X-Tag")).toBe("one, two");
    expect(headers.has("Content-Type")).toBe(false);
  });

  it("rejects a Content-Type row because the content type has its own field", () => {
    const name = "Content-Type";
    expect(() => makeHttpTestRequestHeaders([{ id: 1, name, value: "text/plain" }], "application/json"))
      .toThrow("Set Content-Type using the Request content type field.");
  });

  it.each([
    ["an invalid name", "X Invalid", "value"],
    ["a CRLF value", "X-Note", "safe\r\nInjected: yes"],
    ["a NUL value", "X-Note", "safe\0value"],
    ["a non-byte Unicode value", "X-Note", "value-😀"]
  ])("rejects %s without echoing the supplied value", (_description, name, value) => {
    expect(() => makeHttpTestRequestHeaders([{ id: 1, name, value }], null))
      .toThrowError(new Error("Header 1 has an invalid name or value."));
  });

  it("uses the native Headers normalization for surrounding field whitespace", () => {
    const headers = makeHttpTestRequestHeaders([{ id: 1, name: "X-Note", value: " value " }], null);
    expect(headers.get("X-Note")).toBe("value");
  });

  it.each(["Authorization", "authorization", "Cookie", "Proxy-Authorization"])("rejects the %s authentication header", name => {
    expect(() => makeHttpTestRequestHeaders([{ id: 1, name, value: "caller-secret" }], null))
      .toThrow("Authentication headers are managed by the browser or Studio transport.");
  });

  it.each([
    "Host", "Origin", "Referer", "Connection", "Content-Length", "Accept-Encoding",
    "Access-Control-Request-Method", "Set-Cookie", "Sec-Fetch-Site", "Proxy-Debug", "User-Agent"
  ])("rejects browser-controlled request header %s before sending", name => {
    expect(() => makeHttpTestRequestHeaders([{ id: 1, name, value: "caller-secret" }], null))
      .toThrow(`Header 1 (${name}) is controlled by the browser and cannot be set here.`);
  });

  it("rejects method-override values that would make Fetch forbid the header", () => {
    expect(() => makeHttpTestRequestHeaders([{ id: 1, name: "X-HTTP-Method-Override", value: "TRACE" }], null))
      .toThrow("Header 1 (X-HTTP-Method-Override) cannot override a browser-forbidden HTTP method.");
    expect(() => makeHttpTestRequestHeaders([{ id: 1, name: "X-Method-Override", value: "POST, tRaCe" }], null))
      .toThrow("Header 1 (X-Method-Override) cannot override a browser-forbidden HTTP method.");
    expect(() => makeHttpTestRequestHeaders([{ id: 1, name: "X-Method-Override", value: '"TRACE"' }], null))
      .toThrow("Header 1 (X-Method-Override) cannot override a browser-forbidden HTTP method.");
    expect(() => makeHttpTestRequestHeaders([{ id: 1, name: "X-HTTP-Method-Override", value: "POST" }], null)).not.toThrow();
  });

  it("allows Permissions-Policy because it is not a Fetch forbidden request-header", () => {
    expect(makeHttpTestRequestHeaders([{ id: 1, name: "Permissions-Policy", value: "interest-cohort=()" }], null)
      .get("Permissions-Policy")).toBe("interest-cohort=()");
  });

  it("does not override host-configured headers or replace a host Accept with its fallback", () => {
    const configuredHeaders = { "X-Studio-Scope": "host-secret", Accept: "application/vnd.elsa+json" };
    expect(() => makeHttpTestRequestHeaders([{ id: 1, name: "x-studio-scope", value: "caller-secret" }], null, configuredHeaders))
      .toThrow("Header 1 is configured by Studio and cannot be overridden.");

    const headers = makeHttpTestRequestHeaders([], null, configuredHeaders);
    expect(headers.has("Accept")).toBe(false);
  });

  it("does not let the request Content-Type field replace a host-configured value", () => {
    expect(() => makeHttpTestRequestHeaders([], "application/json", { "Content-Type": "application/vnd.host+json" }))
      .toThrow("Request Content-Type is configured by Studio and cannot be overridden.");
  });
});
