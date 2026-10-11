import { describe, expect, it, vi } from "vitest";
import type { StudioEndpointContext } from "@elsa-workflows/studio-sdk";
import { createSecret, deleteSecret, getSecretDescriptors, listSecrets, pickSecrets, rotateSecret, updateSecret } from "../secretsApi";

describe("secrets api", () => {
  it("calls backend routes with encoded names and safe payloads", async () => {
    const calls: unknown[][] = [];
    const context = api(calls);

    await listSecrets(context, "api key");
    await getSecretDescriptors(context);
    await createSecret(context, { name: "payments.api", typeName: "text", storeName: "encrypted", value: "secret" });
    await updateSecret(context, "payments.api", { displayName: "Payments API", description: "Payment provider token" });
    await rotateSecret(context, "payments/api", { value: "new-secret" });
    await pickSecrets(context, "pay", ["text"]);
    await deleteSecret(context, "payments/api");

    expect(calls).toEqual([
      ["get", "/_elsa/secrets?activeOnly=false&pageSize=100&search=api+key"],
      ["get", "/_elsa/secrets/descriptors"],
      ["post", "/_elsa/secrets", { name: "payments.api", typeName: "text", storeName: "encrypted", value: "secret" }],
      ["put", "/_elsa/secrets/payments.api", { displayName: "Payments API", description: "Payment provider token" }],
      ["post", "/_elsa/secrets/payments%2Fapi/rotate", { value: "new-secret" }],
      ["post", "/_elsa/secrets/picker", { search: "pay", typeNames: ["text"], storeNames: [], activeOnly: true }],
      ["delete", "/_elsa/secrets/payments%2Fapi"]
    ]);
  });
});

function api(calls: unknown[][]): StudioEndpointContext {
  return {
    baseUrl: "https://server.example",
    http: {
      requestJson: vi.fn(),
      getJson: vi.fn(async (url: string) => {
        calls.push(["get", url]);
        return url.includes("descriptors") ? { types: [], stores: [] } : { items: [], totalCount: 0 };
      }),
      postJson: vi.fn(async (url: string, body: unknown) => {
        calls.push(["post", url, body]);
        return url.includes("picker") ? { items: [], canCreateInline: true } : {};
      }),
      putJson: vi.fn(async (url: string, body: unknown) => {
        calls.push(["put", url, body]);
        return {};
      }),
      deleteJson: vi.fn(async (url: string) => {
        calls.push(["delete", url]);
        return {};
      }),
      postForm: vi.fn()
    }
  };
}
