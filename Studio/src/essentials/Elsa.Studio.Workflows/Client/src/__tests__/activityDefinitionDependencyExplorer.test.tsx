import { flushSync } from "react-dom";
import { createRoot } from "react-dom/client";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { StudioHttpError, type StudioEndpointContext } from "@elsa-workflows/studio-sdk";
import { afterEach, describe, expect, it, vi } from "vitest";
import { ActivityDefinitionDependencyExplorer } from "../ActivityDefinitionDependencyExplorer";
import type { ActivityDefinitionUsageEvidence } from "../activityDefinitionTypes";

describe("Activity Definition dependency explorer", () => {
  afterEach(() => {
    document.body.replaceChildren();
  });

  it("loads snapshot-bound pages, preserves repeated occurrences, and renders only the selected complete cycle", async () => {
    const dependencyRequests: string[] = [];
    const getJson = vi.fn(async (url: string) => {
      if (url === "/capabilities") return capabilities();
      if (url.startsWith("/design/activities/definitions/definition-1/versions?")) return versionPage();
      if (url.includes("/dependencies?")) {
        dependencyRequests.push(url);
        return url.includes("cursor=cursor-2")
          ? dependencyPage("occurrence-b", null)
          : dependencyPage("occurrence-a", "cursor-2", true);
      }
      throw new Error(`Unexpected GET ${url}`);
    });
    const rendered = renderExplorer(getJson);

    await selectRoot(rendered.container);
    await waitForText(rendered.container, "AuthoritativeDirect");
    expect(rendered.container.textContent).toContain("occurrence-a");
    click(buttonByText(rendered.container, "Load next page"));
    await waitForText(rendered.container, "occurrence-b");
    expect(rendered.container.textContent).toContain("2 visible rows · 2 server pages");
    expect(dependencyRequests[0]).toContain("direction=outbound&transitive=false&include=versions%2Cdrafts");
    expect(dependencyRequests[1]).toContain("cursor=cursor-2");
    expect(rendered.container.querySelector<HTMLAnchorElement>("a")?.getAttribute("href"))
      .toBe("/workflows/definitions?definition=workflow-1&draft=workflow-draft-1&occurrence=occurrence-a");

    click(buttonByText(rendered.container, "Review exact path"));
    await waitForText(rendered.container, "Complete cycle path");
    expect(rendered.container.querySelectorAll(".ad-dependency-path")).toHaveLength(1);
    await rendered.unmount();
  });

  it("never combines a page whose root or snapshot binding changed", async () => {
    let dependencyReads = 0;
    const getJson = vi.fn(async (url: string) => {
      if (url === "/capabilities") return capabilities();
      if (url.startsWith("/design/activities/definitions/definition-1/versions?")) return versionPage();
      if (url.includes("/dependencies?")) {
        dependencyReads += 1;
        return dependencyReads === 1
          ? dependencyPage("confirmed-occurrence", "cursor-2")
          : { ...dependencyPage("foreign-occurrence", null), root: { ...reference("version-1"), versionId: "hidden-version" } };
      }
      throw new Error(`Unexpected GET ${url}`);
    });
    const rendered = renderExplorer(getJson);

    await selectRoot(rendered.container);
    await waitForText(rendered.container, "confirmed-occurrence");
    click(buttonByText(rendered.container, "Load next page"));
    await waitForText(rendered.container, "stale completed snapshot");
    expect(rendered.container.textContent).toContain("confirmed-occurrence");
    expect(rendered.container.textContent).not.toContain("foreign-occurrence");
    await rendered.unmount();
  });

  it("preserves confirmed rows on cursor expiry but clears all identity and counts for authorization failures", async () => {
    let dependencyReads = 0;
    const getJson = vi.fn(async (url: string) => {
      if (url === "/capabilities") return capabilities();
      if (url.startsWith("/design/activities/definitions/definition-1/versions?")) return versionPage();
      if (url.includes("/dependencies?")) {
        dependencyReads += 1;
        if (dependencyReads === 2) throw new StudioHttpError(410, "hidden expired watermark", null);
        return dependencyPage("confirmed-before-expiry", "cursor-2");
      }
      throw new Error(`Unexpected GET ${url}`);
    });
    const rendered = renderExplorer(getJson);

    await selectRoot(rendered.container);
    await waitForText(rendered.container, "confirmed-before-expiry");
    click(buttonByText(rendered.container, "Load next page"));
    await waitForText(rendered.container, "no longer retains this projection watermark");
    expect(rendered.container.textContent).toContain("confirmed-before-expiry");
    await rendered.unmount();

    const forbidden = renderExplorer(vi.fn(async (url: string) => {
      if (url === "/capabilities") return capabilities();
      if (url.startsWith("/design/activities/definitions/definition-1/versions?")) return versionPage();
      throw new StudioHttpError(403, "hidden-owner-identity", null);
    }));
    await selectRoot(forbidden.container);
    await waitForText(forbidden.container, "No hidden identity or count");
    expect(forbidden.container.textContent).not.toContain("hidden-owner-identity");
    await forbidden.unmount();
  });
});

function renderExplorer(getJson: (url: string) => Promise<unknown>) {
  const container = document.createElement("div");
  document.body.appendChild(container);
  const root = createRoot(container);
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  const context = {
    baseUrl: `test://dependency-explorer-${Math.random()}`,
    http: { getJson }
  } as unknown as StudioEndpointContext;
  flushSync(() => root.render(<QueryClientProvider client={queryClient}><ActivityDefinitionDependencyExplorer context={context} definitionId="definition-1" /></QueryClientProvider>));
  return {
    container,
    unmount: async () => {
      flushSync(() => {
      root.unmount();
      queryClient.clear();
      });
    }
  };
}

async function selectRoot(container: HTMLElement) {
  await waitForText(container, "1.0.0 · Active · version-1");
  const select = container.querySelector('select') as HTMLSelectElement;
  flushSync(() => {
    Object.getOwnPropertyDescriptor(HTMLSelectElement.prototype, "value")?.set?.call(select, "version-1");
    select.dispatchEvent(new Event("change", { bubbles: true }));
  });
}

function capabilities() {
  return {
    capabilities: [{
      id: "elsa.api.activity-design",
      contractVersion: "1",
      links: [
        { rel: "activity-definition-versions", href: "design/activities/definitions/{definitionId}/versions", templated: true },
        { rel: "activity-definition-version", href: "design/activities/versions/{versionId}", templated: true }
      ]
    }]
  };
}

function versionPage() {
  return {
    items: [{
      version: {
        versionId: "version-1",
        definitionId: "definition-1",
        version: "1.0.0",
        lifecycle: "Active",
        publishedAt: "2026-07-18T12:00:00Z"
      },
      providerKey: "elsa.activity-graph",
      providerSchemaVersion: "1",
      isRecommended: true,
      actions: []
    }],
    count: 1,
    totalCount: 1,
    hasMore: false,
    continuation: null,
    snapshot: { snapshotId: "snapshot-1", asOf: "2026-07-18T12:00:00Z" }
  };
}

function dependencyPage(
  occurrenceId: string,
  nextCursor: string | null,
  cycle = false
): ActivityDefinitionUsageEvidence {
  const root = reference("version-1");
  const owner = {
    kind: "WorkflowDraft",
    definitionId: "workflow-1",
    versionId: null,
    draftId: "workflow-draft-1",
    revision: 7
  };
  const path = cycle ? [root, owner, root] : [owner, root];
  return {
    root,
    query: { direction: "Outbound", transitive: false, include: ["Drafts", "Versions"] },
    consistency: {
      kind: "AuthoritativeDirect",
      isAuthoritative: true,
      asOfSequence: null,
      asOf: null,
      rebuildId: null
    },
    items: [{
      relationshipId: `relationship-${occurrenceId}`,
      owner,
      dependency: root,
      occurrence: { occurrenceId, nodeOrigin: [{ kind: "AuthoredNode", id: occurrenceId }] },
      isDirect: true,
      depth: 1,
      path
    }],
    nextCursor
  };
}

function reference(versionId: string) {
  return {
    kind: "ActivityVersion",
    definitionId: "definition-1",
    versionId,
    version: "1.0.0",
    draftId: null,
    revision: null,
    templateHash: "sha256:version-1",
    tenantId: "tenant-1",
    lifecycle: "Active"
  };
}

function buttonByText(container: HTMLElement, text: string) {
  const button = [...container.querySelectorAll("button")].find(candidate => candidate.textContent?.includes(text));
  if (!button) throw new Error(`Button '${text}' not found.`);
  return button;
}

function click(element: Element) {
  flushSync(() => element.dispatchEvent(new MouseEvent("click", { bubbles: true })));
}

async function waitForText(container: HTMLElement, text: string) {
  const deadline = Date.now() + 5_000;
  while (!container.textContent?.includes(text)) {
    if (Date.now() > deadline) throw new Error(`Timed out waiting for '${text}'. Current text: ${container.textContent}`);
    await new Promise(resolve => setTimeout(resolve, 10));
  }
}
