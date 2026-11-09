import React from "react";
import { flushSync } from "react-dom";
import { createRoot, type Root } from "react-dom/client";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { afterEach, describe, expect, it, vi } from "vitest";
import type { StudioActivityDiagnosticFocusResult, StudioEndpointContext } from "@elsa-workflows/studio-sdk";
import { ActivityDefinitionPublicationReview } from "../ActivityDefinitionPublicationReview";
import { clearApiCapabilityCache } from "../api/capabilities";

const unmounts: Array<() => void> = [];

afterEach(() => {
  unmounts.splice(0).forEach(unmount => unmount());
  clearApiCapabilityCache();
  document.body.replaceChildren();
});

describe("ActivityDefinitionPublicationReview toolbar mode", () => {
  it.each(["Error", "error"])("leads with deduplicated actionable blockers and explains the disabled publish action (%s)", async severity => {
    const diagnostic = {
      code: "specialist.manifest.model", severity,
      message: "A complete model connection has not been selected.",
      remediation: "Choose a model connection with a configured model in Designer.",
      subject: { kind: "ActivityDefinition", id: "elsa.specialist" },
      location: { providerKey: "elsa.specialist", jsonPointer: "/model" },
      metadata: { actionLabel: "Choose model connection" }
    };
    const harness = renderReview({ postJson: async () => ({ ...publicationPreflight(), isPublishable: false, diagnostics: [diagnostic, diagnostic] }) });
    click(buttonByText(harness.container, "Review & publish"));
    await waitFor(() => expect(harness.container.querySelector(".ad-publication-blockers")).not.toBeNull());
    const blockers = harness.container.querySelector<HTMLElement>(".ad-publication-blockers")!;
    expect(blockers.textContent).toContain(diagnostic.message);
    expect(blockers.textContent).toContain(diagnostic.remediation);
    expect(blockers.querySelectorAll(".ad-publication-diagnostics li")).toHaveLength(1);
    const summary = harness.container.querySelector(".ad-publication-summary")!;
    expect(blockers.compareDocumentPosition(summary) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
    const publish = buttonByText(harness.container, "Publish 1.0.0") as HTMLButtonElement;
    expect(publish.disabled).toBe(true);
    expect(publish.getAttribute("aria-describedby")).toBe(blockers.id);
    expect(harness.container.querySelector(".ad-publication-version")).toBeNull();
    expect(blockers.querySelector<HTMLDetailsElement>(".ad-publication-diagnostic-details")?.open).toBe(false);
    click(buttonByText(blockers, "Choose model connection"));
    await waitFor(() => expect(harness.onFocusDiagnostic).toHaveBeenCalledWith(diagnostic, expect.any(HTMLButtonElement)));
    expect(harness.container.querySelector("[role='dialog']")).toBeNull();
  });

  it("rechecks blocked material and restores publication after correction", async () => {
    let blocked = true;
    const harness = renderReview({ postJson: async () => ({ ...publicationPreflight(), isPublishable: !blocked }) });
    click(buttonByText(harness.container, "Review & publish"));
    await waitFor(() => expect(harness.container.querySelector(".ad-publication-blockers")).not.toBeNull());
    expect(harness.container.textContent).toContain("ask an administrator");
    blocked = false;
    click(buttonByText(harness.container, "Check again"));
    await waitFor(() => expect(harness.container.querySelector(".ad-publication-blockers")).toBeNull());
    await waitFor(() => expect((buttonByText(harness.container, "Publish 1.0.0") as HTMLButtonElement).disabled).toBe(false));
  });

  it("retains a legal provider code in technical details without inventing a correction target", async () => {
    const code = "acme:model.unavailable";
    const message = "The activity provider reported an issue.";
    const harness = renderReview({ postJson: async () => ({ ...publicationPreflight(), isPublishable: false, diagnostics: [{
      code, severity: "Error", message, remediation: "Ask an administrator to review the diagnostic details and activity provider logs.",
      subject: { kind: "ActivityDefinition", id: "acme" }, location: { providerKey: "acme" }, metadata: {}
    }] }) });
    click(buttonByText(harness.container, "Review & publish"));
    await waitFor(() => expect(harness.container.querySelector(".ad-publication-blockers")).not.toBeNull());
    const issue = harness.container.querySelector(".ad-publication-diagnostics li")!;
    expect(issue.querySelector("strong")?.textContent).toBe(message);
    expect(issue.querySelector("code")?.textContent).toBe(code);
    expect(issue.querySelector<HTMLDetailsElement>("details")?.open).toBe(false);
    expect(issue.querySelector("button")).toBeNull();
  });

  it.each(["Rejected", "Failed"])("keeps earlier readiness distinct after a %s receipt", async status => {
    const harness = renderReview({ postJson: async (url, body) => url.endsWith("/publication-preflight")
      ? publicationPreflight()
      : { ...publicationReceipt(body as Record<string, unknown>), status, outcome: null, diagnostics: [{
        code: "provider.model.unavailable", severity: "Info", message: "Choose an available model connection.",
        subject: { kind: "ActivityDefinition", id: "provider" }, location: { providerKey: "provider", jsonPointer: "/model" },
        remediation: "Review the connection in Designer.", metadata: { actionLabel: "Review model connection" }
      }] } });
    click(buttonByText(harness.container, "Review & publish"));
    await waitFor(() => expect(buttonByText(harness.container, "Publish 1.0.0")).toBeTruthy());
    click(buttonByText(harness.container, "Publish 1.0.0"));
    await waitFor(() => expect(harness.container.querySelector(".ad-publication-blockers")).not.toBeNull());
    const readiness = harness.container.querySelector(".ad-publication-readiness")!;
    expect(readiness.textContent).toContain("Checks from the earlier review");
    expect(readiness.textContent).not.toContain("Ready to publish");
    expect(harness.container.querySelector(".ad-publication-summary")?.textContent).toContain("Blocked");
    expect(harness.container.querySelector(".ad-publication-blockers")?.textContent).toContain("Publication did not complete");
    expect((buttonByText(harness.container, "Publish 1.0.0") as HTMLButtonElement).disabled).toBe(true);
  });

  it("opens with exact save and preflight, then resumes the retained review without another preflight", async () => {
    const preflight = vi.fn(async () => publicationPreflight());
    const flushExactSavedRevision = vi.fn(async () => ({ revision: 3, signature: "signature-3" }));
    const harness = renderReview({ postJson: async url => url.endsWith("/publication-preflight") ? preflight() : Promise.reject(new Error(`Unexpected POST ${url}`)), flushExactSavedRevision });
    const trigger = buttonByText(harness.container, "Review & publish");
    const entryName = trigger.getAttribute("aria-label") ?? trigger.textContent?.trim();
    trigger.focus();
    click(trigger);

    await waitFor(() => expect(buttonByText(harness.container, "Publish 1.0.0")).toBeTruthy());
    const continuationTrigger = buttonByText(harness.container, "Continue publication review");
    const continuationName = continuationTrigger.getAttribute("aria-label") ?? continuationTrigger.textContent?.trim();
    expect(flushExactSavedRevision).toHaveBeenCalledTimes(1);
    expect(preflight).toHaveBeenCalledTimes(1);
    expect(harness.container.textContent).toContain("Additive");
    expect(harness.container.textContent).toContain("Dependency Added");
    expect(harness.container.textContent).not.toContain("Other impact · additive");
    expect(harness.container.querySelector('[role="dialog"]')?.textContent).toContain("Ready to publish");

    window.dispatchEvent(new KeyboardEvent("keydown", { key: "Escape", bubbles: true }));
    await waitFor(() => expect(harness.container.querySelector('[role="dialog"]')).toBeNull());
    expect(document.activeElement).toBe(trigger);
    expect(document.body.style.overflow).toBe("");

    click(continuationTrigger);
    await waitFor(() => expect(harness.container.querySelector('[role="dialog"]')).not.toBeNull());
    expect(flushExactSavedRevision).toHaveBeenCalledTimes(1);
    expect(preflight).toHaveBeenCalledTimes(1);
    expect(entryName).toBe("Review & publish");
    expect(continuationName).toBe("Continue publication review");
    await harness.unmount();
  });

  it("keeps first-publication contents and successful technical checks behind disclosure", async () => {
    const harness = renderReview({ postJson: async () => publicationPreflight() });
    click(buttonByText(harness.container, "Review & publish"));
    await waitFor(() => expect(buttonByText(harness.container, "Publish 1.0.0")).toBeTruthy());
    const impact = harness.container.querySelector(".ad-publication-impact")!;
    expect(impact.closest("details")?.open).toBe(false);
    const readiness = harness.container.querySelector(".ad-publication-readiness")!;
    expect(readiness.closest("details")?.open).toBe(false);
    expect(harness.container.textContent).toContain("Publish the first version for use in workflows.");
    expect(buttonByText(harness.container, "Publish 1.0.0").closest("details")).toBeNull();
  });

  it("keeps changes to an existing version and failed readiness checks visible", async () => {
    const preflight = publicationPreflight();
    const harness = renderReview({ postJson: async () => ({ ...preflight, hasBaseline: true, isPublishable: false }) });
    click(buttonByText(harness.container, "Review & publish"));
    await waitFor(() => expect(harness.container.querySelector(".ad-publication-impact")).not.toBeNull());
    expect(harness.container.querySelector(".ad-publication-impact")?.closest("details")).toBeNull();
    expect(harness.container.querySelector(".ad-publication-readiness")?.closest("details")).toBeNull();
    expect((buttonByText(harness.container, "Publish 1.0.0") as HTMLButtonElement).disabled).toBe(true);
  });

  it("cannot close or leave focus while publication is pending", async () => {
    let resolvePublish!: (value: unknown) => void;
    let publishRequest: Record<string, unknown> | null = null;
    const publishPending = new Promise(resolve => { resolvePublish = resolve; });
    const publicationPending = vi.fn();
    const harness = renderReview({
      postJson: (url, body) => {
        if (url.endsWith("/publication-preflight")) return Promise.resolve(publicationPreflight());
        if (url.endsWith("/publication")) {
          publishRequest = body as Record<string, unknown>;
          return publishPending;
        }
        return Promise.reject(new Error(`Unexpected POST ${url}`));
      },
      onPublicationPending: publicationPending
    });
    click(buttonByText(harness.container, "Review & publish"));
    await waitFor(() => expect(buttonByText(harness.container, "Publish 1.0.0")).toBeTruthy());
    click(buttonByText(harness.container, "Publish 1.0.0"));
    await waitFor(() => expect(buttonByText(harness.container, "Publishing atomically…")).toBeTruthy());

    expect((buttonByText(harness.container, "Close publication review") as HTMLButtonElement).disabled).toBe(true);
    window.dispatchEvent(new KeyboardEvent("keydown", { key: "Escape", bubbles: true }));
    expect(harness.container.querySelector('[role="dialog"]')).not.toBeNull();
    harness.container.querySelector<HTMLButtonElement>(".ad-publication-toolbar-trigger")!.focus();
    window.dispatchEvent(new KeyboardEvent("keydown", { key: "Tab", bubbles: true, cancelable: true }));
    expect(harness.container.querySelector('[role="dialog"]')?.contains(document.activeElement)).toBe(true);
    expect(publicationPending).toHaveBeenCalledWith(true);
    expect(publicationPending).not.toHaveBeenCalledWith(false);

    await waitFor(() => expect(publishRequest).not.toBeNull());
    resolvePublish(publicationReceipt(publishRequest));
    await waitFor(() => expect(harness.container.textContent).toContain("Published immutable version 1.0.0"));
    expect(harness.container.textContent).toContain("First version published. Existing workflows are unaffected.");
    expect(harness.container.textContent).not.toContain("Publish the first version for use in workflows.");
    await harness.unmount();
  });

  it("keeps an unknown publication outcome in the open reconciliation-only dialog", async () => {
    const publicationPending = vi.fn();
    const harness = renderReview({
      postJson: async url => {
        if (url.endsWith("/publication-preflight")) return publicationPreflight();
        if (url.endsWith("/publication")) throw new TypeError("private transport detail");
        throw new Error(`Unexpected POST ${url}`);
      },
      getJson: async url => {
        if (url === "/capabilities") return capabilities();
        if (url === "/design/activities/definitions/definition-1") return definition();
        if (url.startsWith("/design/activities/publication-operations/")) throw new TypeError("private transport detail");
        throw new Error(`Unexpected GET ${url}`);
      },
      onPublicationPending: publicationPending
    });
    click(buttonByText(harness.container, "Review & publish"));
    await waitFor(() => expect(buttonByText(harness.container, "Publish 1.0.0")).toBeTruthy());
    click(buttonByText(harness.container, "Publish 1.0.0"));
    await waitFor(() => expect(harness.container.textContent).toContain("Reconcile authoritative status"));

    expect((buttonByText(harness.container, "Close publication review") as HTMLButtonElement).disabled).toBe(true);
    window.dispatchEvent(new KeyboardEvent("keydown", { key: "Escape", bubbles: true }));
    expect(harness.container.querySelector('[role="dialog"]')).not.toBeNull();
    expect(harness.container.textContent).not.toContain("private transport detail");
    expect(publicationPending.mock.calls.map(call => call[0])).toEqual([true]);
    await harness.unmount();
  });
});

function renderReview({
  postJson,
  getJson,
  flushExactSavedRevision = vi.fn(async () => ({ revision: 3, signature: "signature-3" })),
  onPublicationPending = vi.fn()
}: {
  postJson(url: string, body: unknown): Promise<unknown>;
  getJson?(url: string): Promise<unknown>;
  flushExactSavedRevision?: () => Promise<{ revision: number; signature: string } | null>;
  onPublicationPending?: (pending: boolean) => void;
}) {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  const context = endpoint(postJson, getJson);
  const onFocusDiagnostic = vi.fn(async (): Promise<StudioActivityDiagnosticFocusResult> => ({ kind: "unsupported", announcement: "Not focused." }));
  const container = document.createElement("div");
  document.body.appendChild(container);
  const root: Root = createRoot(container);
  flushSync(() => root.render(<QueryClientProvider client={queryClient}><ActivityDefinitionPublicationReview
    context={context}
    definitionId="definition-1"
    draftId="draft-1"
    currentRevision={3}
    currentSignature="signature-3"
    disabled={false}
    flushExactSavedRevision={flushExactSavedRevision}
    onPublicationPending={onPublicationPending}
    onPublished={vi.fn()}
    onFocusDiagnostic={onFocusDiagnostic}
    onOpenVersion={vi.fn()}
    mode="toolbar"
  /></QueryClientProvider>));
  let mounted = true;
  const unmount = () => {
    if (!mounted) return;
    mounted = false;
    flushSync(() => root.unmount());
    queryClient.clear();
    container.remove();
  };
  unmounts.push(unmount);
  return { container, unmount, onFocusDiagnostic };
}

function endpoint(postJson: (url: string, body: unknown) => Promise<unknown>, getJson?: (url: string) => Promise<unknown>): StudioEndpointContext {
  return {
    baseUrl: "test://activity-publication-review",
    http: {
      getJson: vi.fn((url: string) => {
        if (url === "/capabilities") return Promise.resolve(capabilities());
        if (url === "/design/activities/definitions/definition-1" && !getJson) return Promise.resolve(definition());
        return getJson?.(url) ?? Promise.reject(new Error(`Unexpected GET ${url}`));
      }),
      postJson: vi.fn(postJson),
      putJson: vi.fn(),
      requestJson: vi.fn(),
      deleteJson: vi.fn(),
      postForm: vi.fn()
    }
  } as unknown as StudioEndpointContext;
}

function capabilities() {
  return { capabilities: [
    {
      id: "elsa.api.activity-design",
      contractVersion: "1",
      links: [{ rel: "activity-definition", href: "design/activities/definitions/{definitionId}", templated: true }]
    },
    {
      id: "elsa.api.publishing",
      contractVersion: "1",
      links: [
        { rel: "activity-publication-preflight", href: "design/activities/drafts/{draftId}/publication-preflight", templated: true },
        { rel: "activity-publication", href: "design/activities/drafts/{draftId}/publication", templated: true },
        { rel: "activity-publication-receipt", href: "design/activities/publication-operations/{idempotencyKey}", templated: true }
      ]
    }
  ] };
}

function definition() {
  return {
    definition: {
      definitionId: "definition-1",
      activityTypeKey: "elsa.user.sample",
      category: "General",
      displayName: "Sample",
      contentAuthority: { kind: "Design", authorityKey: "design" },
      headVersionId: null,
      recommendedVersionId: null
    },
    lifecycle: { draftCount: 1, versionCount: 0, head: null, recommendation: null },
    actions: [],
    updatedAt: "2026-10-18T00:00:00Z"
  };
}

function publicationPreflight() {
  return {
    draftId: "draft-1",
    draftRevision: 3,
    definitionId: "definition-1",
    definitionHeadVersionId: null,
    hasBaseline: false,
    reviewToken: "review-1",
    isPublishable: true,
    minimumVersion: "1.0.0",
    validVersions: ["1.0.0"],
    diff: { compatibility: "Initial", requiredBump: "Major", behaviorChanged: true, summary: { breaking: 0, additive: 1, nonBehavioral: 0, warnings: 0 } },
    impactFirstChanges: [{ changeId: "add-dependency", impact: "additive", area: "dependency", kind: "DependencyAdded", message: "Added Write Line.", before: null, after: null, requiredBump: "minor" }],
    dependencies: [],
    provider: { kind: "Provider", key: "elsa.activity-graph", schemaVersion: "1", status: "Ready", supportedSchemaVersions: ["1"] },
    storage: [],
    runtime: [],
    diagnostics: []
  };
}

function publicationReceipt(request: Record<string, unknown> | null = null) {
  return {
    idempotencyKey: request?.idempotencyKey ?? "operation-1",
    status: "Applied",
    draftId: "draft-1",
    expectedDraftRevision: request?.expectedDraftRevision ?? 3,
    expectedDefinitionHeadVersionId: request?.expectedDefinitionHeadVersionId ?? null,
    reviewToken: request?.reviewToken ?? "review-1",
    requestedVersion: request?.version ?? "1.0.0",
    outcome: {
      definitionId: "definition-1",
      definitionVersionId: "version-1",
      draftId: "draft-1",
      version: "1.0.0",
      templateId: "template-1",
      templateHash: "sha256:template",
      sourceReferenceId: "source-1",
      publishedAt: "2026-10-18T00:00:00Z"
    },
    diagnostics: [],
    updatedAt: "2026-10-18T00:00:00Z"
  };
}

function buttonByText(container: ParentNode, text: string) {
  const button = [...container.querySelectorAll("button")].find(candidate => candidate.textContent?.includes(text) || candidate.getAttribute("aria-label") === text);
  if (!button) throw new Error(`Button '${text}' not found in: ${container.textContent}`);
  return button;
}

function click(element: Element) {
  flushSync(() => element.dispatchEvent(new MouseEvent("click", { bubbles: true })));
}

async function waitFor(assertion: () => void) {
  const deadline = Date.now() + 2_000;
  while (true) {
    try {
      assertion();
      return;
    } catch (error) {
      if (Date.now() >= deadline) throw error;
      await new Promise(resolve => setTimeout(resolve, 10));
    }
  }
}
