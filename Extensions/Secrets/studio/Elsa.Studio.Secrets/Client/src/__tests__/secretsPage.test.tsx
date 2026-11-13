import React, { useState } from "react";
import { flushSync } from "react-dom";
import { createRoot } from "react-dom/client";
import { afterEach, describe, expect, it, vi } from "vitest";
import type { StudioEndpointContext } from "@elsa-workflows/studio-sdk";
import { SecretEditorPage } from "../SecretEditorPage";
import type { ElsaStudioModuleApi } from "@elsa-workflows/studio-sdk";
import { SecretsPage } from "../SecretsPage";

const cleanup = new Set<() => void>();
const clipboardDescriptor = Object.getOwnPropertyDescriptor(navigator, "clipboard");
afterEach(() => {
  for (const dispose of cleanup) dispose();
  window.history.replaceState(null, "", "/security/secrets");
  if (clipboardDescriptor) Object.defineProperty(navigator, "clipboard", clipboardDescriptor);
  else Reflect.deleteProperty(navigator, "clipboard");
});

describe("secrets page", () => {
  it("places Create in the header as its only filled primary action", async () => {
    const { container } = render();
    await waitForText(container, "Payments API");

    const header = container.querySelector<HTMLElement>(".studio-page-header")!;
    const create = header.querySelector<HTMLButtonElement>("button[aria-label='Create secret']");
    expect(create).not.toBeNull();
    expect(create?.textContent).toContain("Create secret");
    expect(create?.getAttribute("data-variant")).toBe("primary");
    expect(header.querySelectorAll("button[data-variant='primary']")).toHaveLength(1);
  });

  it("keeps Edit direct and exposes secondary operations in a keyboard accessible menu", async () => {
    const { container } = render();
    await waitForText(container, "Payments API");

    const row = container.querySelector<HTMLButtonElement>("button[aria-label='Edit payments.api']")?.closest("tr");
    expect(row).not.toBeNull();
    expect(row?.querySelector("button[aria-label='Edit payments.api']")).not.toBeNull();
    expect(row?.querySelector("button[aria-label='Rotate payments.api']")).toBeNull();

    const trigger = row?.querySelector<HTMLButtonElement>("button[aria-label='Actions for payments.api']");
    expect(trigger).not.toBeNull();
    if (!trigger) return;

    trigger.getBoundingClientRect = () => ({ left: 20, right: 52, top: 20, bottom: 52, width: 32, height: 32 } as DOMRect);
    flushSync(() => {
      trigger.focus();
      trigger.dispatchEvent(new KeyboardEvent("keydown", { key: "ArrowDown", bubbles: true, cancelable: true }));
    });
    await waitFor(() => document.body.querySelector("[role='menu']") !== null);

    const menu = document.body.querySelector<HTMLElement>("[role='menu']")!;
    const items = [...menu.querySelectorAll<HTMLElement>("[role='menuitem']")];
    expect(items.map(item => item.textContent?.trim())).toEqual(["Rotate", "Test", "Revoke", "Delete"]);
    expect(items.slice(2).map(item => item.dataset.destructive)).toEqual(["true", "true"]);
    expect(menu.querySelector("[role='separator']")).not.toBeNull();
    expect(document.activeElement).toBe(items[0]);

    flushSync(() => items[0].dispatchEvent(new KeyboardEvent("keydown", { key: "Escape", bubbles: true, cancelable: true })));
    await waitFor(() => document.body.querySelector("[role='menu']") === null);
    expect(document.activeElement).toBe(trigger);
  });

  it("preserves revoked and deleted operation gates in the secondary menu", async () => {
    const { container } = render({ getItems: () => [
      paymentSecret({ status: "Revoked" }),
      secret({ id: "deleted", name: "deleted.api", displayName: "Deleted API", status: "Deleted" })
    ] });
    await waitForText(container, "Deleted API");

    const revokedMenu = await openSecretMenu(container, "payments.api");
    expect(menuItem(revokedMenu, "Rotate").disabled).toBe(false);
    expect(menuItem(revokedMenu, "Test").disabled).toBe(false);
    expect(menuItem(revokedMenu, "Revoke").disabled).toBe(true);
    expect(menuItem(revokedMenu, "Delete").disabled).toBe(false);
    flushSync(() => menuItem(revokedMenu, "Rotate").dispatchEvent(new KeyboardEvent("keydown", { key: "Escape", bubbles: true, cancelable: true })));
    await waitFor(() => document.body.querySelector("[role='menu']") === null);

    const deletedMenu = await openSecretMenu(container, "deleted.api");
    expect([...deletedMenu.querySelectorAll<HTMLButtonElement>("[role='menuitem']")].every(item => item.disabled)).toBe(true);
  });

  it("opens a separate editor and cancels to the original collection", async () => {
    const putJson = vi.fn(); const { container } = render({ putJson });
    await waitForText(container, "Payments API");
    await openEditor(container);
    expect(container.querySelector("table")).toBeNull();
    expect(window.location.pathname).toBe("/security/secrets/payments.api/edit");
    change(container.querySelector<HTMLInputElement>(".secrets-edit-form input")!, "Unsaved edit");
    clickButton(container, "Cancel");
    await waitForText(container, "Payments API");
    expect(container.querySelector(".secrets-editor-page")).toBeNull();
    await waitFor(() => document.activeElement?.getAttribute("data-secret-edit") === "payments.api");
    expect(putJson).not.toHaveBeenCalled();
    await openEditor(container);
    expect(container.querySelector<HTMLInputElement>(".secrets-edit-form input")?.value).toBe("Payments API");
  });

  it("uses canonical keyboard tabs without trapping the standalone editor", async () => {
    const { container } = render(); await waitForText(container, "Payments API"); await openEditor(container);
    const selected = container.querySelector<HTMLButtonElement>("[role=tab][aria-selected=true]")!;
    expect(selected.textContent).toBe("Details");
    flushSync(() => { selected.focus(); selected.dispatchEvent(new KeyboardEvent("keydown", { key: "ArrowRight", bubbles: true, cancelable: true })); });
    expect(document.activeElement?.textContent).toBe("Storage & lifetime");
    expect(container.querySelector("[role=tab][aria-selected=true]")?.textContent).toBe("Storage & lifetime");
    clickButton(container, "Cancel"); await waitForText(container, "Payments API");
    await waitFor(() => document.activeElement?.getAttribute("data-secret-edit") === "payments.api");
  });

  it("saves metadata and returns to the updated list", async () => {
    let current = paymentSecret();
    const putJson = vi.fn(async (_url: string, request: { displayName: string; description: string }) => {
      current = paymentSecret(request);
      return current;
    });
    const { container } = render({ getItems: () => [current], putJson });
    await waitForText(container, "Payments API");
    await openEditor(container);
    flushSync(() => change(container.querySelector<HTMLInputElement>(".secrets-edit-form input")!, "Updated payments"));
    clickButton(container, "Save changes");
    await waitForText(container, "Secret metadata updated.");
    expect(container.querySelector(".secrets-editor-page")).toBeNull();
    await waitFor(() => container.querySelector("table")?.textContent?.includes("Updated payments") === true);
    await waitFor(() => document.activeElement?.getAttribute("data-secret-edit") === "payments.api");
    expect(putJson).toHaveBeenCalledWith("/secrets/payments.api", { displayName: "Updated payments", description: "Token for payment provider" });
  });

  it("keeps existing material and immutable metadata read-only in named sections", async () => {
    const { container } = render({ getItems: () => [paymentSecret({ scope: "module.payments", tags: ["billing"], expiresAt: "2026-12-31T00:00:00Z" })] });
    await waitForText(container, "Payments API"); await openEditor(container);
    expect(container.querySelector<HTMLInputElement>("[data-dialog-initial-focus]")?.value).toBe("Payments API");
    const storage = [...container.querySelectorAll<HTMLButtonElement>("[role=tab]")].find(t => t.textContent === "Storage & lifetime")!;
    flushSync(() => storage.click());
    const scope = [...container.querySelectorAll<HTMLInputElement>("input")].find(i => i.readOnly)!;
    expect(scope.value).toBe("module.payments");
    expect(document.getElementById(scope.getAttribute("aria-describedby")!)?.textContent).toContain("Scope does not grant access");
    const secret = [...container.querySelectorAll<HTMLButtonElement>("[role=tab]")].find(t => t.textContent === "Secret")!;
    flushSync(() => secret.click());
    expect(container.querySelector("[role=tabpanel]:not([hidden])")?.textContent).toContain("existing value stays hidden");
    expect(container.querySelector("input[type=password]")).toBeNull();
    expect(container.querySelector("details")).toBeNull();
  });

  it("waits for a pending save before allowing dismissal", async () => {
    let finishSave!: (value: unknown) => void;
    const putJson = vi.fn(() => new Promise(resolve => { finishSave = resolve; }));
    const { container } = render({ putJson });
    await waitForText(container, "Payments API");
    await openEditor(container);
    flushSync(() => change(container.querySelector<HTMLInputElement>(".secrets-edit-form input")!, "Pending save"));
    clickButton(container, "Save changes");
    await waitFor(() => putJson.mock.calls.length === 1);
    const editor = container.querySelector<HTMLElement>(".secrets-editor-page")!;
    flushSync(() => editor.dispatchEvent(new KeyboardEvent("keydown", { key: "Escape", bubbles: true })));
    expect(container.querySelector(".secrets-editor-page")).toBe(editor);
    await waitFor(() => [...editor.querySelectorAll<HTMLButtonElement>("button")].find(b => b.textContent?.includes("Back to secrets"))?.disabled === true);
    finishSave(paymentSecret());
    await waitForText(container, "Secret metadata updated.");
    expect(container.querySelector(".secrets-editor-page")).toBeNull();
  });

  it("keeps failed metadata edits in the dialog for retry", async () => {
    const putJson = vi.fn().mockRejectedValueOnce(new Error("Metadata save unavailable.")).mockResolvedValue(paymentSecret());
    const { container } = render({ putJson });
    await waitForText(container, "Payments API");
    await openEditor(container);
    flushSync(() => change(container.querySelector<HTMLInputElement>(".secrets-edit-form input")!, "Retry payments"));
    clickButton(container, "Save changes");
    await waitForText(container, "Metadata save unavailable.");
    const editor = container.querySelector<HTMLElement>(".secrets-editor-page")!;
    expect(editor).not.toBeNull();
    expect(editor.querySelector("[role=alert]")?.textContent).toContain("Metadata save unavailable.");
    expect(editor.querySelector<HTMLInputElement>(".secrets-edit-form input")?.value).toBe("Retry payments");
    clickButton(container, "Save changes");
    await waitForText(container, "Secret metadata updated.");
    expect(container.querySelector(".secrets-editor-page")).toBeNull();
    expect(putJson).toHaveBeenCalledTimes(2);
  });

  it.each([false, true])("dismisses operation notifications and shows later results (error=%s)", async error => {
    const postJson = error
      ? vi.fn().mockRejectedValue(new Error("Test unavailable."))
      : vi.fn().mockResolvedValue({ succeeded: true, code: "resolved", message: "Secret resolved successfully." });
    const { container } = render({ postJson });
    await waitForText(container, "Payments API");
    const runTest = () => clickSecretAction(container, "payments.api", "Test");
    await runTest();
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
    await runTest();
    await waitForText(container, message);
    expect(container.querySelector(".secrets-notification")).not.toBeNull();
    expect(postJson).toHaveBeenCalledTimes(2);
  });

  it("clears entered rotation material and visibility when the target secret changes", async () => {
    const { container } = render({ getItems: () => [paymentSecret(), secret({ id: "2", name: "other.api", displayName: "Other API" })] });
    await waitForText(container, "Other API");
    await clickSecretAction(container, "payments.api", "Rotate");
    change(container.querySelector<HTMLInputElement>('input[data-secret-field="value"]')!, "dummy-new-value");
    await waitFor(() => container.querySelector<HTMLInputElement>('input[data-secret-field="value"]')?.value === "dummy-new-value");
    flushSync(() => container.querySelector<HTMLButtonElement>("button[aria-label='Show value']")!.click());
    expect(container.querySelector<HTMLInputElement>('input[data-secret-field="value"]')?.type).toBe("text");
    await clickSecretAction(container, "other.api", "Rotate");
    const input = container.querySelector<HTMLInputElement>('input[data-secret-field="value"]')!;
    expect(input.value).toBe("");
    expect(input.type).toBe("password");
  });

  it("offers creation from an empty collection without a blank details panel", async () => {
    const { container } = render({ getItems: () => [] });
    await waitForText(container, "No secrets yet");
    expect(container.querySelector(".secrets-detail")).toBeNull();
    expect(container.textContent).not.toContain("Select a secret.");
    clickButton(container, "Create secret");
    await waitFor(() => !!container.querySelector(".secrets-editor-page[aria-label='Create secret']"));
  });

  it("retains the standalone create form across option failure and retries in place", async () => {
    let calls = 0;
    const descriptors = { types: [{ name: "text", displayName: "Text", supportedStoreNames: ["encrypted"] }], stores: [{ name: "encrypted", displayName: "Encrypted", isReadOnly: false }], scopes: [] };
    const { container } = render({ getDescriptors: () => { if (++calls === 3) throw new Error("private-options-error"); return descriptors; } });
    await waitForText(container, "Payments API"); clickButton(container, "Create secret");
    await waitFor(() => container.querySelector(".secrets-create-form") !== null);
    const name = container.querySelector<HTMLInputElement>('[data-secret-field="name"]')!;
    const value = container.querySelector<HTMLInputElement>('[data-secret-field="value"]')!;
    change(name, "AuthoredSecret"); change(value, "material");
    const storage = [...container.querySelectorAll<HTMLButtonElement>("[role=tab]")].find(t => t.textContent?.startsWith("Storage & lifetime"))!;
    flushSync(() => storage.click()); clickButton(container, "Refresh scope suggestions");
    await waitForText(container, "Secret options could not be loaded.");
    expect(container.querySelector("table")).toBeNull(); expect(name.value).toBe("AuthoredSecret"); expect(value.value).toBe("material");
    clickButton(container, "Retry secret options"); await waitFor(() => calls === 4);
    await waitFor(() => !container.textContent?.includes("Secret options could not be loaded."));
    expect(name.value).toBe("AuthoredSecret"); expect(value.value).toBe("material");
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
    await waitFor(() => !container.querySelector<HTMLButtonElement>(".studio-page-actions button[aria-label='Create secret']")?.disabled);
    clickButton(container, "Create secret");
    await waitFor(() => !!container.querySelector(".secrets-editor-page[aria-label='Create secret'] select[id^='scope-select-']"));
    const scopeSelect = container.querySelector<HTMLSelectElement>(".secrets-editor-page[aria-label='Create secret'] select[id^='scope-select-']")!;
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
      .find(select => select.labels?.[0]?.textContent === "Status")!;
    selectValue(status, "Revoked");
    await waitForText(container, "No loaded secrets match your filters");
    expect(container.querySelector(".secrets-detail")).toBeNull();
    selectValue(status, "");
    await waitForText(container, "Payments API");
    expect(container.querySelector(".secrets-detail")).toBeNull();
  });

  it("distinguishes loaded coverage from the completed search total", async () => {
    const { container } = render({
      getItems: () => inventoryPage(),
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

  it("keeps a selected scope visible when a later search returns a different scope", async () => {
    const laterSecret = secret({
      id: "later-scope",
      name: "later.scope.secret",
      displayName: "Later scope secret",
      scope: "scope.B"
    });
    const { container } = render({
      getItems: url => getSearchTerm(url) === laterSecret.name ? [laterSecret] : inventoryPage(),
      getTotalCount: (_items, url) => getSearchTerm(url) ? 1 : 101
    });
    await waitForText(container, "Inventory secret 1");

    const scope = [...container.querySelectorAll<HTMLSelectElement>(".secrets-filter select")]
      .find(select => select.labels?.[0]?.textContent === "Scope")!;
    selectValue(scope, "scope.1");
    const search = container.querySelector<HTMLInputElement>("input[aria-label='Search secrets']")!;
    change(search, laterSecret.name);
    clickButton(container, "Search");

    await waitFor(() => container.querySelector(".secrets-count")?.textContent === `0 shown · 1 loaded · 1 result for “${laterSecret.name}”`);
    expect(scope.value).toBe("scope.1");
    expect([...scope.options].find(option => option.value === "scope.1")?.textContent).toContain("not in loaded results");

    selectValue(scope, "");
    await waitForText(container, "Later scope secret");
    expect(container.querySelector(".secrets-count")?.textContent).toBe(`1 shown · 1 loaded · 1 result for “${laterSecret.name}”`);
    expect(scope.value).toBe("");
  });

  it("keeps toolbar Refresh on the accepted query while search text is a draft", async () => {
    const { container, listRequests } = render({
      getItems: () => [paymentSecret()],
      getTotalCount: () => 1
    });
    await waitForText(container, "Payments API");

    const search = container.querySelector<HTMLInputElement>("input[aria-label='Search secrets']")!;
    change(search, "queryA");
    clickButton(container, "Search");
    await waitFor(() => container.querySelector(".secrets-count")?.textContent === "1 shown · 1 loaded · 1 result for “queryA”");
    expect(getSearchTerm(listRequests.at(-1)!)).toBe("queryA");

    change(search, "draftB");
    clickButton(container, "Refresh");
    await waitFor(() => listRequests.length === 3);
    await waitFor(() => !!container.querySelector(".secrets-count"));
    expect(container.querySelector(".secrets-count")?.textContent).toBe("1 shown · 1 loaded · 1 result for “queryA”");
    expect(getSearchTerm(listRequests.at(-1)!)).toBe("queryA");
    expect(search.value).toBe("draftB");
  });

  it("keeps metadata-save reloads on the accepted query while search text is a draft", async () => {
    let current = paymentSecret({ scope: "scope.A" });
    const putJson = vi.fn(async (_url: string, request: { displayName: string; description: string }) => {
      current = paymentSecret({ ...request, scope: "scope.A" });
      return current;
    });
    const { container, listRequests } = render({
      getItems: () => [current],
      getTotalCount: () => 1,
      putJson
    });
    await waitForText(container, "Payments API");

    const search = container.querySelector<HTMLInputElement>("input[aria-label='Search secrets']")!;
    change(search, "queryA");
    clickButton(container, "Search");
    await waitFor(() => container.querySelector(".secrets-count")?.textContent === "1 shown · 1 loaded · 1 result for “queryA”");
    expect(getSearchTerm(listRequests.at(-1)!)).toBe("queryA");

    change(search, "draftB");
    await openEditor(container);
    flushSync(() => change(container.querySelector<HTMLInputElement>(".secrets-edit-form input")!, "Updated payments"));
    clickButton(container, "Save changes");
    await waitForText(container, "Secret metadata updated.");

    expect(putJson).toHaveBeenCalledWith("/secrets/payments.api", { displayName: "Updated payments", description: "Token for payment provider" });
    expect(getSearchTerm(listRequests.at(-1)!)).toBe("queryA");
    await waitFor(() => container.querySelector(".secrets-count")?.textContent === "1 shown · 1 loaded · 1 result for “queryA”");
    expect(container.querySelector<HTMLInputElement>("input[aria-label='Search secrets']")?.value).toBe("draftB");
  });

  it("retries the failed query instead of the later search draft", async () => {
    let failAttempt = true;
    const { container, listRequests } = render({
      getItems: url => {
        const query = getSearchTerm(url);
        if (query === "queryB" && failAttempt) {
          failAttempt = false;
          throw new Error("private-backend-detail");
        }
        return query === "draftC" ? [] : [paymentSecret()];
      },
      getTotalCount: (_items, url) => getSearchTerm(url) === "draftC" ? 0 : 1
    });
    await waitForText(container, "Payments API");

    const search = container.querySelector<HTMLInputElement>("input[aria-label='Search secrets']")!;
    change(search, "queryB");
    clickButton(container, "Search");
    await waitForText(container, "Secrets could not be loaded.");
    expect(getSearchTerm(listRequests.at(-1)!)).toBe("queryB");

    change(search, "draftC");
    clickButton(container, "Retry");
    await waitFor(() => listRequests.length === 3);
    await waitFor(() => !!container.querySelector(".secrets-count"));
    expect(container.querySelector(".secrets-count")?.textContent).toBe("1 shown · 1 loaded · 1 result for “queryB”");

    expect(getSearchTerm(listRequests.at(-1)!)).toBe("queryB");
    expect(search.value).toBe("draftC");
  });

  it("does not label a complete full page as partial", async () => {
    const { container } = render({
      getItems: () => inventoryPage("Complete"),
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
      .find(select => select.labels?.[0]?.textContent === "Status")!;
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

    expect(container.querySelector(".studio-result-state button")?.textContent).toContain("Clear filters");
    expect(container.querySelector(".secrets-coverage")).toBeNull();
  });

  it("shows an authoritative no-match search before the selected local facet empty state", async () => {
    const { container } = render({
      getItems: url => getSearchTerm(url) === "missing-secret" ? [] : [paymentSecret()],
      getTotalCount: (_items, url) => getSearchTerm(url) === "missing-secret" ? 0 : 1
    });
    await waitForText(container, "Payments API");

    const status = [...container.querySelectorAll<HTMLSelectElement>(".secrets-filter select")]
      .find(select => select.labels?.[0]?.textContent === "Status")!;
    selectValue(status, "Revoked");
    await waitForText(container, "No loaded secrets match your filters");
    const search = container.querySelector<HTMLInputElement>("input[aria-label='Search secrets']")!;
    change(search, "missing-secret");
    clickButton(container, "Search");

    await waitFor(() => container.querySelector(".secrets-count")?.textContent === "0 shown · 0 loaded · 0 results for “missing-secret”");
    expect(container.querySelector(".studio-result-state h3")?.textContent).toBe("No results for “missing-secret”");
    expect(status.value).toBe("Revoked");
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
    expect(container.querySelector(".secrets-count")?.textContent).toBe("1 shown · 1 loaded · 1 result for “latest”");
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
    expect(scopeFilter.closest(".secrets-filter")?.textContent).toContain("Scopes in loaded results");
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
    const firstPage = inventoryPage();
    const getSecret = vi.fn((name: string) => { expect(name).toBe(laterSecret.name); return laterSecret; });
    const { container, listRequests, metadataRequests } = render({
      getSecret,
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
    expect(container.querySelector(".secrets-editor-page")).not.toBeNull();
    await waitFor(() => metadataRequests.length === 1);
    expect(metadataRequests).toEqual([`/secrets/${encodeURIComponent(laterSecret.name)}`]);
    expect(getSecret).toHaveBeenCalledWith(laterSecret.name, "");
    await waitFor(() => container.querySelector(".secrets-edit-form") !== null);
    clickButton(container, "Cancel", ".secrets-editor-page");
    await waitFor(() => !container.querySelector(".secrets-editor-page"));
    await waitFor(() => document.activeElement?.getAttribute("data-secret-edit") === "known.later.secret");
    expect(container.querySelector("[data-secret-open='known.later.secret']")).not.toBeNull();
    expect([...container.querySelectorAll<HTMLSelectElement>(".secrets-filter select")].find(s => s.labels?.[0]?.textContent === "Scope")?.value).toBe(laterSecret.scope);
    expect(container.querySelector(".secrets-count")?.textContent).toContain(`1 shown · 1 loaded · 1 result for “${laterSecret.name}”`);

    const returnedSearch = container.querySelector<HTMLInputElement>('input[aria-label="Search secrets"]')!;
    change(returnedSearch, laterSecret.displayName);
    flushSync(() => returnedSearch.closest("form")!.dispatchEvent(new Event("submit", { bubbles: true, cancelable: true })));
    await waitFor(() => getSearchTerm(listRequests.at(-1)!) === laterSecret.displayName);
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
    const listCopy = container.querySelector<HTMLButtonElement>('button[aria-label="Copy technical name payments.api"]')!;
    flushSync(() => listCopy.click());
    await waitFor(() => writeText.mock.calls.length === 1);
    await openEditor(container);
    const buttons = [...container.querySelectorAll<HTMLButtonElement>("button[aria-label='Copy technical name payments.api']")];
    expect(buttons).toHaveLength(1);
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
    clickButton(container, "Create secret");
    await waitFor(() => !!container.querySelector(".secrets-create-form"));
    const inputs = Array.from(container.querySelectorAll<HTMLInputElement>(".secrets-create-form input"));
    change(inputs[0], "smtp-password");
    change([...container.querySelectorAll<HTMLInputElement>(".secrets-create-form input")].find(i => i.labels?.[0]?.textContent?.trim() === "Display name")!, "SMTP Password");
    change(container.querySelector<HTMLInputElement>('input[data-secret-field="value"]')!, "secret-value");
    clickButton(container, "Create", ".secrets-create-form");

    await waitForText(container, "Secret created.");
    expect(container.querySelector(".secrets-editor-page")).toBeNull();
    expect(container.querySelector(".secrets-detail")).toBeNull();
    await waitForText(container, "smtp-password");
    expect(postJson).toHaveBeenCalledWith("/secrets", expect.objectContaining({ value: "secret-value" }));
    unmount();
  });

  it("sends an empty description when metadata description is cleared", async () => {
    const putJson = vi.fn(async () => paymentSecret({ description: "" }));
    const { container, unmount } = render({ putJson });

    await waitForText(container, "Payments API");
    await openEditor(container);
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
  getSecret?(name: string, baseUrl: string): unknown | Promise<unknown>;
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
  const metadataRequests: string[] = [];
  const dialogs = {
    confirm: vi.fn(async () => true),
    prompt: vi.fn(async () => null),
    alert: vi.fn(async () => {})
  };
  const createContext = (baseUrl = ""): StudioEndpointContext => {
    const metadataByName = new Map<string, unknown>();
    return ({
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
        if (url.startsWith("/secrets/")) {
          metadataRequests.push(url);
          const name = decodeURIComponent(url.split("/").at(-1)!);
          const record = options.getSecret ? await options.getSecret(name, baseUrl) : metadataByName.get(name.toLowerCase());
          if (!record) throw Object.assign(new Error("Secret not found."), { status: 404 });
          return record as T;
        }
        listRequests.push(url);
        const items = await options.getItems?.(url, baseUrl) ?? [paymentSecret()];
        for (const item of items) {
          if (typeof item === "object" && item !== null && "name" in item) metadataByName.set(String(item.name).toLowerCase(), item);
        }
        return {
          items,
          totalCount: options.getTotalCount?.(items, url) ?? options.totalCount ?? items.length,
          rawValue: "raw-secret-value"
        } as T;
      },
      postJson: async <T,>(url: string, request: unknown) => {
        const response = await (options.postJson ?? vi.fn())(url, request);
        if (response !== undefined || url !== "/secrets") return response as T;
        const created = request as { name: string; displayName: string };
        return secret({ id: created.name, name: created.name, displayName: created.displayName }) as T;
      },
      putJson: options.putJson ?? vi.fn(),
      deleteJson: vi.fn(),
      postForm: vi.fn()
    }
  });
  };
  function Harness({ context }: { context: StudioEndpointContext }) {
    const [path, setPath] = useState("/security/secrets");
    const navigate = (next: string) => { window.history.pushState(window.history.state, "", next); setPath(next); };
    const pathname = new URL(path, window.location.origin).pathname;
    const api = { backend: context, dialogs } as unknown as ElsaStudioModuleApi;
    if (pathname.endsWith("/new")) return <SecretEditorPage api={api} navigate={navigate} />;
    if (pathname.endsWith("/edit")) return <SecretEditorPage api={api} name={decodeURIComponent(pathname.split("/").at(-2)!)} navigate={navigate} />;
    return <SecretsPage context={context} dialogs={dialogs} navigate={navigate} />;
  }
  const renderPage = (context = createContext()) => {
    flushSync(() => root.render(<Harness context={context} />));
  };
  renderPage();

  const unmount = () => {
    flushSync(() => root.unmount());
    host.remove();
    cleanup.delete(unmount);
  };
  cleanup.add(unmount);
  return { container: host, unmount, listRequests, metadataRequests, changeEndpoint: (baseUrl: string) => renderPage(createContext(baseUrl)) };
}

function getSearchTerm(url: string) {
  return new URL(url, "https://fixture.example").searchParams.get("search") ?? "";
}

function inventoryPage(displayPrefix = "Inventory") {
  return Array.from({ length: 100 }, (_, index) => secret({
    id: String(index + 1),
    name: `${displayPrefix.toLowerCase()}.secret.${index + 1}`,
    displayName: `${displayPrefix} secret ${index + 1}`,
    scope: `scope.${index + 1}`
  }));
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
  status?: string;
}) {
  return {
    id: overrides.id,
    name: overrides.name,
    displayName: overrides.displayName,
    typeName: "text",
    storeName: "encrypted",
    tags: overrides.tags ?? [],
    status: overrides.status ?? "Active",
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

async function openEditor(container: HTMLElement) {
  const trigger = container.querySelector<HTMLButtonElement>("button[aria-label='Edit payments.api']")!;
  trigger.focus();
  flushSync(() => trigger.click());
  await waitFor(() => container.querySelector(".secrets-edit-form") !== null);
  return trigger;
}

async function clickSecretAction(container: HTMLElement, secretName: string, action: string) {
  const menu = await openSecretMenu(container, secretName);
  const item = menuItem(menu, action);
  flushSync(() => item.click());
  await waitFor(() => document.body.querySelector("[role='menu']") === null);
}

async function openSecretMenu(container: HTMLElement, secretName: string) {
  const trigger = container.querySelector<HTMLButtonElement>(`button[aria-label='Actions for ${secretName}']`);
  if (!trigger) throw new Error(`Secret action menu not found: ${secretName}`);
  trigger.getBoundingClientRect = () => ({ left: 20, right: 52, top: 20, bottom: 52, width: 32, height: 32 } as DOMRect);
  flushSync(() => trigger.click());
  await waitFor(() => document.body.querySelector("[role='menu']") !== null);
  return document.body.querySelector<HTMLElement>("[role='menu']")!;
}

function menuItem(menu: HTMLElement, action: string) {
  const item = [...menu.querySelectorAll<HTMLButtonElement>("[role='menuitem']")].find(button => button.textContent?.trim() === action);
  if (!item) throw new Error(`Secret action not found: ${action}`);
  return item;
}
