import { describe, expect, it } from "vitest";
import { getHttpContentTypeLanguage } from "../workflow-editor/httpContentTypeLanguage";

describe("getHttpContentTypeLanguage", () => {
  it.each([
    ["application/json", "json"],
    ["Application/JSON; charset=utf-8", "json"],
    ["application/vnd.acme.order+json; charset=UTF-8", "json"],
    ["application/xml", "xml"],
    ["TEXT/XML; charset=UTF-8", "xml"],
    ["application/vnd.acme.order+xml", "xml"],
    ["text/html; charset=utf-8", "html"],
    ["application/javascript", "javascript"],
    ["Text/JavaScript; charset=utf-8", "javascript"],
    ["application/ecmascript", "javascript"]
  ] as const)("maps %s to %s", (contentType, expectedLanguage) => {
    expect(getHttpContentTypeLanguage(contentType)).toBe(expectedLanguage);
  });

  it.each([
    [undefined, "plaintext"],
    [null, "plaintext"],
    ["", "plaintext"],
    ["  ; charset=utf-8", "plaintext"],
    ["text/plain", "plaintext"],
    ["text/csv", "plaintext"],
    ["application/x-www-form-urlencoded", "plaintext"],
    ["application/octet-stream", "plaintext"],
    ["application/unknown", "plaintext"],
    ["unknown+json", "plaintext"],
    ["application/json/extra", "plaintext"]
  ] as const)("uses plaintext for %s", (contentType, expectedLanguage) => {
    expect(getHttpContentTypeLanguage(contentType)).toBe(expectedLanguage);
  });
});
