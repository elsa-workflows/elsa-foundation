import { describe, expect, it, vi } from "vitest";
import type { StudioEndpointContext } from "@elsa-workflows/studio-sdk";
import { pickConnectionSecrets } from "../connectionsApi";

const secretValue = "private-secret-value";

describe("Connections Secret picker API", () => {
  it("requests tenant-scoped active text Secret metadata and allowlists safe fields", async () => {
    const calls: unknown[][] = [];
    const context = endpointContext(async (url, body) => {
      calls.push([url, body]);
      return {
        items: [
          { name: "payments.api", displayName: "Payments API", typeName: "text", scope: "tenant-a", status: "active", expiresAt: null, value: secretValue, configurationKey: "secrets.private" },
          { name: "wrong.type", displayName: "Wrong type", typeName: "configuration", scope: "tenant-a", status: "active", value: secretValue },
          { name: "retired.api", displayName: "Retired", typeName: "text", status: "retired", value: secretValue },
          { name: "expired.api", displayName: "Expired", typeName: "text", status: "active", expiresAt: "2020-01-01T00:00:00Z", value: secretValue },
          { name: "invalid.expiry", displayName: "Invalid expiry", typeName: "text", status: "active", expiresAt: "not-a-date", value: secretValue }
        ],
        canCreateInline: true,
        totalCount: 500
      };
    });

    const result = await pickConnectionSecrets(context, "  payments  ");

    expect(calls).toEqual([[
      "/secrets/picker",
      { search: "payments", typeNames: ["text"], storeNames: [], activeOnly: true }
    ]]);
    expect(result).toEqual({
      items: [{
        name: "payments.api",
        displayName: "Payments API",
        typeName: "text",
        scope: "tenant-a",
        status: "Active",
        expiresAt: null
      }],
      capped: false
    });
    expect(JSON.stringify(result)).not.toContain(secretValue);
    expect(JSON.stringify(result)).not.toContain("configurationKey");
  });

  it("reports the picker cap without inventing a count or pagination state", async () => {
    const context = endpointContext(async () => ({
      items: Array.from({ length: 50 }, (_, index) => ({
        name: `secret.${index}`,
        displayName: `Secret ${index}`,
        typeName: "text",
        status: "active"
      })),
      canCreateInline: true
    }));

    const result = await pickConnectionSecrets(context, "secret");

    expect(result.items).toHaveLength(50);
    expect(result.capped).toBe(true);
    expect(result).not.toHaveProperty("totalCount");
    expect(result).not.toHaveProperty("nextPage");
  });
});

function endpointContext(postJson: (url: string, body: unknown) => Promise<unknown>): StudioEndpointContext {
  return {
    baseUrl: "https://server.example",
    http: {
      requestJson: vi.fn(),
      getJson: vi.fn(),
      postJson: vi.fn(postJson),
      putJson: vi.fn(),
      deleteJson: vi.fn()
    }
  } as unknown as StudioEndpointContext;
}
