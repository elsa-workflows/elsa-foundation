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

  it.each(["Content-Type", " content-type "])("rejects a %s row because the content type has its own field", name => {
    expect(() => makeHttpTestRequestHeaders([{ id: 1, name, value: "text/plain" }], "application/json"))
      .toThrow("Set Content-Type using the Request content type field.");
  });

  it.each([
    ["an invalid name", "X Invalid", "value"],
    ["a CRLF value", "X-Note", "safe\r\nInjected: yes"],
    ["a non-byte Unicode value", "X-Note", "value-😀"]
  ])("rejects %s without echoing the supplied value", (_description, name, value) => {
    expect(() => makeHttpTestRequestHeaders([{ id: 1, name, value }], null))
      .toThrowError(new Error("Header 1 has an invalid name or value."));
  });

  it.each(["Authorization", "authorization", "Cookie", "Proxy-Authorization"])("rejects the %s authentication header", name => {
    expect(() => makeHttpTestRequestHeaders([{ id: 1, name, value: "caller-secret" }], null))
      .toThrow("Authentication headers are supplied by the authenticated API transport.");
  });
});
