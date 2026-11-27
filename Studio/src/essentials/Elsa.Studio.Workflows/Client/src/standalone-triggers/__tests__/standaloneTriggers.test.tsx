import React, { type ReactNode } from "react";
import { flushSync } from "react-dom";
import { createRoot, type Root } from "react-dom/client";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { createContributionRegistry, StudioHttpError, studioSlots, type ElsaStudioModuleApi, type StudioEndpointContext, type StudioTriggerConfigurationEditorContribution, type StudioTriggerTypePresentationContribution } from "@elsa-workflows/studio-sdk";
import { triggerClient, type TriggerClient } from "../api";
import { ChangeReview } from "../ChangeReview";
import { ConfigurationForm } from "../ConfigurationForm";
import { DeliveriesSection, DeliveryDetails, RecipientEvidence } from "../Deliveries";
import { mappingDrafts, realizeMappings } from "../MappingEditor";
import { parseLiteralJson, parseTriggerResponse, serializeTriggerPayload } from "../json";
import { SubscriptionEditor, SubscriptionsSection } from "../Subscriptions";
import { ScheduleSummary, TriggersRoute } from "../TriggersRoute";
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
const button = (container: ParentNode, text: string) => [...container.querySelectorAll<HTMLButtonElement>("button")].find(item => item.textContent === text)!;
function menuItem(text: string) {
  return [...document.body.querySelectorAll<HTMLButtonElement>('[role="menuitem"]')].find(item => item.textContent === text);
}
function openRowMenu(container: ParentNode, label: string) {
  const triggerBtn = container.querySelector<HTMLButtonElement>(`button[aria-label="${label}"]`)!;
  expect(triggerBtn, `Overflow menu "${label}" should be present`).not.toBeNull();
  flushSync(() => triggerBtn.click());
}
const control = (container: ParentNode, label: string) => container.querySelector<HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement>(`[aria-label="${label}"]`)!;
function radioCard(root: ParentNode, typeKey: string, schemaVersion = "1") {
  return root.querySelector<HTMLLabelElement>(`label.wf-trigger-type-card[data-type-key="${typeKey}"][data-schema-version="${schemaVersion}"]`)!;
}
function dialog(root: ParentNode = document.body) {
  return root.querySelector<HTMLElement>('[role="dialog"]')!;
}
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
  it("pins a newly authored expression mapping to the accepted readonly profile", () => {
    const inputs = [{ name: "Amount", type: { alias: "UInt64", collectionKind: "Single" }, isRequired: true }];
    const initial = mappingDrafts([{ inputName: "Amount", binding: { kind: "Literal", value: 7 } }]);
    const built = realizeMappings(inputs, {
      Amount: { ...initial.Amount, kind: "Expression", language: "JavaScript", source: "19" }
    });
    expect(built.errors).toEqual({});
    expect(built.mappings).toEqual([{ inputName: "Amount", binding: {
      kind: "Expression", template: { language: "JavaScript", source: "19",
        resultType: { alias: "UInt64", collectionKind: "Single" }, options: {}, capabilityProfile: "binding-readonly-v1" },
      parameters: {}
    } }]);
  });
  it("retains exact typed scalar inputs, explicit null, omission and expression options", () => {
    const inputs = [{ name: "Amount", type: { alias: "UInt64", collectionKind: "Single" }, isRequired: true }];
    const drafts = mappingDrafts([{ inputName: "Amount", binding: { kind: "Literal", value: "18446744073709551615" } }]);
    const built = realizeMappings(inputs, drafts);
    expect(built.errors).toEqual({});
    expect(serializeTriggerPayload(built.mappings)).toContain('"value":18446744073709551615');
    const expression: InputMapping = { inputName: "Amount", binding: { kind: "Expression", template: { language: "JavaScript", source: "args.value", resultType: { alias: "UInt64", collectionKind: "Single" }, options: { strict: true }, capabilityProfile: "binding-readonly-v1" }, parameters: { value: { kind: "SourceField", path: ["payload", "amount"] } } } };
    expect(realizeMappings(inputs, mappingDrafts([expression])).mappings).toEqual([expression]);
    expect(realizeMappings(inputs, { Amount: { ...drafts.Amount, kind: "Null" } }).mappings).toEqual([{ inputName: "Amount", binding: { kind: "Literal", value: null } }]);
    expect(realizeMappings(inputs, { Amount: { ...drafts.Amount, kind: "Omit" } }).mappings).toEqual([]);
  });
});

describe("mounted standalone trigger interactions (component evidence)", () => {
  it.each([true, false])("distinguishes the saved inactive schedule cursor and failure history from current firing and retry (active: %s)", isActive => {
    const { container } = mount(<ScheduleSummary schedule={{ isActive, nextDueUtc: "2026-11-07T09:00:00Z", timezone: "UTC", offset: "+00:00",
      isOverdue: true, failureReason: "NoEligibleSubscribers", nextRetryAt: "2026-11-07T09:02:00Z" }} />);
    expect(container.textContent).toContain("2026-11-07T09:00:00Z · UTC (+00:00)");
    if (isActive) {
      expect(container.textContent).toContain("Next firing UTC:");
      expect(container.textContent).toContain("Recovery eligible after: 2026-11-07T09:02:00Z");
      expect(container.textContent).toContain("will be retried");
      expect(container.textContent).not.toContain("Last recorded");
    } else {
      expect(container.textContent).toContain("Saved cursor UTC:");
      expect(container.textContent).toContain("No firing is scheduled while this schedule is inactive.");
      expect(container.textContent).toContain("Last recorded failure: no eligible subscribers.");
      expect(container.textContent).toContain("Last recorded retry time: 2026-11-07T09:02:00Z");
      expect(container.textContent).not.toContain("Next firing");
      expect(container.textContent).not.toContain("Recovery eligible after");
      expect(container.textContent).not.toContain("overdue occurrence");
      expect(container.textContent).not.toContain("will be retried");
    }
  });

  it.each(["__proto__", "constructor", "toString"])("creates a literal module configuration key %s without inherited values", key => {
    const submit = vi.fn();
    const descriptor: TriggerTypeDescriptor = { typeKey: "module-literal-key", schemaVersion: "1", displayName: "Literal key", family: "Event", canCreate: true,
      configurationFields: [{ key, label: "Module value", type: "json" }] };
    const { container } = mount(<ConfigurationForm descriptor={descriptor} onCancel={vi.fn()} onSubmit={submit} />);
    expect(control(container, "Module value").value).toBe("");
    fill(control(container, "Trigger name"), "Literal module configuration");
    fill(control(container, "Module value"), '{"nested":"retained"}');
    flushSync(() => button(container, "Create trigger").click());
    expect(submit).toHaveBeenCalledOnce();
    const configuration = submit.mock.calls[0][0].configuration;
    expect(Object.hasOwn(configuration, key)).toBe(true);
    expect(serializeTriggerPayload(configuration)).toBe(JSON.stringify(Object.fromEntries([[key, { nested: "retained" }]])));
  });
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
    openRowMenu(container, "Actions for Restricted workflow");
    expect(menuItem("Authorize")).toBeUndefined();
    expect(menuItem("Pause")?.disabled).toBe(false);
    expect(menuItem("Revoke authority")?.disabled).toBe(false);
    expect(client.mappingContext).not.toHaveBeenCalled();
    flushSync(() => menuItem("Revoke authority")!.click());
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
  it("reuses a create request only for the same exact JSON wire content and schema", async () => {
    discovery.document.capabilities[0].links.push({ rel: "types", href: "custom/types" });
    const descriptors = ["1", "2"].map(schemaVersion => ({ typeKey: "create-fingerprint", schemaVersion, displayName: `Version ${schemaVersion}`,
      family: "Event", canCreate: true, configurationFields: [{ key: "payload", label: "Payload", type: "json" }] }));
    const sent: { requestId: string; schemaVersion: string; configuration: { payload: unknown } }[] = [];
    const context = { baseUrl: "https://backend.example/", http: {
      getJson: vi.fn(async (path: string) => path.endsWith("trigger-identity") ? actor : path.includes("types") ? { items: descriptors } : { items: [] }),
      requestJson: vi.fn(async (_path: string, init: RequestInit) => { sent.push(JSON.parse(init.body as string)); throw new Error("Acknowledgement unavailable"); })
    } } as unknown as StudioEndpointContext;
    mount(<TriggersRoute api={apiStub(context)} view="create" navigate={vi.fn()} />);
    await vi.waitFor(() => expect(radioCard(document.body, "create-fingerprint", "1")).not.toBeNull());
    flushSync(() => radioCard(document.body, "create-fingerprint", "1").click());
    fill(control(dialog(), "Trigger name"), "Retryable create"); fill(control(dialog(), "Payload"), "123");
    const send = async (count: number) => {
      flushSync(() => button(dialog(), "Create trigger").click());
      await vi.waitFor(() => { expect(sent).toHaveLength(count); expect(button(dialog(), "Create trigger").disabled).toBe(false); });
    };
    await send(1); await send(2);
    expect(sent[1].requestId).toBe(sent[0].requestId);
    fill(control(dialog(), "Payload"), '{"text":"123"}'); await send(3);
    expect(sent[0].configuration.payload).toBe(123); expect(sent[2].configuration.payload).toEqual({ text: "123" });
    expect(sent[2].requestId).not.toBe(sent[0].requestId);
    flushSync(() => radioCard(document.body, "create-fingerprint", "2").click());
    fill(control(dialog(), "Trigger name"), "Retryable create"); fill(control(dialog(), "Payload"), '{"text":"123"}'); await send(4);
    expect(sent[3].schemaVersion).toBe("2"); expect(sent[3].requestId).not.toBe(sent[2].requestId);
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
    expect(button(container, "Apply reviewed change").disabled).toBe(true);
    vi.mocked(client.read).mockRejectedValueOnce(new StudioHttpError(503, "Reload unavailable"));
    flushSync(() => button(container, "Reload and review current state").click());
    await vi.waitFor(() => expect(container.textContent).toContain("Reload unavailable"));
    expect(button(container, "Apply reviewed change").disabled).toBe(true);
    expect(client.apply).toHaveBeenCalledTimes(1);
    flushSync(() => button(container, "Reload and review current state").click());
    await vi.waitFor(() => expect(client.preview).toHaveBeenLastCalledWith("trigger-1", 2, { kind: "PauseSubscription", subscriptionId: "sub-1", expectedSubscriptionRevision: 4 }, expect.any(AbortSignal)));
    await vi.waitFor(() => expect(button(container, "Apply reviewed change")?.disabled).toBe(false));
    flushSync(() => button(container, "Apply reviewed change").click());
    await vi.waitFor(() => expect(saved).toHaveBeenCalledTimes(1));
  });
  it("allows the same reviewed request to be retried after an unconfirmed transient failure", async () => {
    const client = { preview: vi.fn(async () => preview), apply: vi.fn().mockRejectedValueOnce(new StudioHttpError(503, "Response unavailable")).mockResolvedValue({ requestId: "retry", disposition: "AlreadyApplied", revision: 2 }) } as unknown as TriggerClient;
    const saved = vi.fn();
    const { container } = mount(<ChangeReview api={apiStub()} client={client} trigger={trigger()} command={{ kind: "PauseTrigger" }} onCancel={vi.fn()} onApplied={saved} />);
    await vi.waitFor(() => expect(button(container, "Apply reviewed change")?.disabled).toBe(false));
    flushSync(() => button(container, "Apply reviewed change").click());
    await vi.waitFor(() => expect(container.textContent).toContain("Response unavailable"));
    expect(button(container, "Apply reviewed change").disabled).toBe(false);
    flushSync(() => button(container, "Apply reviewed change").click());
    await vi.waitFor(() => expect(saved).toHaveBeenCalledTimes(1));
    expect(vi.mocked(client.apply).mock.calls[1]).toEqual(vi.mocked(client.apply).mock.calls[0]);
  });
  it("edits one canonical association from the trigger surface and reloads the same state in workflow context", async () => {
    let current = subscription();
    const client = { subscriptions: vi.fn(async () => ({ items: [current] })), read: vi.fn(async () => trigger()), preview: vi.fn(async () => preview), apply: vi.fn(async () => { current = { ...current, revision: 2, enabled: false }; return { requestId: "pause", disposition: "Applied", revision: 2 }; }) } as unknown as TriggerClient;
    const { container, render } = mount(<SubscriptionsSection api={apiStub()} client={client} actor={actor} trigger={trigger()} />);
    await vi.waitFor(() => { expect(container.textContent).toContain("Enabled"); expect(container.textContent).toContain("Valid"); });
    openRowMenu(container, "Actions for reconcile");
    flushSync(() => menuItem("Pause")!.click());
    await vi.waitFor(() => expect(dialog()).not.toBeNull());
    await vi.waitFor(() => expect(button(dialog(), "Apply reviewed change")?.disabled).toBe(false));
    flushSync(() => button(dialog(), "Apply reviewed change").click());
    await vi.waitFor(() => { expect(container.textContent).toContain("Paused"); expect(container.textContent).toContain("Valid"); });
    render(<SubscriptionsSection key="workflow" api={apiStub()} client={client} actor={actor} definitionId="reconcile" />);
    await vi.waitFor(() => { expect(container.textContent).toContain("Paused"); expect(container.textContent).toContain("Valid"); });
    expect(container.textContent).toContain("independently of this draft or historical graph");
    expect(client.subscriptions).toHaveBeenLastCalledWith({ definitionId: "reconcile", after: undefined }, expect.any(AbortSignal));
  });
  it.each([[2, "Not recorded"], [0, "Not accepted"]] as const)("reports missing acceptance metadata truthfully with %s accepted recipients", async (acceptedCount, expected) => {
    const delivery: DeliveryView = { id: "delivery-metadata", revision: 1, triggerId: "trigger-1", triggerRevision: 1,
      admission: acceptedCount ? "Accepted" : "Refused", acceptedCount, refusedCount: acceptedCount ? 0 : 1,
      recipients: [], findings: [] };
    const client = { delivery: vi.fn().mockResolvedValue(delivery) } as unknown as TriggerClient;
    const { container } = mount(<DeliveryDetails api={apiStub()} client={client} deliveryId={delivery.id} navigate={vi.fn()} />);
    await vi.waitFor(() => expect(container.querySelector("dl")).not.toBeNull());
    const value = (label: string) => [...container.querySelectorAll("dt")].find(item => item.textContent === label)?.nextElementSibling?.textContent;
    expect(value("Accepted at")).toBe(expected);
    expect(value("Accepted trigger revision")).toBe(expected);
  });
  it("names a readable recipient while retaining its exact copyable workflow identity", () => {
    const recipient: RecipientView = { id: "recipient-label", revision: 1, target: { definitionId: "opaque-definition-id", slotName: "default" },
      targetDisplayName: "Reconcile Accounts", admission: "Accepted", dispatch: "NotDispatched", findings: [], actions: [] };
    const { container } = mount(<RecipientEvidence recipient={recipient} />);
    expect(container.querySelector("h3")?.textContent).toBe("Reconcile Accounts");
    expect(container.querySelector('[aria-label="Workflow definition ID: opaque-definition-id"]')).not.toBeNull();
    expect(container.querySelector('[aria-label="Copy Workflow definition ID"]')).not.toBeNull();
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
    await vi.waitFor(() => expect(dialog()).not.toBeNull());
    await vi.waitFor(() => expect(client.preview).toHaveBeenCalledWith("trigger-1", 7,
      { kind: "CancelPendingRecipients", deliveryId: "delivery-1", expectedDeliveryRevision: 4, recipients: [{ recipientId: "recipient-1", expectedRevision: 2 }] }, expect.any(AbortSignal)));
    flushSync(() => button(dialog(), "Reload and review current state").click());
    await vi.waitFor(() => expect(client.preview).toHaveBeenLastCalledWith("trigger-1", 8,
      { kind: "CancelPendingRecipients", deliveryId: "delivery-1", expectedDeliveryRevision: 5, recipients: [{ recipientId: "recipient-1", expectedRevision: 3 }] }, expect.any(AbortSignal)));
    await vi.waitFor(() => expect(button(dialog(), "Apply reviewed change")?.disabled).toBe(false));
    flushSync(() => button(dialog(), "Apply reviewed change").click());
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


describe("review regression cases", () => {
  const descriptor: TriggerTypeDescriptor = { typeKey: "acme-event", schemaVersion: "1", displayName: "Acme", family: "Event", canCreate: true,
    configurationFields: [{ key: "amount", label: "Amount", type: "number" }, { key: "nullable", label: "Nullable", type: "json", defaultValue: "fallback" }] };
  it("keeps exact saved numeric tokens and explicit null through an unrelated edit", () => {
    const submit = vi.fn();
    const configuration = parseLiteralJson('{"amount":0.1234567890123456789012345678,"nullable":null}') as TriggerView["configuration"];
    const { container } = mount(<ConfigurationForm descriptor={descriptor} trigger={{ ...trigger(), configuration }} onCancel={vi.fn()} onSubmit={submit} />);
    fill(control(container, "Trigger name"), "Renamed trigger");
    flushSync(() => button(container, "Review changes").click());
    const saved = serializeTriggerPayload(submit.mock.calls[0][0].configuration);
    expect(saved).toContain('"amount":0.1234567890123456789012345678');
    expect(JSON.parse(saved).nullable).toBeNull();
    fill(control(container, "Amount"), "0.9876543210987654321098765432");
    flushSync(() => button(container, "Review changes").click());
    expect(serializeTriggerPayload(submit.mock.calls[1][0].configuration)).toContain('"amount":0.9876543210987654321098765432');
  });
  it("gives an independent editor ordinary JSON numbers and keeps unchanged numeric tokens", () => {
    const submit = vi.fn(); const seen = vi.fn();
    const editor: StudioTriggerConfigurationEditorContribution = { id: "ordinary-json", typeKey: descriptor.typeKey, schemaVersion: "1",
      component: ({ configuration, onChange }) => { seen(configuration.amount); return <button type="button" onClick={() => onChange(JSON.parse(JSON.stringify(configuration)))}>Clone public JSON</button>; } };
    const configuration = parseLiteralJson('{"amount":5.0,"nullable":null}') as TriggerView["configuration"];
    const { container } = mount(<ConfigurationForm descriptor={descriptor} trigger={{ ...trigger(), configuration }} editors={[editor]} onCancel={vi.fn()} onSubmit={submit} />);
    expect(seen).toHaveBeenCalledWith(5);
    flushSync(() => button(container, "Clone public JSON").click()); flushSync(() => button(container, "Review changes").click());
    expect(serializeTriggerPayload(submit.mock.calls[0][0].configuration)).toContain('"amount":5.0');
  });
  it("uses the standard fields when a public editor cannot preserve a saved number", () => {
    const render = vi.fn(() => <p>Custom editor</p>);
    const configuration = parseLiteralJson('{"amount":18446744073709551615}') as TriggerView["configuration"];
    const { container } = mount(<ConfigurationForm descriptor={descriptor} trigger={{ ...trigger(), configuration }} editors={[{ id: "ordinary-json", typeKey: descriptor.typeKey, schemaVersion: "1", component: render }]} onCancel={vi.fn()} onSubmit={vi.fn()} />);
    expect(render).not.toHaveBeenCalled(); expect(control(container, "Amount").value).toBe("18446744073709551615");
    expect(container.textContent).toContain("preserve the precision");
  });
  it.each([false, true])("protects dotted and nested sensitive fields in both forms (custom: %s)", custom => {
    const submit = vi.fn();
    const protectedDescriptor: TriggerTypeDescriptor = { ...descriptor, configurationFields: [
      { key: "auth", label: "Authentication", type: "json" }, { key: "auth.password", label: "Password", type: "string", sensitive: true }] };
    const editor: StudioTriggerConfigurationEditorContribution = { id: "protected", typeKey: descriptor.typeKey, schemaVersion: "1",
      component: ({ onChange }) => <button type="button" onClick={() => onChange({ auth: { label: "changed", password: "replacement" }, "auth.password": "flat replacement" })}>Change authentication</button> };
    const { container } = mount(<ConfigurationForm descriptor={protectedDescriptor} trigger={{ ...trigger(), configuration: { auth: { label: "saved" } }, configurationRedactions: [{ path: ["auth", "password"] }] }} editors={custom ? [editor] : []} onCancel={vi.fn()} onSubmit={submit} />);
    if (custom) flushSync(() => button(container, "Change authentication").click());
    else fill(control(container, "Authentication"), '{"label":"changed","password":"replacement"}');
    flushSync(() => button(container, "Review changes").click());
    expect(submit.mock.calls[0][0].configuration).toEqual({ auth: { label: "changed" } });
  });
  it("reopens a saved JSON string mapping as JSON while keeping String inputs as plain text", () => {
    const mappings: InputMapping[] = [{ inputName: "json", binding: { kind: "Literal", value: "hello" } }, { inputName: "text", binding: { kind: "Literal", value: "hello" } }];
    const inputs = [{ name: "json", type: { alias: "JsonElement", collectionKind: "Single" }, isRequired: true }, { name: "text", type: { alias: "String", collectionKind: "Single" }, isRequired: true }];
    expect(realizeMappings(inputs, mappingDrafts(mappings))).toEqual({ mappings, errors: {} });
  });
  it("retains real DTO-shaped field errors and associates them with the preserved form", async () => {
    const context = { baseUrl: "https://backend.example/shell/", http: { requestResponse: vi.fn(async () => new Response(JSON.stringify({ code: "validation", message: "Correct the trigger configuration.", errors: { amount: "Amount must be positive." } }), { status: 400 })) } } as unknown as StudioEndpointContext;
    let error: StudioHttpError | undefined;
    try { await triggerClient(context).create({ requestId: "request", name: "Saved edits", typeKey: descriptor.typeKey, schemaVersion: "1", configuration: { amount: -1 } }); } catch (caught) { error = caught as StudioHttpError; }
    expect(error?.validationErrors).toEqual({ amount: ["Amount must be positive."] });
    const { container } = mount(<ConfigurationForm descriptor={descriptor} trigger={{ ...trigger(), configuration: { amount: -1 } }} serverErrors={error!.validationErrors!} onCancel={vi.fn()} onSubmit={vi.fn()} />);
    const input = control(container, "Amount"); expect(input.value).toBe("-1"); expect(input.getAttribute("aria-invalid")).toBe("true");
    expect(input.getAttribute("aria-describedby")!.split(/\s+/).some(id => document.getElementById(id)?.textContent?.includes("Amount must be positive."))).toBe(true);
  });
  it.each(['{1:2}', '{-1:2}', '{1.2:2}', '{1e2:2}'])("rejects invalid original JSON syntax %s", text => {
    expect(() => parseLiteralJson(text)).toThrow();
  });
  it.each([false, true])("edits a saved JSON configuration string with its quoted type intact (fallback: %s)", async fallback => {
    const submit = vi.fn();
    const fail = vi.fn(() => { throw new Error("optional editor unavailable"); });
    vi.spyOn(console, "error").mockImplementation(() => {});
    const { container } = mount(<ConfigurationForm descriptor={descriptor} trigger={{ ...trigger(), configuration: { nullable: "123" } }}
      editors={fallback ? [{ id: "failing", typeKey: descriptor.typeKey, schemaVersion: "1", component: fail }] : []} onCancel={vi.fn()} onSubmit={submit} />);
    await vi.waitFor(() => expect(control(container, "Nullable")).not.toBeNull());
    expect(control(container, "Nullable").value).toBe('"123"');
    fill(control(container, "Nullable"), '"456"'); flushSync(() => button(container, "Review changes").click());
    expect(submit.mock.calls[0][0].configuration.nullable).toBe("456");
  });
  it("can authorize a retarget without inheriting the previous target's execution denial", async () => {
    const resource = { ...trigger(), actions: ["AddSubscription", "OperateSubscriptions", "AuthorizeSubscriptions"].map(action => ({ action, allowed: true })) };
    const original = { ...subscription(), actions: [...subscription().actions, { action: "GrantInvocation", allowed: false }] };
    const client = { mappingContext: vi.fn(async (_id: string, _revision: number, target: { definitionId: string; slotName: string }) => ({ target: { ...target, state: "Available", findings: [], inputContract: { version: 1, inputs: [] } }, sourceSchema: { fields: [] }, supportedExpressions: [], findings: [] })), preview: vi.fn(async () => preview) } as unknown as TriggerClient;
    const { container } = mount(<SubscriptionEditor api={apiStub()} client={client} actor={actor} trigger={resource} subscription={original} fixedDefinitionId="reconcile" onCancel={vi.fn()} onSaved={vi.fn()} />);
    await vi.waitFor(() => expect(button(container, "Review subscription")?.disabled).toBe(false));
    const grant = [...container.querySelectorAll<HTMLInputElement>('input[type="checkbox"]')].find(input => input.closest("label")?.textContent?.includes("Authorize this shared trigger"))!;
    expect(grant.disabled).toBe(true);
    fill(control(container, "Publication slot"), "new-slot");
    await vi.waitFor(() => expect(grant.disabled).toBe(false));
    await vi.waitFor(() => expect(button(container, "Review subscription")?.disabled).toBe(false));
    flushSync(() => grant.click()); flushSync(() => button(container, "Review subscription").click());
    await vi.waitFor(() => expect(client.preview).toHaveBeenCalledWith(resource.id, resource.revision,
      expect.objectContaining({ target: { definitionId: "reconcile", slotName: "new-slot" }, invocationRequest: "Grant" }), expect.any(AbortSignal)));
  });
  it("keeps the latest selected association when Edit responses arrive out of order", async () => {
    let resolveA!: (value: TriggerView) => void, resolveB!: (value: TriggerView) => void;
    const first = new Promise<TriggerView>(resolve => { resolveA = resolve; });
    const second = new Promise<TriggerView>(resolve => { resolveB = resolve; });
    const rows = ["a", "b"].map(letter => ({ ...subscription(), id: "sub-" + letter, target: { definitionId: "definition-" + letter, slotName: "default" },
      mappings: [{ inputName: "Note", binding: { kind: "Literal" as const, value: letter } }] }));
    const client = { subscriptions: vi.fn(async () => ({ items: rows })), read: vi.fn().mockReturnValueOnce(first).mockReturnValueOnce(second),
      targets: vi.fn(async () => ({ items: [] })), mappingContext: vi.fn(async (_id: string, _revision: number, target: { definitionId: string; slotName: string }) =>
        ({ target: { ...target, state: "Available", findings: [], inputContract: { version: 1, inputs: [{ name: "Note", type: { alias: "String", collectionKind: "Single" }, isRequired: false }] } }, sourceSchema: { fields: [] }, supportedExpressions: [], findings: [] })),
      preview: vi.fn(async () => preview) } as unknown as TriggerClient;
    const { container } = mount(<SubscriptionsSection api={apiStub()} client={client} actor={actor} trigger={trigger()} />);
    await vi.waitFor(() => expect([...container.querySelectorAll("button")].filter(item => item.textContent === "Edit")).toHaveLength(2));
    const edits = [...container.querySelectorAll<HTMLButtonElement>("button")].filter(item => item.textContent === "Edit");
    flushSync(() => edits[0].click()); flushSync(() => edits[1].click());
    resolveB(trigger());
    await vi.waitFor(() => expect(dialog()).not.toBeNull());
    await vi.waitFor(() => expect(control(dialog(), "Configured value for Note")?.value).toBe("b"));
    resolveA(trigger()); await first; await new Promise(resolve => setTimeout(resolve, 20));
    await vi.waitFor(() => expect(button(dialog(), "Review subscription")?.disabled).toBe(false));
    flushSync(() => button(dialog(), "Review subscription").click());
    await vi.waitFor(() => expect(client.preview).toHaveBeenCalledWith(trigger().id, 1, expect.objectContaining({
      kind: "SaveSubscription", subscriptionId: "sub-b", target: { definitionId: "definition-b", slotName: "default" },
      mappings: [{ inputName: "Note", binding: { kind: "Literal", value: "b" } }]
    }), expect.any(AbortSignal)));
  });
  it("keeps permission-filtered trigger pages navigable without global read rights", async () => {
    const noGlobalRights = { ...actor, canRead: false, canManage: false, canOperate: false, canAuthorize: false };
    const context = { baseUrl: "https://backend.example/shell/", http: { getJson: vi.fn(async (path: string) => path.includes("trigger-identity") ? noGlobalRights : { items: [], next: "filtered-page" }) } } as unknown as StudioEndpointContext;
    const { container } = mount(<TriggersRoute api={apiStub(context)} view="list" navigate={vi.fn()} />);
    await vi.waitFor(() => expect(button(container, "Next triggers")).toBeDefined());
    expect(container.textContent).toContain("No accessible triggers on this page");
    expect(container.textContent).not.toContain("Trigger access unavailable");
    flushSync(() => button(container, "Next triggers").click());
    await vi.waitFor(() => expect(context.http.getJson).toHaveBeenLastCalledWith(expect.stringContaining("after=filtered-page"), expect.any(Object)));
  });
  it("returns preview field reasons to the preserved configuration editor", async () => {
    const cancel = vi.fn();
    const client = { preview: vi.fn(async () => { throw new StudioHttpError(400, "Correct the trigger configuration.", { amount: ["Amount must be positive."] }); }) } as unknown as TriggerClient;
    const { container } = mount(<ChangeReview api={apiStub()} client={client} trigger={trigger()} command={{ kind: "SaveConfiguration", name: "Edited name", configuration: { amount: -1 } }} onCancel={cancel} onApplied={vi.fn()} />);
    await vi.waitFor(() => expect(container.textContent).toContain("amount: Amount must be positive."));
    flushSync(() => button(container, "Back to editing").click());
    expect(cancel).toHaveBeenCalledWith({ amount: ["Amount must be positive."] });
  });
  it("keeps delivery pagination usable when the current page has no visible rows", async () => {
    const client = { deliveries: vi.fn(async () => ({ items: [], next: "next-visible-page" })) } as unknown as TriggerClient;
    const { container } = mount(<DeliveriesSection client={client} trigger={trigger()} />);
    await vi.waitFor(() => expect(button(container, "Next deliveries")).toBeDefined());
    flushSync(() => button(container, "Next deliveries").click());
    await vi.waitFor(() => expect(client.deliveries).toHaveBeenLastCalledWith({ triggerId: trigger().id, after: "next-visible-page" }, expect.any(AbortSignal)));
  });
  it("uses resource actions rather than null-resource actor flags in the subscription editor", async () => {
    const resource = { ...trigger(), actions: ["AddSubscription", "OperateSubscriptions", "AuthorizeSubscriptions"].map(action => ({ action, allowed: true })) };
    const noGlobalRights = Object.fromEntries(Object.entries(actor).map(([key, value]) => [key, typeof value === "boolean" ? false : value])) as unknown as ActorView;
    const client = { mappingContext: vi.fn(async () => ({ target: { definitionId: "reconcile", slotName: "default", state: "Available", findings: [], inputContract: { version: 1, inputs: [] } }, sourceSchema: { fields: [] }, supportedExpressions: [], findings: [] })), preview: vi.fn(async () => preview) } as unknown as TriggerClient;
    const { container } = mount(<SubscriptionEditor api={apiStub()} client={client} actor={noGlobalRights} trigger={resource} fixedDefinitionId="reconcile" onCancel={vi.fn()} onSaved={vi.fn()} />);
    await vi.waitFor(() => expect(button(container, "Review subscription")?.disabled).toBe(false));
    expect([...container.querySelectorAll<HTMLInputElement>('input[type="checkbox"]')].every(input => !input.disabled)).toBe(true);
    flushSync(() => button(container, "Review subscription").click());
    await vi.waitFor(() => expect(client.preview).toHaveBeenCalled());
  });
  it("renders a consistent collection empty state without a zero-count footer and opens a card-based create modal that navigates to the new trigger", async () => {
    discovery.document.capabilities[0].links.push({ rel: "types", href: "custom/types" });
    const descriptors: TriggerTypeDescriptor[] = [
      { typeKey: "cron", schemaVersion: "1", displayName: "Cron", family: "Cron", canCreate: true, configurationFields: [{ key: "expression", label: "Cron expression", type: "string", required: true }] },
      { typeKey: "http", schemaVersion: "1", displayName: "HTTP", family: "Http", canCreate: true, configurationFields: [{ key: "route", label: "Route", type: "string", required: true }] },
      { typeKey: "acme.webhook", schemaVersion: "1", displayName: "Acme Webhook", family: "Custom", canCreate: true, configurationFields: [{ key: "channel", label: "Channel", type: "string", required: true }] },
      { typeKey: "disabled-source", schemaVersion: "1", displayName: "Disabled Source", family: "Event", canCreate: false, configurationFields: [] }
    ];
    const CustomIcon = ({ size }: { size?: number }) => <svg data-testid="acme-icon" width={size} height={size} />;
    const presentations = createContributionRegistry<StudioTriggerTypePresentationContribution>({ slot: studioSlots.triggerTypePresentations });
    presentations.add({
      id: "acme.webhook.presentation",
      typeKey: "acme.webhook",
      displayName: "Acme Custom Webhook",
      description: "Receives signed webhooks from Acme.",
      icon: CustomIcon,
      accent: "pink",
      category: "Partner",
      order: 5
    });
    const created: TriggerView = { ...trigger(), id: "trigger-created-42", name: "Acme Hook", typeKey: "acme.webhook", schemaVersion: "1", configuration: { channel: "orders" } };
    const context = { baseUrl: "https://backend.example/", http: {
      getJson: vi.fn(async (path: string) => path.endsWith("trigger-identity") ? actor : path.includes("types") ? { items: descriptors } : { items: [] }),
      requestJson: vi.fn(async () => created)
    } } as unknown as StudioEndpointContext;
    const navigate = vi.fn();
    const api = { ...apiStub(context), triggerTypePresentations: presentations } as unknown as ElsaStudioModuleApi;
    const { container } = mount(<TriggersRoute api={api} view="list" navigate={navigate} />);

    await vi.waitFor(() => expect(container.querySelector(".studio-collection-empty")).not.toBeNull());
    expect(container.querySelector(".studio-page[data-studio-collection]")).not.toBeNull();
    expect(container.querySelector(".studio-collection-footer")).toBeNull();

    flushSync(() => button(container, "Create trigger").click());
    await vi.waitFor(() => expect(dialog()).not.toBeNull());
    await vi.waitFor(() => expect(dialog().querySelectorAll(".wf-trigger-type-card")).toHaveLength(4));

    const cards = [...dialog().querySelectorAll<HTMLLabelElement>(".wf-trigger-type-card")];
    expect(cards.map(card => card.dataset.typeKey)).toEqual(["acme.webhook", "http", "disabled-source", "cron"]);
    expect(cards[0].textContent).toContain("Acme Custom Webhook");
    expect(cards[0].textContent).toContain("Receives signed webhooks from Acme.");
    expect(cards[0].textContent).toContain("Partner");
    expect(cards[0].querySelector('[data-testid="acme-icon"]')).not.toBeNull();
    expect(cards[2].getAttribute("aria-disabled")).toBe("true");
    expect(cards[2].querySelector<HTMLInputElement>('input[type="radio"]')!.disabled).toBe(true);

    flushSync(() => radioCard(dialog(), "acme.webhook", "1").click());
    fill(control(dialog(), "Trigger name"), "Acme Hook");
    fill(control(dialog(), "Channel"), "orders");
    flushSync(() => button(dialog(), "Create trigger").click());

    await vi.waitFor(() => expect(navigate).toHaveBeenCalledWith("/workflows/triggers/trigger-created-42"));
  });
  it("supports interactive row click, explicit Manage action and Edit configuration deep link on populated trigger lists", async () => {
    discovery.document.capabilities[0].links.push({ rel: "types", href: "custom/types" });
    const rowTrigger: TriggerView = {
      ...trigger(),
      id: "trigger-99",
      name: "Fire Starter",
      typeKey: "http",
      schemaVersion: "1",
      http: { mode: "AsyncFanOut", admissionPolicy: "RequireAll", proposedEndpoint: "fire-starter", reservationState: "Unreserved" },
      actions: [{ action: "SaveConfiguration", allowed: true }]
    };
    const descriptors: TriggerTypeDescriptor[] = [
      { typeKey: "http", schemaVersion: "1", displayName: "HTTP Endpoint", family: "Http", canCreate: true, configurationFields: [{ key: "route", label: "Route", type: "string", required: true }] }
    ];
    const context = { baseUrl: "https://backend.example/", http: {
      getJson: vi.fn(async (path: string) => path.endsWith("trigger-identity") ? actor : path.includes("types") ? { items: descriptors } : { items: [rowTrigger] })
    } } as unknown as StudioEndpointContext;
    const navigate = vi.fn();
    const { container } = mount(<TriggersRoute api={apiStub(context)} view="list" navigate={navigate} />);

    await vi.waitFor(() => expect(container.querySelector("tbody tr")).not.toBeNull());
    await vi.waitFor(() => expect(container.textContent).toContain("HTTP Endpoint"));
    expect(container.textContent).toContain("1 trigger shown");

    const row = container.querySelector<HTMLTableRowElement>("tbody tr")!;
    expect(row.hasAttribute("data-studio-row-interactive")).toBe(true);
    flushSync(() => row.click());
    expect(navigate).toHaveBeenCalledWith("/workflows/triggers/trigger-99");

    navigate.mockClear();
    flushSync(() => button(container, "Manage").click());
    expect(navigate).toHaveBeenCalledWith("/workflows/triggers/trigger-99");

    navigate.mockClear();
    const menuTrigger = container.querySelector<HTMLButtonElement>('button[aria-label="Actions for Fire Starter"]')!;
    flushSync(() => menuTrigger.click());
    const editItem = [...document.body.querySelectorAll<HTMLButtonElement>('[role="menuitem"]')].find(item => item.textContent === "Edit configuration")!;
    expect(editItem).toBeDefined();
    flushSync(() => editItem.click());
    expect(navigate).toHaveBeenCalledWith("/workflows/triggers/trigger-99?edit=configuration");
  });
  it("supports co-located trigger presentation metadata on configuration editor contributions and opens edit mode from ?edit=configuration", async () => {
    discovery.document.capabilities[0].links.push({ rel: "types", href: "custom/types" });
    const EditorIcon = ({ size }: { size?: number }) => <svg data-testid="editor-icon" width={size} height={size} />;
    const editors = createContributionRegistry<StudioTriggerConfigurationEditorContribution>({ slot: studioSlots.triggerConfigurationEditors });
    editors.add({
      id: "acme.co-located",
      typeKey: "acme.signal",
      schemaVersion: "1",
      component: ({ configuration, onChange }) => <button type="button" onClick={() => onChange({ ...configuration, topic: "updated" })}>Update signal</button>,
      presentation: {
        displayName: "Acme Signal",
        description: "Delivered via co-located editor presentation.",
        icon: EditorIcon,
        accent: "teal",
        category: "Signal"
      }
    });
    const descriptors: TriggerTypeDescriptor[] = [
      { typeKey: "acme.signal", schemaVersion: "1", displayName: "Raw Signal", family: "Event", canCreate: true, configurationEditorId: "acme.co-located", configurationFields: [{ key: "topic", label: "Topic", type: "string" }] }
    ];
    const editableTrigger: TriggerView = {
      ...trigger(),
      id: "trigger-edit-1",
      name: "Signal Trigger",
      typeKey: "acme.signal",
      schemaVersion: "1",
      configuration: { topic: "initial" },
      actions: [{ action: "SaveConfiguration", allowed: true }]
    };
    const context = { baseUrl: "https://backend.example/", http: {
      getJson: vi.fn(async (path: string) => path.endsWith("trigger-identity") ? actor : path.includes("types") ? { items: descriptors } : path.includes("trigger-edit-1") ? editableTrigger : { items: [] })
    } } as unknown as StudioEndpointContext;
    const api = { ...apiStub(context), triggerConfigurationEditors: editors } as unknown as ElsaStudioModuleApi;

    mount(<TriggersRoute api={api} view="create" navigate={vi.fn()} />);
    await vi.waitFor(() => expect(radioCard(document.body, "acme.signal", "1")).not.toBeNull());
    const card = radioCard(document.body, "acme.signal", "1");
    expect(card.textContent).toContain("Acme Signal");
    expect(card.textContent).toContain("Delivered via co-located editor presentation.");
    expect(card.querySelector('[data-testid="editor-icon"]')).not.toBeNull();

    window.history.pushState({}, "", "/workflows/triggers/trigger-edit-1?edit=configuration");
    const { container } = mount(<TriggersRoute api={api} view="details" navigate={vi.fn()} />);
    const editDialog = () => [...document.body.querySelectorAll<HTMLElement>('[role="dialog"]')].find(item => item.textContent?.includes("Edit trigger configuration"))!;
    await vi.waitFor(() => expect(editDialog()).toBeDefined());
    await vi.waitFor(() => expect(button(editDialog(), "Review changes")).toBeDefined());
    expect(control(editDialog(), "Trigger name").value).toBe("Signal Trigger");
    expect(button(editDialog(), "Update signal")).toBeDefined();
    // Details cards and tabs remain mounted behind the Edit configuration modal.
    expect(container.querySelector('[aria-label="Trigger overview"]')).not.toBeNull();
    expect(container.querySelector('[role="tablist"][aria-label="Trigger workspace sections"]')).not.toBeNull();
    window.history.pushState({}, "", "/");
  });
  it("renders TriggerDetails with surface cards, tabbed Subscriptions/Deliveries workspace, and modal trigger picker in workflow direction", async () => {
    discovery.document.capabilities[0].links.push(
      { rel: "types", href: "custom/types" },
      { rel: "subscriptions", href: "custom/subscriptions" },
      { rel: "deliveries", href: "custom/deliveries" }
    );
    const detailTrigger: TriggerView = {
      ...trigger(),
      id: "trigger-tabs-1",
      name: "Webhook Hub",
      typeKey: "http",
      schemaVersion: "1",
      configuration: { path: "hooks/hub" },
      http: { mode: "AsyncFanOut", admissionPolicy: "RequireAll", proposedEndpoint: "hooks/hub", reservationState: "Unreserved" },
      actions: [
        { action: "SaveConfiguration", allowed: true },
        { action: "EnableTrigger", allowed: true },
        { action: "AddSubscription", allowed: true },
        { action: "ReadDeliveries", allowed: true }
      ]
    };
    const context = {
      baseUrl: "https://backend.example/",
      http: {
        getJson: vi.fn(async (path: string) => {
          if (path.endsWith("trigger-identity")) return actor;
          if (path.includes("types")) return { items: [{ typeKey: "http", schemaVersion: "1", displayName: "HTTP Endpoint", family: "Http", canCreate: true, configurationFields: [{ key: "path", label: "Route", type: "string" }] }] };
          if (path.includes("subscriptions") || path.includes("deliveries")) return { items: [] };
          if (path.includes("trigger-tabs-1")) return detailTrigger;
          return { items: [] };
        })
      }
    } as unknown as StudioEndpointContext;
    window.history.pushState({}, "", "/workflows/triggers/trigger-tabs-1");
    const { container } = mount(<TriggersRoute api={apiStub(context)} view="details" navigate={vi.fn()} />);

    await vi.waitFor(() => expect(container.querySelector('[aria-label="Trigger overview"]')).not.toBeNull());
    expect(container.querySelector('[aria-label="HTTP mode and endpoint"]')).not.toBeNull();
    expect(container.textContent).toContain("No live associations");
    // Deliveries tab is lazy-loaded when selected.
    const deliveriesTab = [...container.querySelectorAll<HTMLButtonElement>('[role="tab"]')].find(tab => tab.textContent?.includes("Deliveries"))!;
    expect(deliveriesTab).toBeDefined();
    flushSync(() => deliveriesTab.click());
    await vi.waitFor(() => expect(container.textContent).toContain("No delivery evidence yet"));

    // Workflow Inspector's Add trigger opens a card-based TriggerPickerDialog modal.
    const pickerClient = {
      subscriptions: vi.fn(async () => ({ items: [] })),
      list: vi.fn(async () => ({ items: [detailTrigger] })),
      types: vi.fn(async () => ({ items: [{ typeKey: "http", schemaVersion: "1", displayName: "HTTP Endpoint", family: "Http", canCreate: true, configurationFields: [] }] })),
      targets: vi.fn(async () => ({ items: [] })),
      mappingContext: vi.fn(async () => ({ target: { definitionId: "wf-1", slotName: "default", state: "Available", findings: [], inputContract: { version: 1, inputs: [] } }, sourceSchema: { fields: [] }, supportedExpressions: [], findings: [] }))
    } as unknown as TriggerClient;
    const { container: workflowContainer } = mount(<SubscriptionsSection api={apiStub(context)} client={pickerClient} actor={actor} definitionId="wf-1" />);
    await vi.waitFor(() => expect(button(workflowContainer, "Add trigger")).toBeDefined());
    flushSync(() => button(workflowContainer, "Add trigger").click());
    await vi.waitFor(() => expect(dialog()).not.toBeNull());
    await vi.waitFor(() => expect(dialog().querySelector(".wf-trigger-picker-card")?.textContent).toContain("Webhook Hub"));
    expect(dialog().querySelector(".wf-trigger-picker-card")?.textContent).toContain("HTTP Endpoint");
    window.history.pushState({}, "", "/");
  });
  it("renders inline table findings with semantic tones in the Triggers list and panel findings on Trigger details", async () => {
    discovery.document.capabilities[0].links.push(
      { rel: "types", href: "custom/types" },
      { rel: "subscriptions", href: "custom/subscriptions" }
    );
    const rows: TriggerView[] = [
      {
        ...trigger(),
        id: "trigger-cron-1",
        name: "Heartbeat",
        typeKey: "cron",
        findings: [{ code: "ScheduleCursorInactive", message: "This saved cursor is inactive; no future firing is scheduled." }]
      },
      {
        ...trigger(),
        id: "trigger-http-1",
        name: "Webhook",
        typeKey: "http",
        findings: [
          { code: "ScheduleAdmissionUnavailable", message: "Admission is temporarily unavailable." },
          { code: "HttpRouteConflict", message: "Route is already reserved." }
        ]
      }
    ];
    const context = {
      baseUrl: "https://backend.example/",
      http: {
        getJson: vi.fn(async (path: string) => {
          if (path.endsWith("trigger-identity")) return actor;
          if (path.includes("types") || path.includes("subscriptions")) return { items: [] };
          if (path.includes("trigger-cron-1")) return rows[0];
          return { items: rows };
        })
      }
    } as unknown as StudioEndpointContext;

    const { container } = mount(<TriggersRoute api={apiStub(context)} view="list" navigate={vi.fn()} />);
    await vi.waitFor(() => expect(container.querySelectorAll(".wf-trigger-findings[data-variant=\"inline\"]")).toHaveLength(2));
    const inlineFindings = [...container.querySelectorAll<HTMLLIElement>('.wf-trigger-findings[data-variant="inline"] .wf-trigger-finding')];
    expect(inlineFindings.map(item => item.dataset.tone)).toEqual(["info", "warning", "danger"]);
    expect(inlineFindings[0].textContent).toContain("This saved cursor is inactive; no future firing is scheduled.");

    window.history.pushState({}, "", "/workflows/triggers/trigger-cron-1");
    const { container: detailContainer } = mount(<TriggersRoute api={apiStub(context)} view="details" navigate={vi.fn()} />);
    await vi.waitFor(() => expect(detailContainer.querySelector('.wf-trigger-findings[data-variant="panel"] .wf-trigger-finding[data-tone="info"]')).not.toBeNull());
    window.history.pushState({}, "", "/");
  });
});
