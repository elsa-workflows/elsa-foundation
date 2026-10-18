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
  it("opens with exact save and preflight, then resumes the retained review without another preflight", async () => {
    const preflight = vi.fn(async () => publicationPreflight());
    const flushExactSavedRevision = vi.fn(async () => ({ revision: 3, signature: "signature-3" }));
    const harness = renderReview({ postJson: async url => url.endsWith("/publication-preflight") ? preflight() : Promise.reject(new Error(`Unexpected POST ${url}`)), flushExactSavedRevision });
    const trigger = buttonByText(harness.container, "Review & publish");
    trigger.focus();
    click(trigger);

    await waitFor(() => expect(buttonByText(harness.container, "Publish 1.0.0")).toBeTruthy());
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

    click(trigger);
    await waitFor(() => expect(harness.container.querySelector('[role="dialog"]')).not.toBeNull());
    expect(flushExactSavedRevision).toHaveBeenCalledTimes(1);
    expect(preflight).toHaveBeenCalledTimes(1);
    await harness.unmount();
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
  return { container, unmount };
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
      links: [{ rel: "activity-definition", href: "/design/activities/definitions/{definitionId}", templated: true }]
    },
    {
      id: "elsa.api.publishing",
      contractVersion: "1",
      links: [
        { rel: "activity-publication-preflight", href: "/design/activities/drafts/{draftId}/publication-preflight", templated: true },
        { rel: "activity-publication", href: "/design/activities/drafts/{draftId}/publication", templated: true },
        { rel: "activity-publication-receipt", href: "/design/activities/publication-operations/{idempotencyKey}", templated: true }
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
