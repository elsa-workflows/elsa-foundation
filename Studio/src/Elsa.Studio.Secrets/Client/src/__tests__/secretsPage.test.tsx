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

  it("keeps the created secret selected after refresh", async () => {
    let created = false;
    const postJson = vi.fn(async () => {
      created = true;
      return secret({ id: "2", name: "smtp-password", displayName: "SMTP Password" });
    });
    const { container, unmount } = render({
      getItems: () => created
        ? [paymentSecret(), secret({ id: "2", name: "smtp-password", displayName: "SMTP Password" })]
        : [paymentSecret()],
      postJson
    });

    await waitForText(container, "Payments API");
    clickButton(container, "Create");
    await waitFor(() => !!container.querySelector(".secrets-modal"));
    const inputs = Array.from(container.querySelectorAll<HTMLInputElement>(".secrets-modal input"));
    change(inputs[0], "smtp-password");
    change(inputs[1], "SMTP Password");
    change(inputs[4], "secret-value");
    clickButton(container, "Create", ".secrets-modal");

    await waitForText(container, "Secret created.");
    expect(container.querySelector(".secrets-detail header p")?.textContent).toBe("smtp-password");
    expect(postJson).toHaveBeenCalledWith("/_elsa/secrets", expect.objectContaining({ value: "secret-value" }));
    unmount();
  });

  it("sends an empty description when metadata description is cleared", async () => {
    const putJson = vi.fn(async () => paymentSecret({ description: "" }));
    const { container, unmount } = render({ putJson });

    await waitForText(container, "Payments API");
    await waitFor(() => container.querySelector<HTMLInputElement>(".secrets-edit-form input")?.value === "Payments API");
    change(container.querySelector<HTMLTextAreaElement>(".secrets-edit-form textarea")!, "");
    clickButton(container, "Save metadata");

    await waitFor(() => putJson.mock.calls.length > 0);
    expect(putJson).toHaveBeenCalledWith("/_elsa/secrets/payments.api", { displayName: "Payments API", description: "" });
    unmount();
  });
});

function render(options: {
  getItems?(): unknown[];
  postJson?: ReturnType<typeof vi.fn>;
  putJson?: ReturnType<typeof vi.fn>;
} = {}) {
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
              items: options.getItems?.() ?? [paymentSecret()],
              totalCount: 1,
              rawValue: "raw-secret-value"
            };
          }),
          postJson: options.postJson ?? vi.fn(),
          putJson: options.putJson ?? vi.fn(),
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

function paymentSecret(overrides: Partial<ReturnType<typeof secret>> = {}) {
  return secret({
    id: "1",
    name: "payments.api",
    displayName: "Payments API",
    description: "Token for payment provider",
    ...overrides
  });
}

function secret(overrides: {
  id: string;
  name: string;
  displayName: string;
  description?: string;
}) {
  return {
    id: overrides.id,
    name: overrides.name,
    displayName: overrides.displayName,
    typeName: "text",
    storeName: "encrypted",
    tags: [],
    status: "Active",
    currentVersion: 1,
    createdAt: "2026-06-24T00:00:00Z",
    description: overrides.description ?? ""
  };
}

function change(element: HTMLInputElement | HTMLTextAreaElement, value: string) {
  const prototype = element instanceof HTMLTextAreaElement ? HTMLTextAreaElement.prototype : HTMLInputElement.prototype;
  const descriptor = Object.getOwnPropertyDescriptor(prototype, "value");
  descriptor?.set?.call(element, value);
  element.dispatchEvent(new Event("input", { bubbles: true }));
}

function clickButton(container: HTMLElement, text: string, selector = "") {
  const root = selector ? container.querySelector(selector)! : container;
  const button = Array.from(root.querySelectorAll("button")).find(item => item.textContent?.trim() === text);
  if (!button) throw new Error(`Button not found: ${text}`);
  button.dispatchEvent(new MouseEvent("click", { bubbles: true }));
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
