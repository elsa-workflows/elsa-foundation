import React from "react";
import { flushSync } from "react-dom";
import { createRoot } from "react-dom/client";
import { afterEach, describe, expect, it, vi } from "vitest";
import type { StudioEndpointContext } from "@elsa-workflows/studio-sdk";
import { SecretsPage } from "../SecretsPage";

const cleanup = new Set<() => void>();
const clipboardDescriptor = Object.getOwnPropertyDescriptor(navigator, "clipboard");
afterEach(() => {
  for (const dispose of cleanup) dispose();
  if (clipboardDescriptor) Object.defineProperty(navigator, "clipboard", clipboardDescriptor);
  else Reflect.deleteProperty(navigator, "clipboard");
});

describe("secrets page", () => {
  it("opens details only on request, cancels edits and restores focus to the list", async () => {
    const putJson = vi.fn();
    const { container } = render({ putJson });
    await waitForText(container, "Payments API");
    expect(container.querySelector(".secrets-detail")).toBeNull();
    expect(container.querySelector("[role=dialog]")).toBeNull();
    const trigger = openEditor(container);
    const editor = container.querySelector<HTMLElement>("[role=dialog]")!;
    expect(editor.getAttribute("aria-modal")).toBe("true");
    expect(editor.contains(document.activeElement)).toBe(true);
    change(editor.querySelector<HTMLInputElement>(".secrets-edit-form input")!, "Unsaved edit");
    flushSync(() => editor.dispatchEvent(new KeyboardEvent("keydown", { key: "Escape", bubbles: true })));
    expect(container.querySelector("[role=dialog]")).toBeNull();
    expect(document.activeElement).toBe(trigger);
    expect(putJson).not.toHaveBeenCalled();
    openEditor(container);
    expect(container.querySelector<HTMLInputElement>(".secrets-edit-form input")?.value).toBe("Payments API");
  });

  it("keeps keyboard focus inside the editor and closes through Cancel", async () => {
    const { container } = render();
    await waitForText(container, "Payments API");
    const trigger = openEditor(container);
    const editor = container.querySelector<HTMLElement>("[role=dialog]")!;
    const buttons = [...editor.querySelectorAll<HTMLButtonElement>("button:not(:disabled)")];
    const first = buttons[0];
    const last = buttons[buttons.length - 1];
    first.focus();
    first.dispatchEvent(new KeyboardEvent("keydown", { key: "Tab", shiftKey: true, bubbles: true, cancelable: true }));
    expect(document.activeElement).toBe(last);
    last.dispatchEvent(new KeyboardEvent("keydown", { key: "Tab", bubbles: true, cancelable: true }));
    expect(document.activeElement).toBe(first);
    clickButton(container, "Cancel", "[role=dialog]");
    await waitFor(() => !container.querySelector("[role=dialog]"));
    expect(document.activeElement).toBe(trigger);
  });

  it("saves metadata and returns to the updated list", async () => {
    let current = paymentSecret();
    const putJson = vi.fn(async (_url: string, request: { displayName: string; description: string }) => {
      current = paymentSecret(request);
      return current;
    });
    const { container } = render({ getItems: () => [current], putJson });
    await waitForText(container, "Payments API");
    openEditor(container);
    flushSync(() => change(container.querySelector<HTMLInputElement>(".secrets-edit-form input")!, "Updated payments"));
    clickButton(container, "Save changes");
    await waitForText(container, "Secret metadata updated.");
    expect(container.querySelector("[role=dialog]")).toBeNull();
    expect(container.querySelector("table")?.textContent).toContain("Updated payments");
    expect(document.activeElement?.getAttribute("data-secret-edit")).toBe("payments.api");
    expect(putJson).toHaveBeenCalledWith("/secrets/payments.api", { displayName: "Updated payments", description: "Token for payment provider" });
  });

  it("keeps Scope read-only and tucks secondary metadata behind native details", async () => {
    const { container } = render({ getItems: () => [paymentSecret({
      scope: "module.payments",
      tags: ["billing"],
      updatedAt: "2026-06-25T00:00:00Z",
      expiresAt: "2026-12-31T00:00:00Z"
    })] });
    await waitForText(container, "Payments API");
    openEditor(container);
    const dialog = container.querySelector<HTMLElement>("[role='dialog']")!;

    expect(dialog.querySelector("h3")?.textContent).toBe("Edit secret");
    expect(dialog.querySelector<HTMLInputElement>("[data-dialog-initial-focus]")?.value).toBe("Payments API");
    expect(document.activeElement?.getAttribute("data-dialog-initial-focus")).not.toBeNull();
    const description = dialog.querySelector<HTMLTextAreaElement>(".secrets-edit-form textarea")!;
    expect(description.id).toBeTruthy();
    expect(description.labels?.[0]?.htmlFor).toBe(description.id);
    expect(description.labels?.[0]?.textContent).toBe("Description");
    expect(description.labels?.[0]?.contains(description)).toBe(false);
    const scopeInput = dialog.querySelector<HTMLInputElement>(".secrets-editor-scope input")!;
    expect(scopeInput.labels?.[0]?.textContent).toBe("Scope");
    expect(scopeInput.readOnly).toBe(true);
    expect(scopeInput.value).toBe("module.payments");
    const scopeHelp = dialog.querySelector(`#${scopeInput.getAttribute("aria-describedby")}`)!;
    expect(scopeHelp.textContent).toContain("match it (ignoring case)");
    expect(scopeHelp.textContent).toContain("Scope does not grant access.");
    expect(scopeHelp.textContent).toContain("Scope is read-only in this editor.");

    const disclosure = dialog.querySelector<HTMLDetailsElement>("details")!;
    expect(disclosure.open).toBe(false);
    expect(disclosure.querySelector("summary")?.textContent).toBe("Details");
    flushSync(() => disclosure.querySelector("summary")?.dispatchEvent(new MouseEvent("click", { bubbles: true })));
    expect(disclosure.open).toBe(true);
    expect(disclosure.textContent).toContain("Created");
    expect(disclosure.textContent).toContain("Updated");
    expect(disclosure.textContent).toContain("Store capability");
    expect(dialog.querySelector(".secrets-tags")?.textContent).toContain("billing");
    expect([...dialog.querySelectorAll(".secrets-dialog-actions button")].map(button => button.textContent).join(" ")).toContain("Save changes");
  });

  it("waits for a pending save before allowing dismissal", async () => {
    let finishSave!: (value: unknown) => void;
    const putJson = vi.fn(() => new Promise(resolve => { finishSave = resolve; }));
    const { container } = render({ putJson });
    await waitForText(container, "Payments API");
    openEditor(container);
    flushSync(() => change(container.querySelector<HTMLInputElement>(".secrets-edit-form input")!, "Pending save"));
    clickButton(container, "Save changes");
    await waitFor(() => putJson.mock.calls.length === 1);
    const editor = container.querySelector<HTMLElement>("[role=dialog]")!;
    flushSync(() => editor.dispatchEvent(new KeyboardEvent("keydown", { key: "Escape", bubbles: true })));
    expect(container.querySelector("[role=dialog]")).toBe(editor);
    expect(editor.querySelector<HTMLButtonElement>("button[aria-label='Close editor']")?.disabled).toBe(true);
    finishSave(paymentSecret());
    await waitForText(container, "Secret metadata updated.");
    expect(container.querySelector("[role=dialog]")).toBeNull();
  });

  it("keeps failed metadata edits in the dialog for retry", async () => {
    const putJson = vi.fn().mockRejectedValueOnce(new Error("Metadata save unavailable.")).mockResolvedValue(paymentSecret());
    const { container } = render({ putJson });
    await waitForText(container, "Payments API");
    openEditor(container);
    flushSync(() => change(container.querySelector<HTMLInputElement>(".secrets-edit-form input")!, "Retry payments"));
    clickButton(container, "Save changes");
    await waitForText(container, "Metadata save unavailable.");
    const editor = container.querySelector<HTMLElement>("[role=dialog]")!;
    expect(editor).not.toBeNull();
    expect(editor.querySelector("[role=alert]")?.textContent).toContain("Metadata save unavailable.");
    expect(editor.querySelector<HTMLInputElement>(".secrets-edit-form input")?.value).toBe("Retry payments");
    clickButton(container, "Save changes");
    await waitForText(container, "Secret metadata updated.");
    expect(container.querySelector("[role=dialog]")).toBeNull();
    expect(putJson).toHaveBeenCalledTimes(2);
  });

  it.each([false, true])("dismisses operation notifications and shows later results (error=%s)", async error => {
    const postJson = error
      ? vi.fn().mockRejectedValue(new Error("Test unavailable."))
      : vi.fn().mockResolvedValue({ succeeded: true, code: "resolved", message: "Secret resolved successfully." });
    const { container } = render({ postJson });
    await waitForText(container, "Payments API");
    const runTest = () => container.querySelector<HTMLButtonElement>("button[aria-label='Test payments.api']")!.click();
    runTest();
    const message = error ? "Test unavailable." : "Secret resolved successfully.";
    await waitForText(container, message);
    const notice = container.querySelector<HTMLElement>(".secrets-notification")!;
    const messageRegion = notice.querySelector("[role]")!;
    expect(messageRegion.getAttribute("role")).toBe(error ? "alert" : "status");
    expect(messageRegion.querySelector("button")).toBeNull();
    const dismiss = notice.querySelector<HTMLButtonElement>("button")!;
    expect(dismiss.getAttribute("aria-label")).toBe(error ? "Dismiss error" : "Dismiss notification");
    flushSync(() => dismiss.click());
    expect(container.querySelector(".secrets-notification")).toBeNull();
    expect(container.textContent).toContain("Payments API");
    runTest();
    await waitForText(container, message);
    expect(container.querySelector(".secrets-notification")).not.toBeNull();
    expect(postJson).toHaveBeenCalledTimes(2);
  });

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

  it("keeps an open create form and loaded secrets when descriptor refresh fails, then retries in place", async () => {
    let descriptorRequests = 0;
    const descriptors = () => {
      descriptorRequests++;
      if (descriptorRequests === 2) throw new Error("private catalog failure");
      return {
        types: [{ name: "text", displayName: "Text", description: "", editorHint: "text", supportedStoreNames: ["encrypted"] }],
        stores: [{ name: "encrypted", displayName: "Encrypted", description: "", capabilities: 1, isReadOnly: false }],
        scopes: [{ name: "module.payments", displayName: "Payments module", description: "Payment credentials", source: "Payments module" }]
      };
    };
    const { container } = render({ getDescriptors: descriptors });
    await waitForText(container, "Payments API");
    await waitFor(() => !container.querySelector<HTMLButtonElement>(".secrets-toolbar-actions button:last-child")?.disabled);
    clickButton(container, "Create");
    await waitFor(() => !!container.querySelector("[aria-label='Create secret']"));
    const dialog = container.querySelector<HTMLElement>("[aria-label='Create secret']")!;
    change(dialog.querySelector<HTMLInputElement>("input")!, "kept-secret");
    change(dialog.querySelector<HTMLTextAreaElement>("textarea")!, "Keep this description during refresh.");
    change(dialog.querySelector<HTMLInputElement>(".secrets-value-input input")!, "kept-value");
    selectValue(dialog.querySelector<HTMLSelectElement>("select[id^='scope-select-']")!, "module.payments");

    clickButton(dialog, "Refresh scope suggestions");
    await waitForText(container, "Scope suggestions could not be refreshed.");
    expect(container.textContent).toContain("Payments API");
    expect(dialog.querySelector<HTMLInputElement>("input")?.value).toBe("kept-secret");
    expect(dialog.querySelector<HTMLTextAreaElement>("textarea")?.value).toBe("Keep this description during refresh.");
    expect(dialog.querySelector<HTMLInputElement>(".secrets-value-input input")?.value).toBe("kept-value");
    expect(dialog.querySelector<HTMLSelectElement>("select[id^='scope-select-']")?.value).toBe("module.payments");
    expect(container.textContent).not.toContain("private catalog failure");

    clickButton(dialog, "Retry scope suggestions");
    await waitFor(() => descriptorRequests === 3 && !dialog.querySelector("[role='alert']"));
    expect(dialog.querySelector<HTMLInputElement>("input")?.value).toBe("kept-secret");
    expect(dialog.querySelector<HTMLTextAreaElement>("textarea")?.value).toBe("Keep this description during refresh.");
    expect(dialog.querySelector<HTMLInputElement>(".secrets-value-input input")?.value).toBe("kept-value");
    expect(dialog.querySelector<HTMLSelectElement>("select[id^='scope-select-']")?.value).toBe("module.payments");
  });

  it("ignores an older descriptor failure after the endpoint changes and a newer request succeeds", async () => {
    let endpoint = "old";
    let obsoleteRequests = 0;
    let finishObsolete!: (reason: Error) => void;
    const obsoleteResponse = new Promise((_resolve, reject) => { finishObsolete = reject; });
    const { container, changeEndpoint } = render({ getDescriptors: () => {
      if (endpoint === "old") {
        obsoleteRequests++;
        return obsoleteResponse;
      }
      return {
        types: [{ name: "text", displayName: "Text", description: "", editorHint: "text", supportedStoreNames: ["encrypted"] }],
        stores: [{ name: "encrypted", displayName: "Encrypted", description: "", capabilities: 1, isReadOnly: false }],
        scopes: [{ name: "module.current", displayName: "Current module scope" }]
      };
    } });
    await waitFor(() => obsoleteRequests === 1);

    endpoint = "new";
    changeEndpoint("/new-endpoint");
    await waitFor(() => !container.querySelector<HTMLButtonElement>(".secrets-toolbar-actions button:last-child")?.disabled);
    clickButton(container, "Create");
    await waitFor(() => !!container.querySelector("[aria-label='Create secret'] select[id^='scope-select-']"));
    const scopeSelect = container.querySelector<HTMLSelectElement>("[aria-label='Create secret'] select[id^='scope-select-']")!;
    await waitFor(() => [...scopeSelect.options].some(option => option.value === "module.current"));

    finishObsolete(new Error("old endpoint failed"));
    await new Promise(resolve => setTimeout(resolve, 0));
    expect(container.querySelector("[role='alert']")).toBeNull();
    expect([...scopeSelect.options].map(option => option.value)).toContain("module.current");
  });

  it("disables the page-level descriptor retry while its request is pending", async () => {
    let descriptorRequests = 0;
    let finishRetry!: (value: unknown) => void;
    const pendingRetry = new Promise(resolve => { finishRetry = resolve; });
    const { container } = render({ getDescriptors: () => {
      descriptorRequests++;
      if (descriptorRequests === 1) throw new Error("initial catalog failure");
      return pendingRetry;
    } });
    await waitFor(() => !!container.querySelector<HTMLButtonElement>("button") && container.textContent?.includes("Retry secret options"));
    const retry = [...container.querySelectorAll<HTMLButtonElement>("button")].find(button => button.textContent?.includes("Retry secret options"))!;

    retry.click();
    await waitFor(() => descriptorRequests === 2);
    expect(retry.disabled).toBe(true);
    finishRetry({
      types: [{ name: "text", displayName: "Text", description: "", editorHint: "text", supportedStoreNames: ["encrypted"] }],
      stores: [{ name: "encrypted", displayName: "Encrypted", description: "", capabilities: 1, isReadOnly: false }]
    });
    await waitFor(() => !container.textContent?.includes("Retry secret options"));
    expect(container.textContent).not.toContain("Secret options could not be refreshed.");
  });

  it("shows a filtered empty state and returns to the list when filters are cleared", async () => {
    const { container } = render();
    await waitForText(container, "Payments API");
    const status = [...container.querySelectorAll<HTMLSelectElement>(".secrets-filter select")]
      .find(select => select.parentElement?.textContent?.includes("Status"))!;
    selectValue(status, "Revoked");
    await waitForText(container, "No loaded secrets match your filters");
    expect(container.querySelector(".secrets-detail")).toBeNull();
    selectValue(status, "");
    await waitForText(container, "Payments API");
    expect(container.querySelector(".secrets-detail")).toBeNull();
  });

  it("distinguishes loaded coverage from the completed search total", async () => {
    const { container } = render({
      getItems: () => Array.from({ length: 100 }, (_, index) => secret({
        id: String(index + 1),
        name: `inventory.secret.${index + 1}`,
        displayName: `Inventory secret ${index + 1}`,
        scope: `scope.${index + 1}`
      })),
      totalCount: 101
    });

    await waitForText(container, "Inventory secret 1");

    expect(container.querySelector(".secrets-count")?.textContent).toContain("100 shown · 100 loaded · 101 results");
    expect(container.textContent).toContain("name or display name");

    const search = container.querySelector<HTMLInputElement>("input[aria-label='Search secrets']")!;
    change(search, "confirmed-query");
    clickButton(container, "Search");
    await waitFor(() => Boolean(container.querySelector(".secrets-count")?.textContent?.includes("for “confirmed-query”")));
    const acceptedCount = container.querySelector(".secrets-count")?.textContent;
    change(container.querySelector<HTMLInputElement>("input[aria-label='Search secrets']")!, "unsubmitted-name");
    expect(container.querySelector(".secrets-count")?.textContent).toBe(acceptedCount);
    expect(container.querySelector(".secrets-count")?.textContent).not.toContain("unsubmitted-name");

    const scope = [...container.querySelectorAll<HTMLSelectElement>(".secrets-filter select")]
      .find(select => select.labels?.[0]?.textContent === "Scope")!;
    selectValue(scope, "scope.1");
    expect(container.querySelector(".secrets-count")?.textContent).toContain("1 shown · 100 loaded · 101 results for “confirmed-query”");
  });

  it("does not label a complete full page as partial", async () => {
    const { container } = render({
      getItems: () => Array.from({ length: 100 }, (_, index) => secret({
        id: String(index + 1),
        name: `complete.secret.${index + 1}`,
        displayName: `Complete secret ${index + 1}`
      })),
      totalCount: 100
    });

    await waitForText(container, "Complete secret 1");

    expect(container.querySelector(".secrets-count")?.textContent).toContain("100 shown · 100 loaded · 100 results");
    expect(container.querySelector(".secrets-coverage")).toBeNull();
  });

  it("qualifies a local-facet empty state to the loaded secrets", async () => {
    const { container } = render({ getItems: () => [paymentSecret()], totalCount: 101 });
    await waitForText(container, "Payments API");

    const status = [...container.querySelectorAll<HTMLSelectElement>(".secrets-filter select")]
      .find(select => select.parentElement?.textContent?.includes("Status"))!;
    selectValue(status, "Revoked");
    await waitForText(container, "No loaded secrets match your filters");

    expect(container.textContent).toContain("No loaded secrets match your filters");
  });

  it("keeps empty-result wording tied to the completed search while text is only a draft", async () => {
    const { container } = render({ getItems: () => [] });
    await waitForText(container, "No secrets yet");

    change(container.querySelector<HTMLInputElement>("input[aria-label='Search secrets']")!, "not-submitted");

    expect(container.textContent).toContain("No secrets yet");
    expect(container.textContent).not.toContain("No secrets match your filters");
  });

  it("keeps a complete empty search distinct from the initial empty collection", async () => {
    const { container } = render({ getItems: () => [] });
    await waitForText(container, "No secrets yet");
    const search = container.querySelector<HTMLInputElement>("input[aria-label='Search secrets']")!;
    change(search, "missing-secret");
    clickButton(container, "Search");
    await waitForText(container, "No results for “missing-secret”");

    expect(container.querySelector(".secrets-empty-state button")?.textContent).toContain("Clear filters");
    expect(container.querySelector(".secrets-coverage")).toBeNull();
  });

  it("does not let a slower earlier search replace the latest result", async () => {
    let finishEarlier!: () => void;
    let earlierFinished!: () => void;
    const earlierResponseFinished = new Promise<void>(resolve => { earlierFinished = resolve; });
    const { container, listRequests } = render({
      getItems: url => {
        const term = getSearchTerm(url);
        if (term === "earlier") {
          return new Promise(resolve => {
            finishEarlier = () => {
              resolve([secret({ id: "2", name: "earlier.secret", displayName: "Earlier result" })]);
              earlierFinished();
            };
          });
        }
        return term === "latest"
          ? [secret({ id: "3", name: "latest.secret", displayName: "Latest result" })]
          : [paymentSecret()];
      },
      getTotalCount: (_items, url) => getSearchTerm(url) === "earlier" ? 2 : 1
    });
    await waitForText(container, "Payments API");
    const search = container.querySelector<HTMLInputElement>("input[aria-label='Search secrets']")!;

    change(search, "earlier");
    clickButton(container, "Search");
    await waitFor(() => !!finishEarlier);
    expect(new URL(listRequests.at(-1)!, "https://fixture.example").searchParams.get("search")).toBe("earlier");

    change(search, "latest");
    clickButton(container, "Search");
    await waitForText(container, "Latest result");
    finishEarlier();
    await earlierResponseFinished;
    await new Promise(resolve => setTimeout(resolve, 0));

    expect(container.querySelector("[data-secret-open='latest.secret']")).not.toBeNull();
    expect(container.querySelector("[data-secret-open='earlier.secret']")).toBeNull();
    expect(container.querySelector(".secrets-count")?.textContent).toContain("1");
  });

  it("ignores a pending result after its endpoint context changes", async () => {
    let finishOldContext!: () => void;
    let oldContextFinished!: () => void;
    const oldContextResponseFinished = new Promise<void>(resolve => { oldContextFinished = resolve; });
    const { container, changeEndpoint } = render({
      getItems: (_url, baseUrl) => {
        if (!baseUrl) {
          return new Promise(resolve => {
            finishOldContext = () => {
              resolve([secret({ id: "old", name: "old.tenant", displayName: "Old context result" })]);
              oldContextFinished();
            };
          });
        }
        return [secret({ id: "new", name: "new.tenant", displayName: "Current context result" })];
      }
    });
    await waitFor(() => !!finishOldContext);

    changeEndpoint("https://other-context.example");
    await waitForText(container, "Current context result");
    finishOldContext();
    await oldContextResponseFinished;
    await new Promise(resolve => setTimeout(resolve, 0));

    expect(container.querySelector("[data-secret-open='new.tenant']")).not.toBeNull();
    expect(container.querySelector("[data-secret-open='old.tenant']")).toBeNull();
  });

  it("explains that scope filter options come from loaded secrets", async () => {
    const { container } = render({ getItems: () => [paymentSecret({ scope: "module.payments" })] });
    await waitForText(container, "Payments API");
    const scopeFilter = [...container.querySelectorAll<HTMLSelectElement>(".secrets-filter select")]
      .find(select => select.labels?.[0]?.textContent === "Scope")!;

    expect([...scopeFilter.options].map(option => option.value)).toContain("module.payments");
    expect(scopeFilter.getAttribute("aria-describedby")).toContain("help");
    expect(scopeFilter.labels?.[0]?.textContent).toBe("Scope");
    expect(scopeFilter.parentElement?.textContent).toContain("Scopes in loaded results");
    const describedBy = scopeFilter.getAttribute("aria-describedby")?.split(" ") ?? [];
    expect(describedBy.map(id => container.querySelector(`#${id}`)?.textContent).join(" ")).toContain("up to 100 secrets");
    expect(describedBy.map(id => container.querySelector(`#${id}`)?.textContent).join(" ")).toContain("other scopes may exist");
  });

  it("does not turn descriptor scope suggestions into inventory filters", async () => {
    const { container } = render({
      getItems: () => [],
      getDescriptors: () => ({
        types: [],
        stores: [],
        scopes: [{ name: "advisory.scope", displayName: "Advisory scope", description: "Suggestion only.", source: "Fixture" }]
      })
    });
    await waitForText(container, "No secrets yet");
    const scope = [...container.querySelectorAll<HTMLSelectElement>(".secrets-filter select")]
      .find(select => select.labels?.[0]?.textContent === "Scope")!;

    expect([...scope.options].map(option => option.value)).toEqual([""]);
  });

  it("finds a known later record by name and display name through Search and Enter", async () => {
    const laterSecret = secret({
      id: "beyond-first-page",
      name: "known.later.secret",
      displayName: "Known later credentials",
      scope: "Integration:Later"
    });
    const firstPage = Array.from({ length: 100 }, (_, index) => secret({
      id: String(index + 1),
      name: `inventory.secret.${index + 1}`,
      displayName: `Inventory secret ${index + 1}`
    }));
    const { container, listRequests } = render({
      getItems: url => {
        const term = getSearchTerm(url);
        return term === laterSecret.name || term === laterSecret.displayName ? [laterSecret] : firstPage;
      },
      getTotalCount: (_items, url) => getSearchTerm(url) ? 1 : 101
    });
    await waitForText(container, "Inventory secret 1");
    expect(container.querySelector("[data-secret-open='known.later.secret']")).toBeNull();
    expect(container.querySelector(".secrets-count")?.textContent).toContain("101 results");
    const search = container.querySelector<HTMLInputElement>("input[aria-label='Search secrets']")!;

    change(search, laterSecret.name);
    clickButton(container, "Search");
    await waitForText(container, "Known later credentials");
    expect(container.querySelector("[data-secret-open='known.later.secret']")).not.toBeNull();
    expect(getSearchTerm(listRequests.at(-1)!)).toBe(laterSecret.name);
    expect(new URL(listRequests.at(-1)!, "https://fixture.example").searchParams.get("pageSize")).toBe("100");
    expect(container.querySelector(".secrets-count")?.textContent).toContain(`1 loaded · 1 result for “${laterSecret.name}”`);

    const scope = [...container.querySelectorAll<HTMLSelectElement>(".secrets-filter select")]
      .find(select => select.labels?.[0]?.textContent === "Scope")!;
    selectValue(scope, laterSecret.scope!);
    const trigger = container.querySelector<HTMLButtonElement>("[data-secret-edit='known.later.secret']")!;
    trigger.focus();
    flushSync(() => trigger.click());
    expect(container.querySelector("[role='dialog']")).not.toBeNull();
    clickButton(container, "Cancel", "[role='dialog']");
    await waitFor(() => !container.querySelector("[role='dialog']"));
    expect(document.activeElement?.getAttribute("data-secret-edit")).toBe("known.later.secret");
    expect(container.querySelector("[data-secret-open='known.later.secret']")).not.toBeNull();
    expect(scope.value).toBe(laterSecret.scope);
    expect(container.querySelector(".secrets-count")?.textContent).toContain(`1 shown · 1 loaded · 1 result for “${laterSecret.name}”`);

    change(search, laterSecret.displayName);
    flushSync(() => search.dispatchEvent(new KeyboardEvent("keydown", { key: "Enter", bubbles: true })));
    await waitFor(() => listRequests.length === 3);
    await waitForText(container, `for “${laterSecret.displayName}”`);
    expect(getSearchTerm(listRequests.at(-1)!)).toBe(laterSecret.displayName);
    expect(container.querySelector("[data-secret-open='known.later.secret']")).not.toBeNull();
    expect(container.textContent).not.toContain("raw-secret-value");
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
    expect(container.querySelector("button[aria-label='Edit payments.api']")).not.toBeNull();
    expect(container.textContent).not.toContain("raw-secret-value");
    unmount();
  });

  it("copies only the technical name from both the list and details", async () => {
    const writeText = vi.fn().mockResolvedValue(undefined);
    Object.defineProperty(navigator, "clipboard", { configurable: true, value: { writeText } });
    const { container } = render();
    await waitForText(container, "Payments API");
    openEditor(container);
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

  it("returns to the list containing the created secret after refresh", async () => {
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
    change(container.querySelector<HTMLInputElement>(".secrets-value-input input")!, "secret-value");
    clickButton(container, "Create", ".secrets-modal");

    await waitForText(container, "Secret created.");
    expect(container.querySelector("[role=dialog]")).toBeNull();
    expect(container.querySelector(".secrets-detail")).toBeNull();
    expect(container.textContent).toContain("smtp-password");
    expect(postJson).toHaveBeenCalledWith("/secrets", expect.objectContaining({ value: "secret-value" }));
    unmount();
  });

  it("sends an empty description when metadata description is cleared", async () => {
    const putJson = vi.fn(async () => paymentSecret({ description: "" }));
    const { container, unmount } = render({ putJson });

    await waitForText(container, "Payments API");
    openEditor(container);
    await waitFor(() => container.querySelector<HTMLInputElement>(".secrets-edit-form input")?.value === "Payments API");
    change(container.querySelector<HTMLTextAreaElement>(".secrets-edit-form textarea")!, "");
    clickButton(container, "Save changes");

    await waitFor(() => putJson.mock.calls.length > 0);
    expect(putJson).toHaveBeenCalledWith("/secrets/payments.api", { displayName: "Payments API", description: "" });
    unmount();
  });
});

function render(options: {
  getItems?(url: string, baseUrl: string): unknown[] | Promise<unknown[]>;
  totalCount?: number;
  getTotalCount?(items: unknown[], url: string): number;
  getDescriptors?(): unknown | Promise<unknown>;
  postJson?: ReturnType<typeof vi.fn>;
  putJson?: ReturnType<typeof vi.fn>;
} = {}) {
  const host = document.createElement("div");
  document.body.appendChild(host);
  const root = createRoot(host);
  const listRequests: string[] = [];
  const dialogs = {
    confirm: vi.fn(async () => true),
    prompt: vi.fn(async () => null),
    alert: vi.fn(async () => {})
  };
  const createContext = (baseUrl = ""): StudioEndpointContext => ({
    baseUrl,
    http: {
      requestJson: vi.fn(),
      getJson: async <T,>(url: string): Promise<T> => {
        if (url.includes("descriptors")) {
          if (options.getDescriptors) return await options.getDescriptors() as T;
          return {
            types: [{ name: "text", displayName: "Text", description: "", editorHint: "text", supportedStoreNames: ["encrypted"] }],
            stores: [{ name: "encrypted", displayName: "Encrypted", description: "", capabilities: 1, isReadOnly: false }]
          } as T;
        }
        listRequests.push(url);
        const items = await options.getItems?.(url, baseUrl) ?? [paymentSecret()];
        return {
          items,
          totalCount: options.getTotalCount?.(items, url) ?? options.totalCount ?? items.length,
          rawValue: "raw-secret-value"
        } as T;
      },
      postJson: options.postJson ?? vi.fn(),
      putJson: options.putJson ?? vi.fn(),
      deleteJson: vi.fn(),
      postForm: vi.fn()
    }
  });
  const renderPage = (context = createContext()) => {
    flushSync(() => root.render(<SecretsPage context={context} dialogs={dialogs} />));
  };
  renderPage();

  const unmount = () => {
    flushSync(() => root.unmount());
    host.remove();
    cleanup.delete(unmount);
  };
  cleanup.add(unmount);
  return { container: host, unmount, listRequests, changeEndpoint: (baseUrl: string) => renderPage(createContext(baseUrl)) };
}

function getSearchTerm(url: string) {
  return new URL(url, "https://fixture.example").searchParams.get("search") ?? "";
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
  scope?: string;
  tags?: string[];
  currentVersion?: number | null;
  createdAt?: string;
  updatedAt?: string | null;
  expiresAt?: string | null;
}) {
  return {
    id: overrides.id,
    name: overrides.name,
    displayName: overrides.displayName,
    typeName: "text",
    storeName: "encrypted",
    tags: overrides.tags ?? [],
    status: "Active",
    currentVersion: overrides.currentVersion ?? 1,
    createdAt: overrides.createdAt ?? "2026-06-24T00:00:00Z",
    updatedAt: overrides.updatedAt,
    expiresAt: overrides.expiresAt,
    description: overrides.description ?? "",
    scope: overrides.scope
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

function openEditor(container: HTMLElement) {
  const trigger = container.querySelector<HTMLButtonElement>("button[aria-label='Edit payments.api']")!;
  trigger.focus();
  flushSync(() => trigger.click());
  return trigger;
}
