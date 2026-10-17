import React from "react";
import { flushSync } from "react-dom";
import { createRoot } from "react-dom/client";
import { afterEach, describe, expect, it, vi } from "vitest";
import { SecretsPage } from "../SecretsPage";

const cleanup = new Set<() => void>();
const clipboardDescriptor = Object.getOwnPropertyDescriptor(navigator, "clipboard");
afterEach(() => {
  for (const dispose of cleanup) dispose();
  if (clipboardDescriptor) Object.defineProperty(navigator, "clipboard", clipboardDescriptor);
  else Reflect.deleteProperty(navigator, "clipboard");
});

describe("secrets page", () => {
  it("clears entered rotation material and visibility when the target secret changes", async () => {
    const { container } = render({ getItems: () => [paymentSecret(), secret({ id: "2", name: "other.api", displayName: "Other API" })] });
    await waitForText(container, "Other API");
    flushSync(() => container.querySelector<HTMLButtonElement>("button[aria-label='Rotate payments.api']")!.click());
    change(container.querySelector<HTMLInputElement>(".secrets-value-input input")!, "dummy-new-value");
    await waitFor(() => container.querySelector<HTMLInputElement>(".secrets-value-input input")?.value === "dummy-new-value");
    flushSync(() => container.querySelector<HTMLButtonElement>("button[aria-label='Show value']")!.click());
    expect(container.querySelector<HTMLInputElement>(".secrets-value-input input")?.type).toBe("text");
    flushSync(() => container.querySelector<HTMLButtonElement>("button[aria-label='Rotate other.api']")!.click());
    const input = container.querySelector<HTMLInputElement>(".secrets-value-input input")!;
    expect(input.value).toBe("");
    expect(input.type).toBe("password");
  });

  it("offers creation from an empty collection without a blank details panel", async () => {
    const { container } = render({ getItems: () => [] });
    await waitForText(container, "No secrets yet");
    expect(container.querySelector(".secrets-detail")).toBeNull();
    expect(container.textContent).not.toContain("Select a secret.");
    clickButton(container, "Create");
    await waitFor(() => !!container.querySelector("[role=dialog][aria-label='Create secret']"));
  });

  it("shows a filtered empty state and restores the selected metadata when filters are cleared", async () => {
    const { container } = render();
    await waitForText(container, "Payments API");
    const status = [...container.querySelectorAll<HTMLSelectElement>(".secrets-filter select")]
      .find(select => select.parentElement?.textContent?.includes("Status"))!;
    selectValue(status, "Revoked");
    await waitForText(container, "No secrets match your filters");
    expect(container.querySelector(".secrets-detail")).toBeNull();
    selectValue(status, "");
    await waitFor(() => !!container.querySelector(".secrets-detail"));
    expect(container.querySelector(".secrets-detail h3")?.textContent).toBe("Payments API");
  });

  it("keeps unavailable results distinct from an empty collection and retries", async () => {
    let fail = true;
    const { container } = render({ getItems: () => {
      if (fail) throw new Error("private-backend-detail");
      return [];
    } });
    await waitForText(container, "Secrets could not be loaded.");
    expect(container.querySelector("[role=alert]")).not.toBeNull();
    expect(container.textContent).not.toContain("No secrets yet");
    expect(container.textContent).not.toContain("private-backend-detail");
    fail = false;
    clickButton(container, "Retry");
    await waitForText(container, "No secrets yet");
  });

  it("renders metadata without raw secret values", async () => {
    const { container, unmount } = render();

    await waitForText(container, "Payments API");

    expect(container.textContent).toContain("payments.api");
    expect(container.querySelector("button[aria-label='Open payments.api']")).not.toBeNull();
    expect(container.textContent).toContain("Values are never displayed.");
    expect(container.textContent).not.toContain("raw-secret-value");
    unmount();
  });

  it("copies only the technical name from both the list and details", async () => {
    const writeText = vi.fn().mockResolvedValue(undefined);
    Object.defineProperty(navigator, "clipboard", { configurable: true, value: { writeText } });
    const { container } = render();
    await waitForText(container, "Payments API");
    const buttons = [...container.querySelectorAll<HTMLButtonElement>("button[aria-label='Copy technical name payments.api']")];
    expect(buttons).toHaveLength(2);
    for (const button of buttons) {
      button.dispatchEvent(new MouseEvent("click", { bubbles: true }));
      await waitFor(() => button.parentElement?.textContent === "Copied");
    }
    expect(writeText.mock.calls).toEqual([["payments.api"], ["payments.api"]]);
    expect(JSON.stringify(writeText.mock.calls)).not.toContain("raw-secret-value");
  });

  it("reports clipboard failure without claiming a name was copied", async () => {
    Object.defineProperty(navigator, "clipboard", { configurable: true, value: { writeText: vi.fn().mockRejectedValue(new Error("denied")) } });
    const { container } = render();
    await waitForText(container, "Payments API");
    container.querySelector<HTMLButtonElement>("button[aria-label='Copy technical name payments.api']")!.click();
    await waitForText(container, "Copy failed");
    expect(container.textContent).not.toContain("Copied");
    expect(container.textContent).toContain("payments.api");
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
    expect(postJson).toHaveBeenCalledWith("/secrets", expect.objectContaining({ value: "secret-value" }));
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
    expect(putJson).toHaveBeenCalledWith("/secrets/payments.api", { displayName: "Payments API", description: "" });
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
            const items = options.getItems?.() ?? [paymentSecret()];
            return {
              items,
              totalCount: items.length,
              rawValue: "raw-secret-value"
            };
          }),
          postJson: options.postJson ?? vi.fn(),
          putJson: options.putJson ?? vi.fn(),
          deleteJson: vi.fn(),
          postForm: vi.fn()
        }
      }}
      dialogs={{
        confirm: vi.fn(async () => true),
        prompt: vi.fn(async () => null),
        alert: vi.fn(async () => {})
      }}
    />
  ));

  const unmount = () => {
    flushSync(() => root.unmount());
    host.remove();
    cleanup.delete(unmount);
  };
  cleanup.add(unmount);
  return { container: host, unmount };
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

function selectValue(element: HTMLSelectElement, value: string) {
  flushSync(() => {
    Object.getOwnPropertyDescriptor(HTMLSelectElement.prototype, "value")?.set?.call(element, value);
    element.dispatchEvent(new Event("change", { bubbles: true }));
  });
}
