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
    window.history.replaceState({}, "", "/");
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

    expect(rendered.container.querySelector('[aria-label="Version pages"]')).toBeNull();
    expect(rendered.container.textContent).not.toContain("Loading evidence…");
    expect([...rendered.container.querySelectorAll<HTMLInputElement>('input[name="dependency-direction"]')]
      .find(input => input.parentElement?.textContent?.includes("Dependencies"))?.checked).toBe(true);
    await selectRoot(rendered.container);
    expect([...rendered.container.querySelectorAll<HTMLInputElement>('input[name="dependency-direction"]')]
      .find(input => input.parentElement?.textContent?.includes("Dependencies"))?.checked).toBe(true);
    await waitForText(rendered.container, "AuthoritativeDirect");
    expect(rendered.container.querySelector<HTMLDetailsElement>(".ad-scan-details")?.open).toBe(false);
    expect(rendered.container.textContent).toContain("occurrence-a");
    click(buttonByText(rendered.container, "Load next page"));
    await waitForText(rendered.container, "occurrence-b");
    expect(rendered.container.textContent).toContain("2 visible rows · 2 server pages");
    expect(dependencyRequests[0]).toContain("direction=outbound&transitive=false&include=versions%2Cdrafts");
    expect(dependencyRequests[1]).toContain("cursor=cursor-2");
    const sourceLink = rendered.container.querySelector<HTMLAnchorElement>("a")!;
    expect(sourceLink.getAttribute("href"))
      .toBe("/workflows/definitions?definition=workflow-1&draft=workflow-draft-1&occurrence=occurrence-a");
    const event = new MouseEvent("click", { bubbles: true, cancelable: true, button: 0 });
    flushSync(() => sourceLink.dispatchEvent(event));
    expect(event.defaultPrevented).toBe(true);
    expect(window.location.pathname + window.location.search)
      .toBe("/workflows/definitions?definition=workflow-1&draft=workflow-draft-1&occurrence=occurrence-a");
    window.history.replaceState({}, "", "/");

    click(buttonByText(rendered.container, "View path"));
    await waitForText(rendered.container, "Complete cycle path");
    expect(rendered.container.querySelectorAll(".ad-dependency-path")).toHaveLength(1);
    await rendered.unmount();
  });

  it("starts source-provided activities at the confirmed recommended version and shows inbound usage", async () => {
    const dependencyRequests: string[] = [];
    const getJson = vi.fn(async (url: string) => {
      if (url === "/capabilities") return capabilities();
      if (url.startsWith("/design/activities/definitions/definition-1/versions?")) return versionPage();
      if (url.includes("/dependencies?")) {
        dependencyRequests.push(url);
        return dependencyPage("source-occurrence", null, false, "Inbound");
      }
      throw new Error(`Unexpected GET ${url}`);
    });
    const rendered = renderExplorer(getJson, { initialVersionId: "version-1", initialDirection: "inbound" });

    await waitForText(rendered.container, "1.0.0 · Active");
    expect((rendered.container.querySelector("select") as HTMLSelectElement).value).toBe("version-1");
    expect(rendered.container.querySelector<HTMLOptionElement>('option[value="version-1"]')?.title).toBe("1.0.0+build-sha");
    await waitForText(rendered.container, "source-occurrence");
    expect(rendered.container.textContent).toContain("Used by");
    expect(dependencyRequests[0]).toContain("direction=inbound&transitive=false&include=versions%2Cdrafts");
    expect(dependencyRequests[0]).toContain("/versions/version-1/dependencies?");
    await rendered.unmount();

    const unconfirmedRequests: string[] = [];
    const unconfirmed = renderExplorer(vi.fn(async (url: string) => {
      if (url === "/capabilities") return capabilities();
      if (url.startsWith("/design/activities/definitions/definition-1/versions?")) return versionPage();
      if (url.includes("/dependencies?")) unconfirmedRequests.push(url);
      throw new Error(`Unexpected GET ${url}`);
    }), { initialVersionId: "unconfirmed-version", initialDirection: "inbound" });
    await waitForText(unconfirmed.container, "1.0.0 · Active");
    await waitForText(unconfirmed.container, "Choose a version to see matching evidence.");
    expect((unconfirmed.container.querySelector("select") as HTMLSelectElement).value).toBe("");
    expect(unconfirmedRequests).toHaveLength(0);
    await unconfirmed.unmount();
  });

  it("keeps traversal and evidence filters in accessible advanced options", async () => {
    const dependencyRequests: string[] = [];
    const getJson = vi.fn(async (url: string) => {
      if (url === "/capabilities") return capabilities();
      if (url.startsWith("/design/activities/definitions/definition-1/versions?")) return versionPage();
      if (url.includes("/dependencies?")) {
        dependencyRequests.push(url);
        return dependencyPage("filtered-occurrence", null, false, "Outbound", true, ["Versions"]);
      }
      throw new Error(`Unexpected GET ${url}`);
    });
    const rendered = renderExplorer(getJson);
    const advanced = rendered.container.querySelector("details")!;
    expect(advanced.open).toBe(false);
    flushSync(() => advanced.querySelector("summary")?.click());
    expect(advanced.open).toBe(true);
    expect(advanced.querySelector("fieldset legend")?.textContent).toBe("Search depth");

    const indirect = [...advanced.querySelectorAll<HTMLInputElement>('input[name="dependency-depth"]')]
      .find(input => input.parentElement?.textContent?.includes("Include indirect"))!;
    const drafts = [...advanced.querySelectorAll<HTMLInputElement>('input[type="checkbox"]')]
      .find(input => input.parentElement?.textContent?.includes("Drafts"))!;
    flushSync(() => indirect.click());
    flushSync(() => drafts.click());
    expect(indirect.checked).toBe(true);
    expect(drafts.checked).toBe(false);
    await selectRoot(rendered.container);
    await waitForText(rendered.container, "filtered-occurrence");
    expect(dependencyRequests[0]).toContain("transitive=true&include=versions");
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
    await waitForText(rendered.container, "scan is out of date");
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
    await waitForText(rendered.container, "snapshot is no longer available");
    expect(rendered.container.textContent).toContain("confirmed-before-expiry");
    await rendered.unmount();

    const forbidden = renderExplorer(vi.fn(async (url: string) => {
      if (url === "/capabilities") return capabilities();
      if (url.startsWith("/design/activities/definitions/definition-1/versions?")) return versionPage();
      throw new StudioHttpError(403, "hidden-owner-identity", null);
    }));
    await selectRoot(forbidden.container);
    await waitForText(forbidden.container, "No identities or counts are shown");
    expect(forbidden.container.textContent).not.toContain("hidden-owner-identity");
    await forbidden.unmount();
  });
});

function renderExplorer(
  getJson: (url: string) => Promise<unknown>,
  options: { initialVersionId?: string; initialDirection?: "inbound" | "outbound" } = {}
) {
  const container = document.createElement("div");
  document.body.appendChild(container);
  const root = createRoot(container);
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  const context = {
    baseUrl: `test://dependency-explorer-${Math.random()}`,
    http: { getJson }
  } as unknown as StudioEndpointContext;
  flushSync(() => root.render(<QueryClientProvider client={queryClient}><ActivityDefinitionDependencyExplorer context={context} definitionId="definition-1" {...options} /></QueryClientProvider>));
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
  await waitForText(container, "1.0.0 · Active");
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
        version: "1.0.0+build-sha",
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
  cycle = false,
  direction: "Inbound" | "Outbound" = "Outbound",
  transitive = false,
  include: Array<"Drafts" | "Versions"> = ["Drafts", "Versions"]
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
    query: { direction, transitive, include },
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
