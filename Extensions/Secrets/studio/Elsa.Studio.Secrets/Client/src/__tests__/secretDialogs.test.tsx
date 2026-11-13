import React, { useState } from "react";
import { flushSync } from "react-dom";
import { createRoot } from "react-dom/client";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { CreateSecretDialog, RotateSecretDialog } from "../SecretDialogs";
import type { CreateSecretRequest, RotateSecretRequest, SecretDescriptorsResponse, SecretMetadata } from "../secretTypes";

type SecretSaveRequest = CreateSecretRequest | RotateSecretRequest;

const descriptors: SecretDescriptorsResponse = {
  types: ["text", "json"].map(name => ({ name, displayName: name, supportedStoreNames: ["encrypted"] })),
  stores: [{ name: "encrypted", displayName: "Encrypted", isReadOnly: false }]
};
const scopeDescriptors: SecretDescriptorsResponse = {
  ...descriptors,
  types: descriptors.types.map(type => ({ ...type, description: "Fixture type description." })),
  stores: descriptors.stores.map(store => ({ ...store, description: "Fixture store description." })),
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
    change(container.querySelector<HTMLInputElement>('input[data-secret-field="value"]')!, "replacement-value");

    flushSync(() => container.querySelector("form")!.dispatchEvent(new Event("submit", { bubbles: true, cancelable: true })));
    await waitFor(() => expect(onSave).toHaveBeenCalledTimes(1));
    expect(onSave).toHaveBeenCalledWith(expect.objectContaining({ scope: " Payments:Production " }));
  });

  it.each(["create", "rotate"] as const)("masks by default, toggles without submitting, and resets when reopening %s", mode => {
    const { container, onSave, render } = renderDialog(mode);
    const input = container.querySelector<HTMLInputElement>('input[data-secret-field="value"]')!;
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
    expect(container.querySelector<HTMLInputElement>('input[data-secret-field="value"]')?.type).toBe("password");
  });

  it("remasks when the selected secret type changes without losing entered text", () => {
    const { container } = renderDialog("create");
    change(container.querySelector<HTMLInputElement>('input[data-secret-field="value"]')!, "dummy-pasted-value");
    flushSync(() => container.querySelector<HTMLButtonElement>("button[aria-label='Show value']")!.click());
    selectOption(fieldControl<HTMLSelectElement>(container, "Type"), "json");
    const input = container.querySelector<HTMLInputElement>('input[data-secret-field="value"]')!;
    expect(input.type).toBe("password");
    expect(input.value).toBe("dummy-pasted-value");
  });
});

describe("Create dialog regression contract", () => {
  it("preserves technical name casing on blur and submit while trimming whitespace", () => {
    const { container, onSave } = renderDialog("create");
    const name = fieldControl<HTMLInputElement>(container, "Technical name");
    change(name, " DbConnectionString ");
    flushSync(() => name.dispatchEvent(new FocusEvent("focusout", { bubbles: true })));
    expect(name.value).toBe("DbConnectionString");
    change(fieldControl<HTMLInputElement>(container, "Value"), "new-secret-value");
    expect(container.querySelector<HTMLButtonElement>("button[type='submit']")!.disabled).toBe(false);
    flushSync(() => container.querySelector("form")!.dispatchEvent(new Event("submit", { bubbles: true, cancelable: true })));
    expect(onSave).toHaveBeenCalledWith(expect.objectContaining({ name: "DbConnectionString", displayName: "DbConnectionString" }));
  });

  it("disables field editing while pending and restores the draft after a failed save", async () => {
    let rejectSave!: (error: Error) => void;
    const { container, onSave } = renderDialog("create");
    onSave.mockImplementation(() => new Promise<void>((_resolve, reject) => { rejectSave = reject; }));
    const name = fieldControl<HTMLInputElement>(container, "Technical name");
    const value = fieldControl<HTMLInputElement>(container, "Value");
    change(name, "DbConnectionString");
    change(value, "demonstration-value");
    flushSync(() => container.querySelector("form")!.dispatchEvent(new Event("submit", { bubbles: true, cancelable: true })));
    expect(onSave).toHaveBeenCalledWith(expect.objectContaining({ name: "DbConnectionString", displayName: "DbConnectionString" }));
    expect(name.disabled).toBe(true);
    expect(value.disabled).toBe(true);
    rejectSave(new Error("Demonstration failure"));
    await waitFor(() => {
      const alert = container.querySelector("[role='alert']");
      expect(alert?.textContent).toBe("Demonstration failure");
      expect(document.activeElement).toBe(alert);
      expect(name.disabled).toBe(false);
      expect(value.disabled).toBe(false);
    });
    expect(name.value).toBe("DbConnectionString");
    expect(value.value).toBe("demonstration-value");
  });

  it("keeps a non-text type's description with Configuration key", () => {
    const described: SecretDescriptorsResponse = {
      ...configurableDescriptors,
      types: [{ name: "json", displayName: "JSON", supportedStoreNames: ["configuration", "encrypted"], description: "Fixture type description." }]
    };
    const { container } = renderDialog("create", { descriptors: described });
    const key = fieldControl<HTMLInputElement>(container, "Configuration key");
    expect(document.getElementById(key.getAttribute("aria-describedby")!)?.textContent).toBe("Fixture type description.");
  });

  it("uses descriptor defaults, hides a sole Store and focuses missing Value before sending the exact request", () => {
    const fetch = vi.fn(); vi.stubGlobal("fetch", fetch);
    const { container, onSave } = renderDialog("create");
    expect(fieldControl<HTMLSelectElement>(container, "Type").value).toBe("text");
    expect([...container.querySelectorAll("label")].some(label => directLabelText(label) === "Store")).toBe(false);
    submit(container); expect(onSave).not.toHaveBeenCalled();
    expect(document.activeElement).toBe(fieldControl(container, "Value"));
    change(fieldControl(container, "Value"), "authored-value"); submit(container);
    expect(onSave).toHaveBeenCalledWith({ name: "test-key", displayName: "test-key", description: undefined, typeName: "text", storeName: "encrypted", scope: undefined, tags: [], value: "authored-value", configurationKey: undefined, expiresAt: undefined });
    expect(fetch).not.toHaveBeenCalled();
  });

  it("retains native label and invalid-help associations when validation reveals another tab", () => {
    const { container, onSave } = renderDialog("create");
    const name = fieldControl<HTMLInputElement>(container, "Technical name");
    expect(name.labels?.[0]?.control).toBe(name);
    change(name, "has spaces");
    expect(name.getAttribute("aria-invalid")).toBe("true");
    const help = document.getElementById(name.getAttribute("aria-describedby")!);
    expect(help?.textContent).toContain("Use letters, numbers");
    flushSync(() => editorTab(container, "Details").click());
    submit(container);
    expect(editorTab(container, "Secret").getAttribute("aria-selected")).toBe("true");
    expect(document.activeElement).toBe(name);
    expect(onSave).not.toHaveBeenCalled();
    change(name, "DbConnectionString");
    expect(name.hasAttribute("aria-invalid")).toBe(false);
    expect(document.getElementById(name.getAttribute("aria-describedby")!)?.textContent).toContain("Casing is preserved");
  });

  it("opens the store recovery section when a type has no compatible provider", () => {
    const { container, onSave } = renderDialog("create", { descriptors: unavailableStoreDescriptors });
    change(fieldControl(container, "Value"), "authored-value"); selectOption(fieldControl(container, "Type"), "JSON"); submit(container);
    expect(onSave).not.toHaveBeenCalled();
    expect(editorTab(container, "Storage & lifetime").getAttribute("aria-selected")).toBe("true");
    expect(document.activeElement?.textContent).toContain("No compatible secret store");
    flushSync(() => editorTab(container, "Secret").click()); selectOption(fieldControl(container, "Type"), "Text"); submit(container);
    expect(onSave).toHaveBeenCalledWith(expect.objectContaining({ typeName: "text", storeName: "encrypted", value: "authored-value" }));
  });

  it("recovers a retained store replaced by a sole compatible descriptor without losing entered material", () => {
    const { container, render, onSave } = renderDialog("create");
    change(fieldControl(container, "Value"), "retained-material");
    const replacement: SecretDescriptorsResponse = { types: [{ name: "text", displayName: "Text", supportedStoreNames: ["configuration"] }], stores: [{ name: "configuration", displayName: "Configuration", isReadOnly: true }] };
    render("create", { descriptors: replacement });
    flushSync(() => editorTab(container, "Storage & lifetime").click());
    const store = fieldControl<HTMLSelectElement>(container, "Store");
    expect(store.value).toBe("encrypted");
    expect(store.selectedOptions[0].textContent).toContain("unavailable");
    selectOption(store, "Configuration"); submit(container);
    expect(onSave).not.toHaveBeenCalled();
    expect(editorTab(container, "Secret").getAttribute("aria-selected")).toBe("true");
    change(fieldControl(container, "Configuration key"), "Config:Primary");
    render("create", { descriptors });
    selectOption(fieldControl(container, "Store"), "Encrypted");
    expect(fieldControl<HTMLInputElement>(container, "Value").value).toBe("retained-material");
    submit(container);
    expect(onSave).toHaveBeenCalledWith(expect.objectContaining({ value: "retained-material", configurationKey: undefined, storeName: "encrypted" }));
  });

  it("validates Configuration key in Secret and sends no hidden Value", () => {
    const { container, onSave } = renderDialog("create", { descriptors: configurableDescriptors });
    expect(fieldControl<HTMLSelectElement>(container, "Store").value).toBe("configuration");
    flushSync(() => editorTab(container, "Storage & lifetime").click()); submit(container);
    expect(editorTab(container, "Secret").getAttribute("aria-selected")).toBe("true");
    expect(document.activeElement).toBe(fieldControl(container, "Configuration key"));
    expect(onSave).not.toHaveBeenCalled();
    change(fieldControl(container, "Configuration key"), "  Payments:Primary  "); submit(container);
    expect(onSave).toHaveBeenCalledWith(expect.objectContaining({ configurationKey: "Payments:Primary", value: undefined, storeName: "configuration" }));
  });

  it("uses named mounted panels with contextual guidance and no nested disclosure or redundant type/store prose", () => {
    const { container } = renderDialog("create", { descriptors: scopeDescriptors });
    expect([...container.querySelectorAll("[role=tab]")].map(t => t.textContent)).toEqual(["Secret", "Details", "Storage & lifetime"]);
    expect(container.textContent).not.toContain("More options");
    expect(container.textContent).not.toContain("Fixture type description.");
    expect(container.textContent).not.toContain("Fixture store description.");
    const details = panelFor(container, "Details"); expect(details.hidden).toBe(true);
    flushSync(() => editorTab(container, "Details").click());
    expect(details.hidden).toBe(false);
    change(fieldControl(container, "Display name"), "Private title");
    expect(editorTab(container, "Details").textContent).toBe("Details (1)");
    expect(editorTab(container, "Details").textContent).not.toContain("Private title");
    flushSync(() => editorTab(container, "Storage & lifetime").click());
    const scope = fieldControl<HTMLSelectElement>(container, "Scope");
    const descriptionIds = scope.getAttribute("aria-describedby")!.split(/\s+/);
    expect(descriptionIds).toHaveLength(2);
    expect(new Set(descriptionIds).size).toBe(2);
    for (const id of descriptionIds) expect(document.getElementById(id)?.isConnected).toBe(true);
    const hints = descriptionIds.map(id => document.getElementById(id)?.textContent).join(" ");
    expect(hints).toContain("Scope does not grant access.");
    expect(panelFor(container, "Storage & lifetime").contains(document.getElementById(descriptionIds[0]))).toBe(true);
    expect([...container.querySelectorAll("p")].filter(p => p.textContent?.includes("Scope does not grant access."))).toHaveLength(1);
    selectOption(scope, "Custom…");
    const custom = fieldControl<HTMLInputElement>(container, "Custom scope value");
    expect(custom.getAttribute("aria-describedby")).toBe(scope.getAttribute("aria-describedby"));
    change(custom, " Team:Production ");
    expect(custom.value).toBe(" Team:Production ");
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

  it("preserves all authored values through tab navigation without copying them into labels", () => {
    const { container } = renderDialog("create", { descriptors: switchableDescriptors });
    change(fieldControl(container, "Value"), "material");
    flushSync(() => editorTab(container, "Details").click());
    change(fieldControl(container, "Display name"), "Private title"); change(fieldControl(container, "Description"), "  description  "); change(fieldControl(container, "Tags"), "api, billing");
    expect(editorTab(container, "Details").textContent).toBe("Details (3)");
    flushSync(() => editorTab(container, "Storage & lifetime").click());
    selectOption(fieldControl(container, "Scope"), "Custom…"); change(fieldControl(container, "Custom scope value"), " Team:Production ");
    change(fieldControl(container, "Expires"), "2030-01-02T03:04");
    flushSync(() => editorTab(container, "Secret").click());
    expect(fieldControl<HTMLInputElement>(container, "Value").value).toBe("material");
    flushSync(() => editorTab(container, "Details").click());
    expect(fieldControl<HTMLInputElement>(container, "Display name").value).toBe("Private title");
    expect(fieldControl<HTMLTextAreaElement>(container, "Description").value).toBe("  description  ");
    expect(fieldControl<HTMLInputElement>(container, "Tags").value).toBe("api, billing");
    expect(fieldControl<HTMLInputElement>(container, "Custom scope value").value).toBe(" Team:Production ");
    expect([...container.querySelectorAll("[role=tab]")].map(t => t.textContent).join(" ")).not.toContain("Private title");
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
      name: "PAYMENTS.Api",
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

  it("keeps focused custom scope and material through descriptor failure and recovery", () => {
    const { container, render } = renderDialog("create", { descriptors: scopeDescriptors });
    change(fieldControl(container, "Value"), "material"); flushSync(() => editorTab(container, "Storage & lifetime").click());
    selectOption(fieldControl(container, "Scope"), "Custom…"); const scope = fieldControl<HTMLInputElement>(container, "Custom scope value"); change(scope, " Team:Production "); scope.focus();
    render("create", { descriptors: scopeDescriptors, scopeCatalogError: true });
    expect(document.activeElement).toBe(scope); expect(scope.value).toBe(" Team:Production ");
    expect(container.textContent).toContain("Scope suggestions could not be refreshed");
    render("create", { descriptors: scopeDescriptors, scopeCatalogError: false });
    expect(document.activeElement).toBe(scope); expect(fieldControl<HTMLInputElement>(container, "Value").value).toBe("material");
  });

  it("can navigate to advisory option recovery and return without discarding the secret", () => {
    const retry = vi.fn(); const { container, render } = renderDialog("create", { descriptors: scopeDescriptors, onRetryScopeCatalog: retry });
    const value = fieldControl<HTMLInputElement>(container, "Value");
    change(value, "material"); value.focus();
    render("create", { descriptors: scopeDescriptors, scopeCatalogError: true, onRetryScopeCatalog: retry });
    expect(document.activeElement).toBe(value);
    expect(editorTab(container, "Secret").getAttribute("aria-selected")).toBe("true");
    expect(editorTab(container, "Storage & lifetime").textContent).toContain("needs attention");
    flushSync(() => editorTab(container, "Storage & lifetime").click());
    expect(panelFor(container, "Storage & lifetime").hidden).toBe(false);
    const button = [...container.querySelectorAll<HTMLButtonElement>("button")].find(b => b.textContent === "Retry secret options")!; flushSync(() => button.click());
    expect(retry).toHaveBeenCalledOnce();
    render("create", { descriptors: scopeDescriptors, scopeCatalogError: false, onRetryScopeCatalog: retry });
    expect(editorTab(container, "Storage & lifetime").textContent).not.toContain("needs attention");
    flushSync(() => editorTab(container, "Secret").click());
    expect(fieldControl<HTMLInputElement>(container, "Value").value).toBe("material");
  });

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
    const input = dialog.querySelector<HTMLInputElement>(mode === "create" ? "input" : 'input[data-secret-field="value"]')!;
    change(input, mode === "create" ? "test-key" : "replacement-value");
    if (mode === "create") change(dialog.querySelectorAll<HTMLInputElement>('input[data-secret-field="value"]')[0], "replacement-value");
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
    const content = dialog.querySelector<HTMLElement>(".secrets-editor-fields")!;
    const actions = dialog.querySelector<HTMLElement>(".secrets-create-actions")!;
    expect(content.contains(actions)).toBe(false);
    expect(actions.parentElement).toBe(dialog.querySelector("form"));

    const technicalName = dialog.querySelector<HTMLInputElement>("input")!;
    const value = dialog.querySelector<HTMLInputElement>('input[data-secret-field="value"]')!;
    change(technicalName, "preserved.secret");
    change(value, "preserved-secret-value");
    flushSync(() => dialog.querySelector("form")!.dispatchEvent(new Event("submit", { bubbles: true, cancelable: true })));

    await waitFor(() => expect(onSave).toHaveBeenCalledTimes(1));
    await waitFor(() => expect(dialog.querySelector("[role='alert']")).not.toBeNull());
    const alert = dialog.querySelector<HTMLElement>("[role='alert']")!;
    await waitFor(() => expect(document.activeElement).toBe(alert));
    expect(alert.tabIndex).toBe(-1);
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

function editorTab(container: ParentNode, label: string) {
  const tab = [...container.querySelectorAll<HTMLButtonElement>("[role=tab]")].find(t => t.textContent?.startsWith(label));
  if (!tab) throw new Error(`Tab not found: ${label}`); return tab;
}
function panelFor(container: ParentNode, label: string) {
  return document.getElementById(editorTab(container, label).getAttribute("aria-controls")!)!;
}
function submit(container: ParentNode) {
  flushSync(() => container.querySelector("form")!.dispatchEvent(new Event("submit", { bubbles: true, cancelable: true })));
}

function revealFieldIfHidden(container: HTMLElement, field: HTMLElement) {
  const body = field.closest<HTMLElement>("[hidden]");
  if (!body) return;
  const button = [...container.querySelectorAll<HTMLButtonElement>("button")].find(candidate => candidate.getAttribute("aria-controls") === body.id);
  if (!button) throw new Error("Hidden field has no disclosure control.");
  flushSync(() => button.click());
}

function selectOption(select: HTMLSelectElement, label: string) {
  const option = [...select.options].find(item => item.textContent === label);
  if (!option) throw new Error(`Option not found: ${label}`);
  flushSync(() => {
    Object.getOwnPropertyDescriptor(HTMLSelectElement.prototype, "value")!.set!.call(select, option.value);
    select.dispatchEvent(new Event("change", { bubbles: true }));
  });
}
