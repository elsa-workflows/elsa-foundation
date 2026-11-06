import React, { type ReactNode } from "react";
import { flushSync } from "react-dom";
import { createRoot, type Root } from "react-dom/client";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { StudioHttpError, type ElsaStudioModuleApi, type StudioEndpointContext, type StudioTriggerConfigurationEditorContribution } from "@elsa-workflows/studio-sdk";
import { triggerClient, type TriggerClient } from "../api";
import { ChangeReview } from "../ChangeReview";
import { ConfigurationForm } from "../ConfigurationForm";
import { DeliveryDetails, RecipientEvidence } from "../Deliveries";
import { mappingDrafts, realizeMappings } from "../MappingEditor";
import { parseLiteralJson, parseTriggerResponse, serializeTriggerPayload } from "../json";
import { SubscriptionsSection } from "../Subscriptions";
import { TriggersRoute } from "../TriggersRoute";
import type { ActorView, ChangePreview, DeliveryView, InputMapping, RecipientView, SubscriptionView, TriggerTypeDescriptor, TriggerView } from "../types";

const discovery = vi.hoisted(() => ({ document: { capabilities: [] as { id: string; contractVersion: string; links: { rel: string; href: string; templated?: boolean }[] }[] } }));
vi.mock("@elsa-workflows/studio-sdk", async importOriginal => ({ ...await importOriginal<typeof import("@elsa-workflows/studio-sdk")>(), getBackendCapabilities: vi.fn(async () => discovery.document) }));

let roots: Root[] = [];
beforeEach(() => {
  discovery.document = { capabilities: [{ id: "elsa.api.standalone-triggers", contractVersion: "1", links: [
    { rel: "identity", href: "custom/trigger-identity" }, { rel: "triggers", href: "custom/triggers" }, { rel: "trigger", href: "custom/triggers/{triggerId}", templated: true },
    { rel: "change-preview", href: "custom/triggers/{triggerId}/preview", templated: true }, { rel: "change-apply", href: "custom/triggers/{triggerId}/apply", templated: true }
  ] }] };
});
afterEach(() => { for (const root of roots) flushSync(() => root.unmount()); roots = []; document.body.innerHTML = ""; vi.restoreAllMocks(); });
function mount(node: ReactNode) { const container = document.createElement("div"); document.body.appendChild(container); const root = createRoot(container); roots.push(root); flushSync(() => root.render(node)); return { container, render: (next: ReactNode) => flushSync(() => root.render(next)) }; }
const button = (container: HTMLElement, text: string) => [...container.querySelectorAll<HTMLButtonElement>("button")].find(item => item.textContent === text)!;
const control = (container: HTMLElement, label: string) => container.querySelector<HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement>(`[aria-label="${label}"]`)!;
function fill(element: HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement, value: string) {
  const prototype = element instanceof HTMLSelectElement ? HTMLSelectElement.prototype : element instanceof HTMLTextAreaElement ? HTMLTextAreaElement.prototype : HTMLInputElement.prototype;
  Object.getOwnPropertyDescriptor(prototype, "value")!.set!.call(element, value);
  flushSync(() => element.dispatchEvent(new Event(element instanceof HTMLSelectElement ? "change" : "input", { bubbles: true })));
}
const actor: ActorView = { subjectId: "operator", canRead: true, canManage: true, canOperate: true, canAuthorize: true, canReadDeliveries: true, canRetryDeliveries: true, canCancelPending: true };
function trigger(revision = 1): TriggerView { return { id: "trigger-1", name: "Daily accounting", typeKey: "Elsa.Cron", schemaVersion: "1", revision, configuration: {}, origin: { kind: "Studio", configurationReadOnly: false }, desiredState: "Paused", effectiveState: "Paused", subscriptionCount: 1, eligibleSubscriptionCount: 1, findings: [], actions: [{ action: "AddSubscription", allowed: true }] }; }
function subscription(revision = 1): SubscriptionView { return { id: "sub-1", triggerId: "trigger-1", revision, enabled: true, origin: { kind: "Studio", configurationReadOnly: false }, target: { definitionId: "reconcile", slotName: "default" }, mappings: [], invocationGrant: { state: "Granted", revision: 1 }, compatibility: "Valid", findings: [], actions: [{ action: "PauseSubscription", allowed: true }, { action: "SaveSubscription", allowed: true }] }; }
const preview: ChangePreview = { previewId: "preview-1", expiresAt: "2099-01-01T00:00:00Z", observedRevision: 1, commandFingerprint: "fingerprint", canApply: true, findings: [], impact: [{ label: "Subscription", before: "Enabled", after: "Paused" }] };
function apiStub(context?: StudioEndpointContext) { return { backend: context ?? { baseUrl: "https://backend.example/shell/", http: { getJson: vi.fn(), requestJson: vi.fn() } }, runtime: {}, notifications: { notify: vi.fn() }, dialogs: { confirm: vi.fn(async () => true) } } as unknown as ElsaStudioModuleApi; }

describe("standalone trigger API and literal handling", () => {
  it("uses advertised relation paths through the selected transport", async () => {
    const context = { baseUrl: "https://backend.example/tenant/elsa/", http: { getJson: vi.fn(async () => actor) } } as unknown as StudioEndpointContext;
    await triggerClient(context).actor();
    expect(context.http.getJson).toHaveBeenCalledWith("/custom/trigger-identity", expect.any(Object));
    discovery.document.capabilities[0].links[0].href = "../other/identity";
    await expect(triggerClient(context).actor()).rejects.toThrow("unsafe trigger operation");
    expect(context.http.getJson).toHaveBeenCalledTimes(1);
  });
  it("refuses a missing or incompatible capability before calling a resource endpoint", async () => {
    const context = { baseUrl: "opaque", http: { getJson: vi.fn() } } as unknown as StudioEndpointContext;
    discovery.document.capabilities = [];
    await expect(triggerClient(context).list()).rejects.toThrow("does not provide standalone triggers");
    discovery.document.capabilities = [{ id: "elsa.api.standalone-triggers", contractVersion: "2", links: [{ rel: "triggers", href: "future/triggers" }] }];
    await expect(triggerClient(context).list()).rejects.toThrow("require API contract 1; this backend advertises 2");
    expect(context.http.getJson).not.toHaveBeenCalled();
  });
  it("preserves numeric tokens and quoted numeric text through read/edit/write", () => {
    const text = '{"revision":7,"mappings":[{"inputName":"Amount","binding":{"kind":"Literal","value":{"large":18446744073709551615,"fraction":0.1234567890123456789012345678,"text":"18446744073709551615"}}}]}';
    const parsed = parseTriggerResponse<{ revision: number; mappings: InputMapping[] }>(text);
    expect(parsed.revision).toBe(7);
    expect(serializeTriggerPayload(parsed)).toBe(text);
    const quoted = String.raw`[-0,1e-30,"escaped \" 12"]`;
    expect(serializeTriggerPayload(parseLiteralJson(quoted))).toBe(quoted);
    expect(() => parseLiteralJson("[01]")).toThrow();
  });
  it("retains exact typed scalar inputs, explicit null, omission and expression options", () => {
    const inputs = [{ name: "Amount", type: { alias: "UInt64", collectionKind: "Single" }, isRequired: true }];
    const drafts = mappingDrafts([{ inputName: "Amount", binding: { kind: "Literal", value: "18446744073709551615" } }]);
    const built = realizeMappings(inputs, drafts);
    expect(built.errors).toEqual({});
    expect(serializeTriggerPayload(built.mappings)).toContain('"value":18446744073709551615');
    const expression: InputMapping = { inputName: "Amount", binding: { kind: "Expression", template: { language: "JavaScript", source: "args.value", resultType: { alias: "UInt64", collectionKind: "Single" }, options: { strict: true }, capabilityProfile: "binding-pure-v1" }, parameters: { value: { kind: "SourceField", path: ["payload", "amount"] } } } };
    expect(realizeMappings(inputs, mappingDrafts([expression])).mappings).toEqual([expression]);
    expect(realizeMappings(inputs, { Amount: { ...drafts.Amount, kind: "Null" } }).mappings).toEqual([{ inputName: "Amount", binding: { kind: "Literal", value: null } }]);
    expect(realizeMappings(inputs, { Amount: { ...drafts.Amount, kind: "Omit" } }).mappings).toEqual([]);
  });
});

describe("mounted standalone trigger interactions (component evidence)", () => {
  it("uses an exact public optional editor while preserving source-protected and unknown fields", () => {
    const submit = vi.fn();
    const descriptor: TriggerTypeDescriptor = { typeKey: "acme-event", schemaVersion: "1", displayName: "Acme Event", family: "Event", canCreate: true,
      configurationEditorId: "acme.event-editor", configurationFields: [{ key: "topic", label: "Topic", type: "string", required: true }, { key: "credential", label: "Credential", type: "string", sensitive: true }] };
    const editor: StudioTriggerConfigurationEditorContribution = { id: "acme.event-editor", typeKey: "acme-event", schemaVersion: "1",
      component: ({ configuration, disabled, onChange }) => <button type="button" disabled={disabled} onClick={() => onChange({ ...configuration, topic: "changed", credential: "not-authorized" })}>Custom topic</button> };
    const resource = { ...trigger(), typeKey: "acme-event", configuration: { topic: "saved", credential: "protected-reference", providerSetting: "retained" } };
    const { container } = mount(<ConfigurationForm descriptor={descriptor} trigger={resource} editors={[editor]} onCancel={vi.fn()} onSubmit={submit} />);
    flushSync(() => button(container, "Custom topic").click()); flushSync(() => button(container, "Review changes").click());
    expect(submit).toHaveBeenCalledWith(expect.objectContaining({ configuration: { topic: "changed", credential: "protected-reference", providerSetting: "retained" } }));
  });
  it("retains saved values and offers usable metadata fields when the preferred editor is missing or mismatched", () => {
    const submit = vi.fn();
    const descriptor: TriggerTypeDescriptor = { typeKey: "acme-event", schemaVersion: "1", displayName: "Acme Event", family: "Event", canCreate: true,
      configurationEditorId: "acme.event-editor", configurationFields: [{ key: "topic", label: "Topic", type: "string", required: true }] };
    const wrongVersion: StudioTriggerConfigurationEditorContribution = { id: "acme.event-editor", typeKey: "acme-event", schemaVersion: "2", component: () => <p>Wrong version</p> };
    const { container } = mount(<ConfigurationForm descriptor={descriptor} trigger={{ ...trigger(), configuration: { topic: "saved", retained: 7 } }} editors={[wrongVersion]} onCancel={vi.fn()} onSubmit={submit} />);
    expect(container.textContent).toContain("optional configuration editor is unavailable"); expect(container.textContent).not.toContain("Wrong version");
    expect(control(container, "Topic").value).toBe("saved"); fill(control(container, "Topic"), "corrected");
    flushSync(() => button(container, "Review changes").click());
    expect(submit).toHaveBeenCalledWith(expect.objectContaining({ configuration: { topic: "corrected", retained: 7 } }));
  });
  it("shows restricted workflows without unpublished fallbacks or target editing while retaining allowed pause and revoke controls", async () => {
    const restricted: SubscriptionView = { ...subscription(), target: { definitionId: "", slotName: "" }, compatibility: "ValidationPending",
      findings: [{ code: "TargetDetailsRestricted", message: "Workflow read is required." }],
      actions: [{ action: "SaveSubscription", allowed: true }, { action: "PauseSubscription", allowed: true },
        { action: "GrantInvocation", allowed: true }, { action: "RevokeInvocation", allowed: true }] };
    const client = { subscriptions: vi.fn(async () => ({ items: [restricted] })), read: vi.fn(async () => trigger()),
      preview: vi.fn(async () => preview), mappingContext: vi.fn() } as unknown as TriggerClient;
    const { container } = mount(<SubscriptionsSection api={apiStub()} client={client} actor={actor} trigger={trigger()} />);
    await vi.waitFor(() => expect(container.textContent).toContain("Restricted workflow"));
    expect(container.textContent).not.toContain("Not serving");
    expect(button(container, "Edit")).toBeUndefined();
    expect(button(container, "Authorize")).toBeUndefined();
    expect(button(container, "Pause").disabled).toBe(false);
    expect(button(container, "Revoke authority").disabled).toBe(false);
    expect(client.mappingContext).not.toHaveBeenCalled();
    flushSync(() => button(container, "Revoke authority").click());
    await vi.waitFor(() => expect(client.preview).toHaveBeenCalledWith("trigger-1", 1,
      { kind: "RevokeInvocation", subscriptionId: "sub-1", expectedSubscriptionRevision: 1, expectedGrantRevision: 1 }, expect.any(AbortSignal)));
    expect(client.mappingContext).not.toHaveBeenCalled();
  });

  it("requires an explicit HTTP mode and saves its per-trigger async policy", () => {
    const submit = vi.fn();
    const descriptor: TriggerTypeDescriptor = { typeKey: "Elsa.HttpEndpoint", schemaVersion: "1", displayName: "HTTP Endpoint", family: "Http", canCreate: true, configurationFields: [{ key: "route", label: "Route", type: "string", required: true }] };
    const { container } = mount(<ConfigurationForm descriptor={descriptor} onCancel={vi.fn()} onSubmit={submit} />);
    fill(control(container, "Trigger name"), "Accounting"); fill(control(container, "Route"), "/accounting");
    flushSync(() => button(container, "Create trigger").click());
    expect(submit).not.toHaveBeenCalled(); expect(container.textContent).toContain("Choose who responds");
    fill(control(container, "HTTP response mode"), "AsyncFanOut");
    expect(control(container, "HTTP admission policy").value).toBe("RequireAll");
    fill(control(container, "HTTP admission policy"), "AcceptAvailable");
    flushSync(() => button(container, "Create trigger").click());
    expect(submit).toHaveBeenCalledWith(expect.objectContaining({ configuration: { route: "/accounting" }, http: { mode: "AsyncFanOut", admissionPolicy: "AcceptAvailable" } }));
  });
  it("keeps missing capability and denied access distinct from an empty collection", async () => {
    discovery.document.capabilities = [];
    const { container } = mount(<TriggersRoute api={apiStub()} view="list" navigate={vi.fn()} />);
    await vi.waitFor(() => expect(container.textContent).toContain("Standalone triggers unavailable"));
    expect(container.textContent).not.toContain("No triggers yet");
    discovery.document.capabilities = [{ id: "elsa.api.standalone-triggers", contractVersion: "1", links: [{ rel: "identity", href: "custom/identity" }] }];
    const deniedContext = { baseUrl: "https://backend.example/", http: { getJson: vi.fn(async () => { throw new StudioHttpError(403, "You cannot read triggers."); }) } } as unknown as StudioEndpointContext;
    const denied = mount(<TriggersRoute api={apiStub(deniedContext)} view="list" navigate={vi.fn()} />).container;
    await vi.waitFor(() => expect(denied.textContent).toContain("Trigger access unavailable"));
    expect(denied.textContent).not.toContain("No triggers yet");
    expect(deniedContext.http.getJson).toHaveBeenCalledTimes(1);
  });
  it("retains the proposed change on conflict and requires a fresh server review", async () => {
    const api = apiStub();
    const client = { preview: vi.fn(async () => preview), apply: vi.fn().mockRejectedValueOnce(new StudioHttpError(409, "Revision changed")).mockResolvedValue({ requestId: "retry", disposition: "Applied", revision: 3 }), read: vi.fn(async () => trigger(2)), subscription: vi.fn(async () => subscription(4)) } as unknown as TriggerClient;
    const saved = vi.fn();
    const { container } = mount(<ChangeReview api={api} client={client} trigger={trigger()} command={{ kind: "PauseSubscription", subscriptionId: "sub-1", expectedSubscriptionRevision: 1 }} onCancel={vi.fn()} onApplied={saved} />);
    await vi.waitFor(() => expect(button(container, "Apply reviewed change")?.disabled).toBe(false));
    flushSync(() => button(container, "Apply reviewed change").click());
    await vi.waitFor(() => expect(container.textContent).toContain("Your edits are retained"));
    expect(saved).not.toHaveBeenCalled();
    flushSync(() => button(container, "Reload and review current state").click());
    await vi.waitFor(() => expect(client.preview).toHaveBeenLastCalledWith("trigger-1", 2, { kind: "PauseSubscription", subscriptionId: "sub-1", expectedSubscriptionRevision: 4 }, expect.any(AbortSignal)));
    await vi.waitFor(() => expect(button(container, "Apply reviewed change")?.disabled).toBe(false));
    flushSync(() => button(container, "Apply reviewed change").click());
    await vi.waitFor(() => expect(saved).toHaveBeenCalledTimes(1));
  });
  it("edits one canonical association from the trigger surface and reloads the same state in workflow context", async () => {
    let current = subscription();
    const client = { subscriptions: vi.fn(async () => ({ items: [current] })), read: vi.fn(async () => trigger()), preview: vi.fn(async () => preview), apply: vi.fn(async () => { current = { ...current, revision: 2, enabled: false }; return { requestId: "pause", disposition: "Applied", revision: 2 }; }) } as unknown as TriggerClient;
    const { container, render } = mount(<SubscriptionsSection api={apiStub()} client={client} actor={actor} trigger={trigger()} />);
    await vi.waitFor(() => expect(container.textContent).toContain("Enabled · Valid"));
    flushSync(() => button(container, "Pause").click());
    await vi.waitFor(() => expect(button(container, "Apply reviewed change")?.disabled).toBe(false));
    flushSync(() => button(container, "Apply reviewed change").click());
    await vi.waitFor(() => expect(container.textContent).toContain("Paused · Valid"));
    render(<SubscriptionsSection key="workflow" api={apiStub()} client={client} actor={actor} definitionId="reconcile" />);
    await vi.waitFor(() => expect(container.textContent).toContain("Paused · Valid"));
    expect(container.textContent).toContain("independently of this draft or historical graph");
    expect(client.subscriptions).toHaveBeenLastCalledWith({ definitionId: "reconcile", after: undefined }, expect.any(AbortSignal));
  });
  it("reviews and reloads retained delivery controls using its current resource revision after deletion", async () => {
    const recipient: RecipientView = { id: "recipient-1", revision: 2, admission: "Accepted", dispatch: "NotDispatched", findings: [], actions: [{ action: "CancelPendingRecipients", allowed: true }] };
    const retained: DeliveryView = { id: "delivery-1", revision: 4, triggerId: "trigger-1", triggerName: "Deleted resource", triggerRevision: 7,
      acceptedTriggerRevision: 2, admission: "Accepted", acceptedCount: 1, refusedCount: 0, recipients: [recipient], findings: [] };
    const current = { ...retained, triggerRevision: 8, revision: 5, recipients: [{ ...recipient, revision: 3 }] };
    const client = { delivery: vi.fn().mockResolvedValueOnce(retained).mockResolvedValue(current),
      read: vi.fn(async () => { throw new StudioHttpError(404, "Resource deleted"); }), preview: vi.fn(async () => preview),
      apply: vi.fn(async () => ({ requestId: "cancel", disposition: "Applied", revision: 8 })) } as unknown as TriggerClient;
    const { container } = mount(<DeliveryDetails api={apiStub()} client={client} deliveryId="delivery-1" navigate={vi.fn()} />);
    await vi.waitFor(() => expect(button(container, "Cancel pending recipient")?.disabled).toBe(false));
    flushSync(() => button(container, "Cancel pending recipient").click());
    await vi.waitFor(() => expect(client.preview).toHaveBeenCalledWith("trigger-1", 7,
      { kind: "CancelPendingRecipients", deliveryId: "delivery-1", expectedDeliveryRevision: 4, recipients: [{ recipientId: "recipient-1", expectedRevision: 2 }] }, expect.any(AbortSignal)));
    flushSync(() => button(container, "Reload and review current state").click());
    await vi.waitFor(() => expect(client.preview).toHaveBeenLastCalledWith("trigger-1", 8,
      { kind: "CancelPendingRecipients", deliveryId: "delivery-1", expectedDeliveryRevision: 5, recipients: [{ recipientId: "recipient-1", expectedRevision: 3 }] }, expect.any(AbortSignal)));
    await vi.waitFor(() => expect(button(container, "Apply reviewed change")?.disabled).toBe(false));
    flushSync(() => button(container, "Apply reviewed change").click());
    await vi.waitFor(() => expect(client.apply).toHaveBeenCalledWith("trigger-1", 8,
      { kind: "CancelPendingRecipients", deliveryId: "delivery-1", expectedDeliveryRevision: 5, recipients: [{ recipientId: "recipient-1", expectedRevision: 3 }] }, "preview-1", expect.any(String)));
    expect(client.read).not.toHaveBeenCalled();
  });
  it("does not invent an actual-run link from a committed dispatch or planned identity", () => {
    const recipient: RecipientView = { id: "recipient-1", revision: 1, admission: "Accepted", dispatch: "DispatchCommitted", plannedExecutionIdentity: "planned-run", findings: [], actions: [] };
    const { container, render } = mount(<RecipientEvidence recipient={recipient} backendId="selected" />);
    expect(container.textContent).toContain("target may not be queued or started yet");
    expect(container.querySelector("a")).toBeNull();
    render(<RecipientEvidence recipient={{ ...recipient, executionReference: { id: "actual-run", kind: "Native", status: "Running", inspectionPath: "/workflows/instances/actual-run" } }} backendId="selected" />);
    expect(container.querySelector("a")?.textContent).toBe("Inspect actual run");
    expect(container.querySelector("a")?.getAttribute("href")).toContain("backend=selected");
    render(<RecipientEvidence recipient={{ ...recipient, executionReference: { id: "actual-run", kind: "Native", inspectionPath: "/workflows/instances/actual-run?backend=foreign" } }} backendId="selected" />);
    expect(container.querySelector("a")).toBeNull();
  });
});
