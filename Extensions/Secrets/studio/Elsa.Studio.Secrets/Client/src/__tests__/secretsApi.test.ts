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
      ["get", "/secrets?activeOnly=false&pageSize=100&search=api+key"],
      ["get", "/secrets/descriptors"],
      ["post", "/secrets", { name: "payments.api", typeName: "text", storeName: "encrypted", value: "secret" }],
      ["put", "/secrets/payments.api", { displayName: "Payments API", description: "Payment provider token" }],
      ["post", "/secrets/payments%2Fapi/rotate", { value: "new-secret" }],
      ["post", "/secrets/picker", { search: "pay", typeNames: ["text"], storeNames: [], activeOnly: true }],
      ["delete", "/secrets/payments%2Fapi"]
    ]);
  });
});

function api(calls: unknown[][]): StudioEndpointContext {
  return {
    baseUrl: "https://server.example",
    http: {
      requestJson: vi.fn(),
      getJson: async <T,>(url: string): Promise<T> => {
        calls.push(["get", url]);
        const response: unknown = url.includes("descriptors") ? { types: [], stores: [] } : { items: [], totalCount: 0 };
        return response as T;
      },
      postJson: async <T,>(url: string, body: unknown): Promise<T> => {
        calls.push(["post", url, body]);
        const response: unknown = url.includes("picker") ? { items: [], canCreateInline: true } : {};
        return response as T;
      },
      putJson: async <T,>(url: string, body: unknown): Promise<T> => {
        calls.push(["put", url, body]);
        const response: unknown = {};
        return response as T;
      },
      deleteJson: async <T,>(url: string): Promise<T> => {
        calls.push(["delete", url]);
        const response: unknown = {};
        return response as T;
      },
      postForm: vi.fn()
    }
  };
}
