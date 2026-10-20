import React, { useState } from "react";
import { flushSync } from "react-dom";
import { createRoot } from "react-dom/client";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { CreateSecretDialog, RotateSecretDialog } from "../SecretDialogs";
import { SECRET_SCOPE_MATCHING_HINT } from "../SecretScopePicker";
import type { CreateSecretRequest, RotateSecretRequest, SecretDescriptorsResponse, SecretMetadata } from "../secretTypes";

type SecretSaveRequest = CreateSecretRequest | RotateSecretRequest;

const descriptors: SecretDescriptorsResponse = {
  types: ["text", "json"].map(name => ({ name, displayName: name, supportedStoreNames: ["encrypted"] })),
  stores: [{ name: "encrypted", displayName: "Encrypted", isReadOnly: false }]
};
const scopeDescriptors: SecretDescriptorsResponse = {
  ...descriptors,
  scopes: [{ name: "module.payments", displayName: "Payments", description: "Payment module scope", source: "Module catalog" }]
};
const configurableDescriptors: SecretDescriptorsResponse = {
  types: [{ name: "text", displayName: "Text", supportedStoreNames: ["configuration", "encrypted"] }],
  stores: [
    { name: "configuration", displayName: "Configuration", isReadOnly: true },
    { name: "encrypted", displayName: "Encrypted", isReadOnly: false }
  ]
};
const switchableDescriptors: SecretDescriptorsResponse = {
  types: [{ name: "text", displayName: "Text", supportedStoreNames: ["encrypted", "configuration"] }, { name: "json", displayName: "JSON", supportedStoreNames: ["encrypted", "configuration"] }],
  stores: [
    { name: "encrypted", displayName: "Encrypted", isReadOnly: false },
    { name: "configuration", displayName: "Configuration", isReadOnly: true }
  ],
  scopes: scopeDescriptors.scopes
};
const unavailableStoreDescriptors: SecretDescriptorsResponse = {
  types: [
    { name: "text", displayName: "Text", supportedStoreNames: ["encrypted"] },
    { name: "json", displayName: "JSON", supportedStoreNames: ["unavailable"] }
  ],
  stores: [{ name: "encrypted", displayName: "Encrypted", isReadOnly: false }]
};
const secret: SecretMetadata = {
  id: "test", name: "test-key", displayName: "Test key", typeName: "text", storeName: "encrypted",
  tags: [], status: "Active", createdAt: "2026-10-17T00:00:00Z"
};
const cleanup = new Set<() => void>();
beforeEach(() => {
  vi.spyOn(window, "requestAnimationFrame").mockImplementation(callback => {
    callback(0);
    return 1;
  });
  vi.spyOn(window, "cancelAnimationFrame").mockImplementation(() => undefined);
});
afterEach(() => {
  for (const dispose of cleanup) dispose();
  cleanup.clear();
  vi.restoreAllMocks();
  vi.unstubAllGlobals();
});

describe("entered secret visibility", () => {
  it("explicitly associates the create description label with its textarea", () => {
    const { container } = renderDialog("create");
    const description = container.querySelector<HTMLTextAreaElement>(".secrets-modal textarea")!;
    expect(description.id).toBeTruthy();
    expect(description.labels?.[0]?.htmlFor).toBe(description.id);
    expect(description.labels?.[0]?.textContent).toBe("Description");
    expect(description.labels?.[0]?.contains(description)).toBe(false);
  });

  it("submits a custom scope without changing the authored value", async () => {
    const { container, onSave } = renderDialog("create");
    const scopeSelect = fieldControl<HTMLSelectElement>(container, "Scope");
    selectOption(scopeSelect, "Custom…");
    change(container.querySelector<HTMLInputElement>("input[id^='scope-custom-']")!, " Payments:Production ");
    change(container.querySelector<HTMLInputElement>(".secrets-value-input input")!, "replacement-value");

    flushSync(() => container.querySelector("form")!.dispatchEvent(new Event("submit", { bubbles: true, cancelable: true })));
    await waitFor(() => expect(onSave).toHaveBeenCalledTimes(1));
    expect(onSave).toHaveBeenCalledWith(expect.objectContaining({ scope: " Payments:Production " }));
  });

  it.each(["create", "rotate"] as const)("masks by default, toggles without submitting, and resets when reopening %s", mode => {
    const { container, onSave, render } = renderDialog(mode);
    const input = container.querySelector<HTMLInputElement>(".secrets-value-input input")!;
    change(input, "dummy-pasted-value");
    expect(input.type).toBe("password");
    expect(input.labels?.[0].textContent).toBe(mode === "create" ? "Value" : "New value");

    const toggle = container.querySelector<HTMLButtonElement>("button[aria-label='Show value']")!;
    expect(toggle.getAttribute("aria-controls")).toBe(input.id);
    expect(toggle.getAttribute("aria-pressed")).toBe("false");
    flushSync(() => toggle.click());
    expect(input.type).toBe("text");
    expect(input.value).toBe("dummy-pasted-value");
    expect(toggle.getAttribute("aria-label")).toBe("Hide value");
    expect(toggle.getAttribute("aria-pressed")).toBe("true");
    expect(onSave).not.toHaveBeenCalled();

    flushSync(() => toggle.click());
    expect(input.type).toBe("password");
    expect(input.value).toBe("dummy-pasted-value");
    flushSync(() => toggle.click());
    render(null);
    render(mode);
    expect(container.querySelector<HTMLInputElement>(".secrets-value-input input")?.type).toBe("password");
  });

  it("remasks when the selected secret type changes without losing entered text", () => {
    const { container } = renderDialog("create");
    change(container.querySelector<HTMLInputElement>(".secrets-value-input input")!, "dummy-pasted-value");
    flushSync(() => container.querySelector<HTMLButtonElement>("button[aria-label='Show value']")!.click());
    selectOption(fieldControl<HTMLSelectElement>(container, "Type"), "json");
    const input = container.querySelector<HTMLInputElement>(".secrets-value-input input")!;
    expect(input.type).toBe("password");
    expect(input.value).toBe("dummy-pasted-value");
  });
});

describe("Create dialog regression contract", () => {
  it("uses descriptor defaults, gates submission on Value, and submits the exact default request without reading stored values", () => {
    const fetch = vi.fn();
    vi.stubGlobal("fetch", fetch);
    const { container, onSave } = renderDialog("create");
    const dialog = container.querySelector<HTMLElement>("[role='dialog']")!;
    const type = fieldControl<HTMLSelectElement>(dialog, "Type");
    const store = fieldControl<HTMLSelectElement>(dialog, "Store");
    const value = fieldControl<HTMLInputElement>(dialog, "Value");
    const create = dialog.querySelector<HTMLButtonElement>("button[type='submit']")!;

    expect(type.value).toBe("text");
    expect(store.value).toBe("encrypted");
    expect(value.value).toBe("");
    expect(create.disabled).toBe(true);
    expect(onSave).not.toHaveBeenCalled();

    change(value, "new-secret-value");
    expect(create.disabled).toBe(false);
    flushSync(() => dialog.querySelector("form")!.dispatchEvent(new Event("submit", { bubbles: true, cancelable: true })));

    expect(onSave).toHaveBeenCalledWith({
      name: "test-key",
      displayName: "test-key",
      description: undefined,
      typeName: "text",
      storeName: "encrypted",
      scope: undefined,
      tags: [],
      value: "new-secret-value",
      configurationKey: undefined,
      expiresAt: undefined
    });
    expect(fetch).not.toHaveBeenCalled();
  });

  it("keeps Store directly recoverable when a Type has no supported stores", () => {
    const { container } = renderDialog("create", { descriptors: unavailableStoreDescriptors });
    const dialog = container.querySelector<HTMLElement>("[role='dialog']")!;
    const type = fieldControl<HTMLSelectElement>(dialog, "Type");
    const options = moreOptionsButton(dialog);
    const body = disclosureBody(dialog, options);

    selectOption(type, "JSON");
    expect(type.value).toBe("json");
    expect(fieldControl<HTMLSelectElement>(dialog, "Store").value).toBe("");
    expect(options.getAttribute("aria-expanded")).toBe("true");
    expect(body.hidden).toBe(false);
    expect(body.contains(fieldControl<HTMLSelectElement>(dialog, "Store"))).toBe(true);
    expect(body.contains(type)).toBe(false);

    selectOption(type, "Text");
    expect(type.value).toBe("text");
    expect(fieldControl<HTMLSelectElement>(dialog, "Store").value).toBe("encrypted");
  });

  it("uses Configuration key as the required source and trims it on submit", () => {
    const { container, onSave } = renderDialog("create", { descriptors: configurableDescriptors });
    const dialog = container.querySelector<HTMLElement>("[role='dialog']")!;
    const type = fieldControl<HTMLSelectElement>(dialog, "Type");
    const store = fieldControl<HTMLSelectElement>(dialog, "Store");
    const create = dialog.querySelector<HTMLButtonElement>("button[type='submit']")!;

    expect(type.value).toBe("text");
    expect(store.value).toBe("configuration");
    expect(fieldControl<HTMLInputElement>(dialog, "Configuration key").value).toBe("");
    expect(dialog.querySelector(".secrets-value-input input")).toBeNull();
    expect(create.disabled).toBe(true);
    change(fieldControl<HTMLInputElement>(dialog, "Configuration key"), "   ");
    expect(create.disabled).toBe(true);
    change(fieldControl<HTMLInputElement>(dialog, "Configuration key"), "  Payments:Primary  ");
    expect(create.disabled).toBe(false);

    flushSync(() => dialog.querySelector("form")!.dispatchEvent(new Event("submit", { bubbles: true, cancelable: true })));

    expect(onSave).toHaveBeenCalledWith({
      name: "test-key",
      displayName: "test-key",
      description: undefined,
      typeName: "text",
      storeName: "configuration",
      scope: undefined,
      tags: [],
      value: undefined,
      configurationKey: "Payments:Primary",
      expiresAt: undefined
    });
  });

  it("starts More options collapsed, keeps required fields and context direct, and counts only authored optional configuration", () => {
    const { container } = renderDialog("create", { descriptors: scopeDescriptors });
    const dialog = container.querySelector<HTMLElement>("[role='dialog']")!;
    const options = moreOptionsButton(dialog);
    const body = disclosureBody(dialog, options);
    const name = fieldControl<HTMLInputElement>(dialog, "Technical name");
    const type = fieldControl<HTMLSelectElement>(dialog, "Type");
    const value = fieldControl<HTMLInputElement>(dialog, "Value");
    const expires = fieldControl<HTMLInputElement>(dialog, "Expires");

    expect(options.getAttribute("aria-expanded")).toBe("false");
    expect(body.hidden).toBe(true);
    expect(body.contains(fieldControl<HTMLInputElement>(dialog, "Display name"))).toBe(true);
    expect(body.contains(fieldControl<HTMLTextAreaElement>(dialog, "Description"))).toBe(true);
    expect(body.contains(fieldControl<HTMLInputElement>(dialog, "Tags"))).toBe(true);
    expect(body.contains(fieldControl<HTMLSelectElement>(dialog, "Store"))).toBe(true);
    expect(body.contains(fieldControl<HTMLSelectElement>(dialog, "Scope"))).toBe(true);
    expect(body.contains(type)).toBe(false);
    expect(body.contains(value)).toBe(false);
    expect(body.contains(expires)).toBe(false);
    expect(name.compareDocumentPosition(type) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
    expect(type.compareDocumentPosition(value) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
    expect(expires.parentElement?.textContent).toContain("Expires");
    expect(textNodeOutside(dialog, "Store: Encrypted", body)).not.toBeNull();

    const scopeHint = [...dialog.querySelectorAll<HTMLParagraphElement>("p")].filter(paragraph => paragraph.textContent?.trim() === SECRET_SCOPE_MATCHING_HINT);
    expect(scopeHint).toHaveLength(1);
    expect(body.contains(scopeHint[0])).toBe(false);
    expect(fieldControl<HTMLSelectElement>(dialog, "Scope").getAttribute("aria-describedby")?.split(/\s+/)).toContain(scopeHint[0].id);

    change(name, "authored.name");
    change(value, "authored-source");
    change(expires, "2030-01-02T03:04");
    expect(options.textContent).not.toContain("set");

    flushSync(() => options.click());
    expect(options.getAttribute("aria-expanded")).toBe("true");
    expect(body.hidden).toBe(false);
    change(fieldControl<HTMLInputElement>(dialog, "Display name"), "Private title");
    expect(options.textContent).toContain("1 set");
    expect(options.textContent).not.toContain("Private title");
  });

  it("preserves Value and Configuration key through Store switching and remasks Value on Type change", () => {
    const { container, onSave } = renderDialog("create", { descriptors: switchableDescriptors });
    const dialog = container.querySelector<HTMLElement>("[role='dialog']")!;
    const value = fieldControl<HTMLInputElement>(dialog, "Value");
    change(value, "authored-value");
    flushSync(() => dialog.querySelector<HTMLButtonElement>("button[aria-label='Show value']")!.click());
    expect(value.type).toBe("text");

    const store = fieldControl<HTMLSelectElement>(dialog, "Store");
    revealFieldIfHidden(dialog, store);
    selectOption(store, "Configuration");
    change(fieldControl<HTMLInputElement>(dialog, "Configuration key"), " Auth:Payments ");
    selectOption(store, "Encrypted");
    const retainedValue = fieldControl<HTMLInputElement>(dialog, "Value");
    expect(retainedValue.value).toBe("authored-value");
    expect(retainedValue.type).toBe("password");

    selectOption(fieldControl<HTMLSelectElement>(dialog, "Type"), "JSON");
    expect(fieldControl<HTMLInputElement>(dialog, "Value").value).toBe("authored-value");
    expect(fieldControl<HTMLInputElement>(dialog, "Value").type).toBe("password");
    selectOption(fieldControl<HTMLSelectElement>(dialog, "Type"), "Text");
    expect(fieldControl<HTMLInputElement>(dialog, "Value").value).toBe("authored-value");
    selectOption(fieldControl<HTMLSelectElement>(dialog, "Store"), "Configuration");
    expect(fieldControl<HTMLInputElement>(dialog, "Configuration key").value).toBe(" Auth:Payments ");
    flushSync(() => dialog.querySelector("form")!.dispatchEvent(new Event("submit", { bubbles: true, cancelable: true })));

    expect(onSave).toHaveBeenCalledWith({
      name: "test-key",
      displayName: "test-key",
      description: undefined,
      typeName: "text",
      storeName: "configuration",
      scope: undefined,
      tags: [],
      value: undefined,
      configurationKey: "Auth:Payments",
      expiresAt: undefined
    });
  });

  it("retains optional values through manual collapse and reopen without exposing them in the disclosure label", () => {
    const { container } = renderDialog("create", { descriptors: switchableDescriptors });
    const dialog = container.querySelector<HTMLElement>("[role='dialog']")!;
    const options = moreOptionsButton(dialog);
    const body = disclosureBody(dialog, options);
    flushSync(() => options.click());
    change(fieldControl<HTMLInputElement>(dialog, "Display name"), "Payment token");
    expect(options.textContent).toContain("1 set");
    change(fieldControl<HTMLTextAreaElement>(dialog, "Description"), "  keep this description  ");
    expect(options.textContent).toContain("2 set");
    change(fieldControl<HTMLInputElement>(dialog, "Tags"), " api, payments, ");
    expect(options.textContent).toContain("3 set");
    selectOption(fieldControl<HTMLSelectElement>(dialog, "Scope"), "Custom…");
    change(fieldControl<HTMLInputElement>(dialog, "Custom scope value"), " Payments:Production ");
    expect(options.textContent).toContain("4 set");
    const store = fieldControl<HTMLSelectElement>(dialog, "Store");
    selectOption(store, "Configuration");
    expect(options.textContent).toContain("5 set");
    selectOption(store, "Encrypted");
    expect(options.textContent).toContain("4 set");

    flushSync(() => options.click());
    expect(options.getAttribute("aria-expanded")).toBe("false");
    expect(options.textContent).not.toContain("Payment token");
    expect(options.textContent).not.toContain("keep this description");
    expect(options.textContent).not.toContain("Payments:Production");
    flushSync(() => options.click());
    expect(options.getAttribute("aria-expanded")).toBe("true");
    expect(options.textContent).toContain("4 set");
    expect(fieldControl<HTMLInputElement>(dialog, "Display name").value).toBe("Payment token");
    expect(fieldControl<HTMLTextAreaElement>(dialog, "Description").value).toBe("  keep this description  ");
    expect(fieldControl<HTMLInputElement>(dialog, "Tags").value).toBe(" api, payments, ");
    expect(fieldControl<HTMLInputElement>(dialog, "Custom scope value").value).toBe(" Payments:Production ");
    expect(body.hidden).toBe(false);
  });

  it("normalizes Expires and preserves the exact authored source and optional payload", () => {
    const { container, onSave } = renderDialog("create", { descriptors: scopeDescriptors });
    const dialog = container.querySelector<HTMLElement>("[role='dialog']")!;
    change(fieldControl<HTMLInputElement>(dialog, "Technical name"), "  PAYMENTS.Api  ");
    const displayName = fieldControl<HTMLInputElement>(dialog, "Display name");
    revealFieldIfHidden(dialog, displayName);
    change(displayName, " Payments API ");
    change(fieldControl<HTMLTextAreaElement>(dialog, "Description"), "  API credential  ");
    change(fieldControl<HTMLInputElement>(dialog, "Tags"), "release, api,, ");
    selectOption(fieldControl<HTMLSelectElement>(dialog, "Scope"), "Custom…");
    change(fieldControl<HTMLInputElement>(dialog, "Custom scope value"), " Team:Production ");
    change(fieldControl<HTMLInputElement>(dialog, "Value"), "  raw-source-value  ");
    const localExpiry = "2030-01-02T03:04";
    change(fieldControl<HTMLInputElement>(dialog, "Expires"), localExpiry);
    flushSync(() => dialog.querySelector("form")!.dispatchEvent(new Event("submit", { bubbles: true, cancelable: true })));

    expect(onSave).toHaveBeenCalledWith({
      name: "payments.api",
      displayName: "Payments API",
      description: "API credential",
      typeName: "text",
      storeName: "encrypted",
      scope: " Team:Production ",
      tags: ["release", "api"],
      value: "  raw-source-value  ",
      configurationKey: undefined,
      expiresAt: new Date(localExpiry).toISOString()
    });
  });

  it("keeps the focused custom Scope through advisory catalog failure and recovery", () => {
    const onRetryScopeCatalog = vi.fn();
    const { container, render } = renderDialog("create", { descriptors: scopeDescriptors, onRetryScopeCatalog });
    const dialog = container.querySelector<HTMLElement>("[role='dialog']")!;
    const type = fieldControl<HTMLSelectElement>(dialog, "Type");
    const value = fieldControl<HTMLInputElement>(dialog, "Value");
    const create = dialog.querySelector<HTMLButtonElement>("button[type='submit']")!;
    const options = moreOptionsButton(dialog);

    change(value, "new-secret");
    expect(create.disabled).toBe(false);
    flushSync(() => options.click());
    selectOption(fieldControl<HTMLSelectElement>(dialog, "Scope"), "Custom…");
    const customScope = fieldControl<HTMLInputElement>(dialog, "Custom scope value");
    change(customScope, " Payments:Production ");
    customScope.focus();
    expect(document.activeElement).toBe(customScope);

    render("create", { descriptors: scopeDescriptors, scopeCatalogError: true, onRetryScopeCatalog });
    const body = disclosureBody(dialog, options);
    expect(options.getAttribute("aria-expanded")).toBe("true");
    expect(body.hidden).toBe(false);
    expect(dialog.querySelector("[role='alert']")?.textContent).toContain("Scope suggestions could not be refreshed");
    expect(fieldControl<HTMLInputElement>(dialog, "Custom scope value").value).toBe(" Payments:Production ");
    expect(create.disabled).toBe(false);
    expect(type.value).toBe("text");
    expect(document.activeElement).toBe(customScope);

    render("create", { descriptors: scopeDescriptors, scopeCatalogError: false, onRetryScopeCatalog });
    expect(options.getAttribute("aria-expanded")).toBe("true");
    expect(body.hidden).toBe(false);
    expect(document.activeElement).toBe(customScope);
    expect(create.disabled).toBe(false);
  });

  it("forces recovery open during advisory catalog failure and holds it open after recovery until dismissed", () => {
    const onRetryScopeCatalog = vi.fn();
    const { container, render } = renderDialog("create", { descriptors: scopeDescriptors, onRetryScopeCatalog });
    const dialog = container.querySelector<HTMLElement>("[role='dialog']")!;
    const value = fieldControl<HTMLInputElement>(dialog, "Value");
    const create = dialog.querySelector<HTMLButtonElement>("button[type='submit']")!;
    const options = moreOptionsButton(dialog);
    const body = disclosureBody(dialog, options);

    change(value, "new-secret");
    expect(create.disabled).toBe(false);
    flushSync(() => options.click());
    selectOption(fieldControl<HTMLSelectElement>(dialog, "Scope"), "Custom…");
    change(fieldControl<HTMLInputElement>(dialog, "Custom scope value"), " Payments:Production ");
    flushSync(() => options.click());
    expect(options.getAttribute("aria-expanded")).toBe("false");
    expect(body.hidden).toBe(true);

    render("create", { descriptors: scopeDescriptors, scopeCatalogError: true, onRetryScopeCatalog });
    expect(options.getAttribute("aria-expanded")).toBe("true");
    expect(body.hidden).toBe(false);
    expect(dialog.querySelector("[role='alert']")?.textContent).toContain("Scope suggestions could not be refreshed");
    expect(fieldControl<HTMLInputElement>(dialog, "Custom scope value").value).toBe(" Payments:Production ");
    expect(create.disabled).toBe(false);

    render("create", { descriptors: scopeDescriptors, scopeCatalogError: false, onRetryScopeCatalog });
    expect(options.getAttribute("aria-expanded")).toBe("true");
    expect(body.hidden).toBe(false);
    expect(create.disabled).toBe(false);
    flushSync(() => options.click());
    expect(options.getAttribute("aria-expanded")).toBe("false");
    flushSync(() => options.click());
    expect(fieldControl<HTMLInputElement>(dialog, "Custom scope value").value).toBe(" Payments:Production ");
  });
});

describe("secret dialog keyboard focus", () => {
  it.each(["create", "rotate"] as const)("focuses, contains, and restores focus for %s", mode => {
    const { container, opener } = renderDialogWithTrigger(mode);
    flushSync(() => {
      opener.focus();
      opener.click();
    });

    const dialog = container.querySelector<HTMLElement>("[role='dialog']")!;
    const focusable = [...dialog.querySelectorAll<HTMLElement>("button:not(:disabled), input:not(:disabled), select:not(:disabled), textarea:not(:disabled)")];
    expect(document.activeElement).toBe(dialog.querySelector("[data-dialog-initial-focus]") ?? focusable[0]);

    focusable[0].focus();
    flushSync(() => window.dispatchEvent(new KeyboardEvent("keydown", { key: "Tab", shiftKey: true, bubbles: true, cancelable: true })));
    expect(document.activeElement).toBe(focusable.at(-1));

    flushSync(() => window.dispatchEvent(new KeyboardEvent("keydown", { key: "Escape", bubbles: true, cancelable: true })));
    expect(container.querySelector("[role='dialog']")).toBeNull();
    expect(document.activeElement).toBe(opener);
  });

  it.each(["create", "rotate"] as const)("does not dismiss %s while saving", async mode => {
    let finishSave!: () => void;
    const onSave = vi.fn<(request: SecretSaveRequest) => Promise<void>>(() => new Promise<void>(resolve => { finishSave = resolve; }));
    const { container, opener } = renderDialogWithTrigger(mode, onSave);
    flushSync(() => {
      opener.focus();
      opener.click();
    });
    const dialog = container.querySelector<HTMLElement>("[role='dialog']")!;
    const input = dialog.querySelector<HTMLInputElement>(mode === "create" ? "input" : ".secrets-value-input input")!;
    change(input, mode === "create" ? "test-key" : "replacement-value");
    if (mode === "create") change(dialog.querySelectorAll<HTMLInputElement>(".secrets-value-input input")[0], "replacement-value");
    flushSync(() => dialog.querySelector<HTMLButtonElement>("button[type='submit']")!.click());
    await waitFor(() => expect(onSave).toHaveBeenCalledTimes(1));

    const cancel = [...dialog.querySelectorAll<HTMLButtonElement>("button")].find(button => button.textContent === "Cancel")!;
    expect(cancel.disabled).toBe(true);
    flushSync(() => cancel.click());
    expect(container.querySelector("[role='dialog']")).toBe(dialog);
    flushSync(() => dialog.querySelector("form")!.dispatchEvent(new Event("submit", { bubbles: true, cancelable: true })));
    expect(onSave).toHaveBeenCalledTimes(1);

    const escape = new KeyboardEvent("keydown", { key: "Escape", bubbles: true, cancelable: true });
    window.dispatchEvent(escape);
    expect(escape.defaultPrevented).toBe(true);
    expect(container.querySelector("[role='dialog']")).toBe(dialog);

    finishSave();
    await waitFor(() => {
      expect(container.querySelector("[role='dialog']")).toBeNull();
      expect(document.activeElement).toBe(opener);
    });
  });

  it("keeps Create actions outside the scroll body and focuses a failed-save message without clearing fields", async () => {
    const onSave = vi.fn(async () => { throw new Error("Fixture secret creation failed."); });
    const { container, opener } = renderDialogWithTrigger("create", onSave);
    flushSync(() => {
      opener.focus();
      opener.click();
    });

    const dialog = container.querySelector<HTMLElement>("[role='dialog']")!;
    const content = dialog.querySelector<HTMLElement>(".secrets-create-content")!;
    const actions = dialog.querySelector<HTMLElement>(".secrets-create-actions")!;
    expect(content.contains(actions)).toBe(false);
    expect(actions.parentElement).toBe(dialog.querySelector("form"));

    const technicalName = dialog.querySelector<HTMLInputElement>("input")!;
    const value = dialog.querySelector<HTMLInputElement>(".secrets-value-input input")!;
    change(technicalName, "preserved.secret");
    change(value, "preserved-secret-value");
    flushSync(() => dialog.querySelector("form")!.dispatchEvent(new Event("submit", { bubbles: true, cancelable: true })));

    await waitFor(() => expect(onSave).toHaveBeenCalledTimes(1));
    await waitFor(() => expect(dialog.querySelector("[role='alert']")).not.toBeNull());
    const alert = dialog.querySelector<HTMLElement>("[role='alert']")!;
    await waitFor(() => expect(document.activeElement).toBe(alert));
    expect(alert.tabIndex).toBe(0);
    expect(alert.textContent).toBe("Fixture secret creation failed.");
    expect(technicalName.value).toBe("preserved.secret");
    expect(value.value).toBe("preserved-secret-value");
  });
});

type DialogMode = "create" | "rotate";
type DialogRenderOptions = {
  descriptors?: SecretDescriptorsResponse;
  scopeCatalogLoading?: boolean;
  scopeCatalogError?: boolean;
  onRetryScopeCatalog?(): void;
};

function renderDialog(mode: DialogMode, options: DialogRenderOptions = {}) {
  const container = document.createElement("div");
  document.body.appendChild(container);
  const root = createRoot(container);
  const onSave = vi.fn<(request: SecretSaveRequest) => Promise<void>>(async () => undefined);
  const onCancel = vi.fn();
  let activeOptions = options;
  const render = (next: DialogMode | null, nextOptions?: DialogRenderOptions) => {
    if (nextOptions) activeOptions = nextOptions;
    flushSync(() => root.render(next === null ? null : next === "create"
      ? <CreateSecretDialog
          descriptors={activeOptions.descriptors ?? descriptors}
          scopeCatalogLoading={activeOptions.scopeCatalogLoading}
          scopeCatalogError={activeOptions.scopeCatalogError}
          onRetryScopeCatalog={activeOptions.onRetryScopeCatalog}
          onSave={onSave}
          onCancel={onCancel}
        />
      : <RotateSecretDialog secret={secret} descriptors={activeOptions.descriptors ?? descriptors} onSave={onSave} onCancel={onCancel} />));
  };
  cleanup.add(() => { flushSync(() => root.unmount()); container.remove(); });
  render(mode);
  if (mode === "create") change(fieldControl<HTMLInputElement>(container, "Technical name"), "test-key");
  return { container, onSave, render };
}

function renderDialogWithTrigger(mode: "create" | "rotate", onSave: (request: SecretSaveRequest) => Promise<void> = vi.fn(async () => undefined)) {
  const container = document.createElement("div");
  document.body.appendChild(container);
  const root = createRoot(container);
  flushSync(() => root.render(<DialogHarness mode={mode} onSave={onSave} />));
  cleanup.add(() => { flushSync(() => root.unmount()); container.remove(); });
  const opener = container.querySelector<HTMLButtonElement>("button")!;
  return { container, opener };
}

function DialogHarness({ mode, onSave }: { mode: "create" | "rotate"; onSave(request: SecretSaveRequest): Promise<void> }) {
  const [open, setOpen] = useState(false);
  return <>
    <button type="button" onClick={() => setOpen(true)}>Open dialog</button>
    {open ? mode === "create"
      ? <CreateSecretDialog descriptors={descriptors} onSave={onSave} onCancel={() => setOpen(false)} />
      : <RotateSecretDialog secret={secret} descriptors={descriptors} onSave={onSave} onCancel={() => setOpen(false)} />
      : null}
  </>;
}

async function waitFor(assertion: () => void) {
  for (let attempt = 0; attempt < 10; attempt++) {
    try {
      assertion();
      return;
    } catch {
      await new Promise(resolve => setTimeout(resolve, 0));
    }
  }
  assertion();
}

function change(input: HTMLInputElement | HTMLTextAreaElement, value: string) {
  flushSync(() => {
    const prototype = input instanceof HTMLTextAreaElement ? HTMLTextAreaElement.prototype : HTMLInputElement.prototype;
    Object.getOwnPropertyDescriptor(prototype, "value")!.set!.call(input, value);
    input.dispatchEvent(new Event("input", { bubbles: true }));
  });
}

function fieldControl<T extends HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement>(container: ParentNode, labelText: string): T {
  const label = [...container.querySelectorAll<HTMLLabelElement>("label")].find(candidate => directLabelText(candidate) === labelText);
  const control = label?.control;
  if (!control) throw new Error(`Field not found: ${labelText}`);
  return control as T;
}

function directLabelText(label: HTMLLabelElement) {
  return [...label.childNodes]
    .filter(node => node.nodeType === Node.TEXT_NODE)
    .map(node => node.textContent ?? "")
    .join(" ")
    .replace(/\s+/g, " ")
    .trim();
}

function moreOptionsButton(container: ParentNode) {
  const button = [...container.querySelectorAll<HTMLButtonElement>("button")].find(candidate =>
    candidate.querySelector(".studio-disclosure-fold-label")?.textContent?.trim() === "More options");
  if (!button) throw new Error("More options disclosure not found.");
  return button;
}

function disclosureBody(container: HTMLElement, button: HTMLButtonElement) {
  const bodyId = button.getAttribute("aria-controls");
  const body = bodyId ? document.getElementById(bodyId) : null;
  if (!body || !container.contains(body)) throw new Error("More options disclosure body not found.");
  return body;
}

function revealFieldIfHidden(container: HTMLElement, field: HTMLElement) {
  const body = field.closest<HTMLElement>("[hidden]");
  if (!body) return;
  const button = [...container.querySelectorAll<HTMLButtonElement>("button")].find(candidate => candidate.getAttribute("aria-controls") === body.id);
  if (!button) throw new Error("Hidden field has no disclosure control.");
  flushSync(() => button.click());
}

function textNodeOutside(container: HTMLElement, text: string, excluded: HTMLElement) {
  const walker = document.createTreeWalker(container, NodeFilter.SHOW_TEXT);
  while (walker.nextNode()) {
    const node = walker.currentNode;
    const parent = node.parentElement;
    if (parent && node.textContent?.includes(text) && !excluded.contains(parent) && !parent.closest("select, option")) return parent;
  }
  return null;
}

function selectOption(select: HTMLSelectElement, label: string) {
  const option = [...select.options].find(item => item.textContent === label);
  if (!option) throw new Error(`Option not found: ${label}`);
  flushSync(() => {
    Object.getOwnPropertyDescriptor(HTMLSelectElement.prototype, "value")!.set!.call(select, option.value);
    select.dispatchEvent(new Event("change", { bubbles: true }));
  });
}
