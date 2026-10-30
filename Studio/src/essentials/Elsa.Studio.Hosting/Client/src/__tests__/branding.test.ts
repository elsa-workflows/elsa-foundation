import { describe, expect, it } from "vitest";
import { getStudioBranding, safeBrandUrl } from "../app/runtime";
describe("host branding", () => {
  it.each(["javascript:alert(1)", "data:image/svg+xml,unsafe", "//external.example", "/\\external.example", "https://example.com/a b"])("rejects unsafe asset/link URL %s", value => {
    expect(safeBrandUrl(value)).toBeUndefined();
  });
  it.each(["/brand.svg", "https://brand.example/image.jpg", "http://localhost:7243/brand.svg"])("accepts deployable asset/link URL %s", value => {
    expect(safeBrandUrl(value)).toBe(value);
  });
  it("resolves the host brand and rejects an unsafe logo", () => {
    window.__ELSA_STUDIO_RUNTIME__ = { branding: { productName: "Northstar", logoUrl: "javascript:alert(1)", privacyUrl: "/privacy" } };
    try { expect(getStudioBranding()).toMatchObject({ productName: "Northstar", logoUrl: undefined, privacyUrl: "/privacy" }); }
    finally { delete window.__ELSA_STUDIO_RUNTIME__; }
  });
});
