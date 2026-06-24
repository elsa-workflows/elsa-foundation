import React from "react";
import { flushSync } from "react-dom";
import { createRoot } from "react-dom/client";
import { describe, expect, it, vi } from "vitest";
import { SecretsPage } from "../SecretsPage";

describe("secrets page", () => {
  it("renders metadata without raw secret values", async () => {
    const { container, unmount } = render();

    await waitForText(container, "Payments API");

    expect(container.textContent).toContain("payments.api");
    expect(container.textContent).toContain("Values are never displayed.");
    expect(container.textContent).not.toContain("raw-secret-value");
    unmount();
  });
});

function render() {
  const host = document.createElement("div");
  document.body.appendChild(host);
  const root = createRoot(host);
  flushSync(() => root.render(
    <SecretsPage
      context={{
        baseUrl: "",
        http: {
          requestJson: vi.fn(),
          getJson: vi.fn(async (url: string) => {
            if (url.includes("descriptors")) {
              return {
                types: [{ name: "text", displayName: "Text", description: "", editorHint: "text", supportedStoreNames: ["encrypted"] }],
                stores: [{ name: "encrypted", displayName: "Encrypted", description: "", capabilities: 1, isReadOnly: false }]
              };
            }
            return {
              items: [
                {
                  id: "1",
                  name: "payments.api",
                  displayName: "Payments API",
                  typeName: "text",
                  storeName: "encrypted",
                  tags: [],
                  status: "Active",
                  currentVersion: 1,
                  createdAt: "2026-06-24T00:00:00Z",
                  description: "Token for payment provider"
                }
              ],
              totalCount: 1,
              rawValue: "raw-secret-value"
            };
          }),
          postJson: vi.fn(),
          putJson: vi.fn(),
          deleteJson: vi.fn(),
          postForm: vi.fn()
        }
      }}
    />
  ));

  return {
    container: host,
    unmount: () => {
      root.unmount();
      host.remove();
    }
  };
}

async function waitForText(container: HTMLElement, text: string) {
  await waitFor(() => container.textContent?.includes(text) ?? false);
}

async function waitFor(predicate: () => boolean) {
  for (let i = 0; i < 20; i++) {
    if (predicate()) return;
    await new Promise(resolve => setTimeout(resolve, 0));
  }
  throw new Error("Timed out waiting for predicate.");
}
