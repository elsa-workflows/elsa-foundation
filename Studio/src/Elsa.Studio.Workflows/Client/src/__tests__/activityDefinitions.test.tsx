import React from "react";
import { flushSync } from "react-dom";
import { createRoot } from "react-dom/client";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { afterEach, describe, expect, it, vi } from "vitest";
import { StudioHttpError, type StudioActivityDefinitionImplementationEditorContribution, type StudioEndpointContext } from "@elsa-workflows/studio-sdk";
import { ActivityDefinitionsPage } from "../ActivityDefinitionsPage";
import { activityDesignKeys } from "../api/activityDesign";
import { clearApiCapabilityCache } from "../api/capabilities";
import { activityDefinitionsObservationEvent, observeActivityDefinitions } from "../activityDefinitionObservability";
import type { ActivityDefinitionDraftManagementView, ActivityDefinitionManagementView, ActivityDefinitionVersionManagementView, ActivityManagementPage } from "../activityDefinitionTypes";

afterEach(() => {
  clearApiCapabilityCache();
  window.history.replaceState({}, "", "/");
});

describe("Activity Definitions experience", () => {
  it("opens dedicated Elsa 3 import and broad upgrade routes through host navigation", async () => {
    const navigateToStudioPath = vi.fn();
    const getJson = vi.fn(async (url: string) => {
      if (url === "/capabilities") return capabilities();
      return page([]);
    });
    const rendered = renderPage(
      getJson,
      undefined,
      "/workflows/activity-definitions",
      undefined,
      [],
      undefined,
      navigateToStudioPath
    );
    await waitForText(rendered.container, "No Activity Definitions yet");

    click(buttonByText(rendered.container, "Import from Elsa 3"));

    expect(navigateToStudioPath).toHaveBeenCalledWith("/workflows/activity-definitions/import-elsa3");
    click(buttonByText(rendered.container, "Plan broad upgrade"));
    expect(navigateToStudioPath).toHaveBeenCalledWith("/workflows/activity-definitions/upgrades");
    await rendered.unmount();
  });

  it("distinguishes initial loading, an empty collection, and no filter matches", async () => {
    let resolveCollection!: (value: unknown) => void;
    const firstCollection = new Promise(resolve => { resolveCollection = resolve; });
    const getJson = vi.fn(async (url: string) => {
      if (url === "/capabilities") return capabilities();
      if (url.includes("search=missing")) return page([]);
      return firstCollection;
    });
    const rendered = renderPage(getJson);

    await waitForText(rendered.container, "Loading Activity Definitions");
    resolveCollection(page([]));
    await waitForText(rendered.container, "No Activity Definitions yet");

    change(rendered.container.querySelector<HTMLInputElement>("input[placeholder='Name, type key, or category']")!, "missing");
    await waitForText(rendered.container, "No Activity Definitions match");
    await rendered.unmount();
  });

  it("shows a safe failed-without-data state", async () => {
    const getJson = vi.fn(async (url: string) => {
      if (url === "/capabilities") return capabilities();
      throw new Error("secret upstream failure for definition-hidden");
    });
    const rendered = renderPage(getJson);

    await waitForText(rendered.container, "Couldn't load Activity Definitions");
    expect(rendered.container.textContent).not.toContain("secret upstream failure");
    expect(rendered.container.textContent).not.toContain("definition-hidden");
    await rendered.unmount();
  });

  it("humanizes source-owned CLR titles and renders a placeholder for MinValue updated dates", async () => {
    const clr = "Elsa.Activities.Http.Activities.SendHttpRequest";
    const sourceOwned: ActivityDefinitionManagementView = {
      ...definition({ definitionId: "definition-http", activityTypeKey: clr, displayName: clr }),
      updatedAt: "0001-01-01T00:00:00"
    };
    const getJson = vi.fn(async (url: string) => {
      if (url === "/capabilities") return capabilities();
      if (url.startsWith("/design/activities/definitions?")) return page([sourceOwned]);
      throw new Error(`Unexpected GET ${url}`);
    });
    const rendered = renderPage(getJson);

    await waitForText(rendered.container, "Send Http Request");
    const definitionCell = rendered.container.querySelector<HTMLElement>("[data-label='Definition']")!;
    // Title humanized, full CLR type kept once as the subtitle (never duplicated).
    expect(definitionCell.querySelector("strong")?.textContent).toBe("Send Http Request");
    expect(definitionCell.querySelector("small")?.textContent).toBe(clr);
    expect([...definitionCell.querySelectorAll("strong, small")].filter(element => element.textContent === clr)).toHaveLength(1);
    // MinValue updated timestamp renders as an em-dash rather than "01/01/1".
    const updatedCell = rendered.container.querySelector<HTMLElement>("[data-label='Updated']")!;
    expect(updatedCell.textContent).toBe("—");
    await rendered.unmount();
  });

  it("opens the stable definition workbench and keeps an exact draft identity in the URL", async () => {
    const navigateToStudioPath = vi.fn();
    const getJson = vi.fn(async (url: string) => {
      if (url === "/capabilities") return capabilities();
      if (url.startsWith("/design/activities/definitions?")) return page([definition()]);
      if (url === "/design/activities/definitions/definition-1") return definition();
      if (url.startsWith("/design/activities/definitions/definition-1/drafts?")) return page([draft()]);
      if (url.startsWith("/design/activities/definitions/definition-1/versions?")) return page([version()]);
      if (url === "/design/activities/drafts/draft-1") return draftDetail();
      if (url === "/design/activities/versions/version-1") return versionDetail();
      throw new Error(`Unexpected GET ${url}`);
    });
    const rendered = renderPage(
      getJson,
      undefined,
      "/workflows/activity-definitions",
      undefined,
      [],
      undefined,
      navigateToStudioPath
    );

    await waitForText(rendered.container, "Invoice evaluator");
    const row = rendered.container.querySelector<HTMLElement>("[role='row'][tabindex='0']")!;
    row.dispatchEvent(new KeyboardEvent("keydown", { key: "Enter", bubbles: true }));

    await waitForText(rendered.container, "Stable Activity Definition");
    expect(rendered.container.textContent).toContain("Contoso.InvoiceEvaluator");
    expect(rendered.container.textContent).toContain("visual-graph");
    expect(getJson.mock.calls.some(([url]) => String(url).includes("/drafts?"))).toBe(false);
    expect(getJson.mock.calls.some(([url]) => String(url).includes("/versions?"))).toBe(false);
    click(buttonByText(rendered.container, "Drafts"));
    await waitForText(rendered.container, "Draft draft-1");
    click(buttonByText(rendered.container, "Draft draft-1"));

    expect(window.location.pathname).toBe("/workflows/activity-definitions");
    expect(new URLSearchParams(window.location.search).get("definition")).toBe("definition-1");
    expect(new URLSearchParams(window.location.search).get("draft")).toBe("draft-1");
    click(buttonByText(rendered.container, "Plan broad upgrade from this exact draft"));
    expect(navigateToStudioPath).toHaveBeenCalledWith(
      "/workflows/activity-definitions/upgrades?rootKind=ActivityDraft&rootId=draft-1"
    );
    await rendered.unmount();
  });

  it("resolves an exact draft bookmark even when the selected draft is outside the loaded page", async () => {
    const getJson = vi.fn(async (url: string) => {
      if (url === "/capabilities") return capabilities();
      if (url === "/design/activities/definitions/definition-1") return definition();
      if (url.startsWith("/design/activities/definitions/definition-1/drafts?")) return page([]);
      if (url === "/design/activities/drafts/draft-2") return draftDetail({ draftId: "draft-2", presentationLabel: "Parallel review" });
      throw new Error(`Unexpected GET ${url}`);
    });
    const rendered = renderPage(getJson, undefined, "/workflows/activity-definitions?definition=definition-1&section=drafts&draft=draft-2");

    await waitForText(rendered.container, "Parallel review");
    expect(rendered.container.textContent).toContain("draft-2");
    expect(rendered.container.textContent).toContain("revision 3");
    await rendered.unmount();
  });

  it("lists parallel duplicate labels with stable fallbacks and creates an unlabeled blank draft", async () => {
    const postJson = vi.fn(async (_url: string, _body: unknown) => draftDetail({ draftId: "draft-blank" }));
    const getJson = vi.fn(async (url: string) => {
      if (url === "/capabilities") return capabilities(true);
      if (url === "/design/activities/definitions/definition-1") return definition({}, [{ action: "create-draft", allowed: true }]);
      if (url.startsWith("/design/activities/definitions/definition-1/drafts?")) return page([
        draft({ draftId: "draft-review-a", presentationLabel: "Review" }),
        draft({ draftId: "draft-review-b", presentationLabel: "Review" }),
        draft({ draftId: "draft-unlabeled", presentationLabel: null })
      ]);
      if (url === "/design/activities/authoring-capabilities") return authoringCapabilities();
      if (url === "/design/activities/drafts/draft-blank") return draftDetail({ draftId: "draft-blank" });
      throw new Error(`Unexpected GET ${url}`);
    });
    const rendered = renderPage(getJson, undefined, "/workflows/activity-definitions?definition=definition-1&section=drafts", postJson, [graphContribution()]);

    await waitForText(rendered.container, "Draft draft-unlabeled");
    expect([...rendered.container.querySelectorAll("strong")].filter(element => element.textContent === "Review")).toHaveLength(2);
    click(buttonByText(rendered.container, "Create parallel draft"));
    await waitForText(rendered.container, "Blank");
    await waitFor(() => expect(rendered.container.querySelector<HTMLSelectElement>(".ad-management-dialog select")?.value).toBe("visual-graph|1"));
    click(buttonByText(rendered.container, "Create blank draft"));

    await waitFor(() => expect(postJson).toHaveBeenCalledTimes(1));
    expect(postJson).toHaveBeenCalledWith("/design/activities/definitions/definition-1/drafts", {
      sourceVersionId: null,
      presentationLabel: null,
      provider: { providerKey: "visual-graph", schemaVersion: "1", payload: { root: null } },
      contract: {
        contractSchemaVersion: "1",
        inputs: [],
        outputs: [],
        outcomes: [{ referenceKey: "done", name: "Done", isEmitted: true, description: null }]
      },
      layout: []
    });
    await waitFor(() => expect(new URLSearchParams(window.location.search).get("draft")).toBe("draft-blank"));
    await rendered.unmount();
  });

  it("clones one authorized exact immutable version without rewriting the source", async () => {
    const navigateToStudioPath = vi.fn();
    const postJson = vi.fn(async (_url: string, _body: unknown) => draftDetail({ draftId: "draft-clone", presentationLabel: "Review" }));
    const getJson = vi.fn(async (url: string) => {
      if (url === "/capabilities") return capabilities(true);
      if (url === "/design/activities/definitions/definition-1") return definition({}, [{ action: "create-draft", allowed: true }]);
      if (url.startsWith("/design/activities/definitions/definition-1/versions?")) return page([
        version({ actions: [{ action: "clone-draft", allowed: true }] })
      ]);
      if (url === "/design/activities/versions/version-1") return versionDetail();
      if (url === "/design/activities/authoring-capabilities") return authoringCapabilities();
      if (url === "/design/activities/drafts/draft-clone") return draftDetail({ draftId: "draft-clone", presentationLabel: "Review" });
      throw new Error(`Unexpected GET ${url}`);
    });
    const rendered = renderPage(
      getJson,
      undefined,
      "/workflows/activity-definitions?definition=definition-1&section=versions&version=version-1",
      postJson,
      [graphContribution()],
      undefined,
      navigateToStudioPath
    );

    await waitForText(rendered.container, "Create draft from this exact version");
    click(buttonByText(rendered.container, "Plan broad upgrade from this exact version"));
    expect(navigateToStudioPath).toHaveBeenCalledWith(
      "/workflows/activity-definitions/upgrades?rootKind=ActivityVersion&rootId=version-1"
    );
    click(buttonByText(rendered.container, "Create draft from this exact version"));
    await waitForText(rendered.container, "Clone exact version");
    change(controlByLabel<HTMLInputElement>(rendered.container, "Draft label"), "Review");
    click(buttonByText(rendered.container, "Clone exact version"));

    await waitFor(() => expect(postJson).toHaveBeenCalledTimes(1));
    expect(postJson).toHaveBeenCalledWith("/design/activities/definitions/definition-1/drafts", {
      sourceVersionId: "version-1",
      presentationLabel: "Review"
    });
    expect(postJson.mock.calls.every(([url]) => url === "/design/activities/definitions/definition-1/drafts")).toBe(true);
    await waitFor(() => expect(new URLSearchParams(window.location.search).get("draft")).toBe("draft-clone"));
    await rendered.unmount();
  });

  it("keeps source-owned history exact and read-only when fork authority is denied", async () => {
    const source = definition({
      contentAuthority: { kind: "ProviderSource", authorityKey: "contoso.catalog", sourceId: "invoice-source" }
    }, [{ action: "fork-definition", allowed: false, unavailableCode: "activity.action.forbidden" }]);
    const getJson = vi.fn(async (url: string) => {
      if (url === "/capabilities") return capabilities();
      if (url === "/design/activities/definitions/definition-1") return source;
      if (url.startsWith("/design/activities/definitions/definition-1/versions?")) return page([
        version({ actions: [{ action: "fork-definition", allowed: false, unavailableCode: "activity.action.forbidden" }] })
      ]);
      if (url === "/design/activities/versions/version-1") return versionDetail({
        definition: source.definition,
        provider: { providerKey: "contoso.source", schemaVersion: "7", manifestFingerprint: "source-fingerprint" }
      });
      throw new Error(`Unexpected GET ${url}`);
    });
    const rendered = renderPage(getJson, undefined, "/workflows/activity-definitions?definition=definition-1&section=versions&version=version-1");

    await waitForText(rendered.container, "Source-owned and read-only");
    await waitForText(rendered.container, "contoso.source");
    expect(rendered.container.textContent).toContain("contoso.catalog");
    expect(rendered.container.textContent).toContain("invoice-source");
    expect(rendered.container.textContent).toContain("contoso.source");
    expect(rendered.container.textContent).toContain("schema 7");
    expect(rendered.container.textContent).toContain("Immutable public contract");
    expect([...rendered.container.querySelectorAll("button")].some(button => button.textContent?.includes("Fork"))).toBe(false);
    await rendered.unmount();
  }, 15_000);

  it("does not offer an explicit fork version override when no recommended active version exists", async () => {
    const source = {
      ...definition({
        contentAuthority: { kind: "ProviderSource", authorityKey: "contoso.catalog", sourceId: "invoice-source" }
      }, [{ action: "fork-definition", allowed: true }]),
      lifecycle: {
        ...definition().lifecycle,
        recommendation: { ...definition().lifecycle.recommendation!, lifecycle: "Revoked" }
      }
    };
    const getJson = vi.fn(async (url: string) => {
      if (url === "/capabilities") return capabilities(true, true);
      if (url === "/design/activities/definitions/definition-1") return source;
      throw new Error(`Unexpected GET ${url}`);
    });
    const rendered = renderPage(getJson, undefined, "/workflows/activity-definitions?definition=definition-1");

    await waitForText(rendered.container, "No recommended active version");
    expect(rendered.container.textContent).toContain("does not offer an explicit version override");
    expect([...rendered.container.querySelectorAll("button")].some(button => button.textContent?.includes("Fork"))).toBe(false);
    await rendered.unmount();
  });

  it("fails closed without probing a direct fork path when the preview relation is unavailable", async () => {
    const source = definition({
      contentAuthority: { kind: "ProviderSource", authorityKey: "contoso.catalog", sourceId: "invoice-source" }
    }, [{ action: "fork-definition", allowed: true }]);
    const postJson = vi.fn();
    const getJson = vi.fn(async (url: string) => {
      if (url === "/capabilities") return capabilities(true);
      if (url === "/design/activities/definitions/definition-1") return source;
      if (url === "/design/activities/authoring-capabilities") return authoringCapabilities();
      throw new Error(`Unexpected GET ${url}`);
    });
    const rendered = renderPage(getJson, undefined, "/workflows/activity-definitions?definition=definition-1", postJson, [graphContribution()]);

    await waitForText(rendered.container, "Fork recommended version");
    click(buttonByText(rendered.container, "Fork recommended version"));
    await waitForText(rendered.container, "Studio does not offer an explicit version override");
    click(await enabledButton(rendered.container, "Create fork preview"));
    await waitForText(rendered.container, "fork preview could not be established");
    expect(postJson).not.toHaveBeenCalled();
    await rendered.unmount();
  });

  it("reviews and atomically applies the recommended active version without offering an explicit version picker", async () => {
    const source = definition({
      contentAuthority: { kind: "ProviderSource", authorityKey: "contoso.catalog", sourceId: "invoice-source" }
    }, [{ action: "fork-definition", allowed: true }]);
    const postJson = vi.fn(async (url: string, _body: unknown) => {
      if (url === "/design/activities/definitions/definition-1/fork-previews") return forkPreview();
      if (url === "/design/activities/fork-candidates/candidate-signed/apply") return {
        ...forkReceipt(),
        idempotencyKey: (_body as { idempotencyKey: string }).idempotencyKey
      };
      throw new Error(`Unexpected POST ${url}`);
    });
    const getJson = vi.fn(async (url: string) => {
      if (url === "/capabilities") return capabilities(true, true);
      if (url === "/design/activities/definitions/definition-1") return source;
      if (url === "/design/activities/authoring-capabilities") return authoringCapabilities();
      if (url === "/design/activities/drafts/draft-fork") return draftDetail({ draftId: "draft-fork" });
      throw new Error(`Unexpected GET ${url}`);
    });
    const rendered = renderPage(getJson, undefined, "/workflows/activity-definitions?definition=definition-1", postJson, [graphContribution()]);

    await waitForText(rendered.container, "Fork recommended version");
    click(buttonByText(rendered.container, "Fork recommended version"));
    await waitForText(rendered.container, "Recommended version");
    expect(rendered.container.querySelector("select")?.textContent).not.toContain("Version");
    click(await enabledButton(rendered.container, "Create fork preview"));

    await waitForText(rendered.container, "elsa.user.invoice-evaluator.forked");
    expect(rendered.container.textContent).toContain("definition-fork");
    expect(rendered.container.textContent).toContain("draft-fork");
    click(buttonByText(rendered.container, "Apply reviewed fork"));

    await waitFor(() => expect(new URLSearchParams(window.location.search).get("definition")).toBe("definition-fork"));
    expect(new URLSearchParams(window.location.search).get("draft")).toBe("draft-fork");
    expect(postJson.mock.calls[0][0]).toBe("/design/activities/definitions/definition-1/fork-previews");
    expect(postJson.mock.calls[0][1]).toMatchObject({
      sourceVersionId: "version-1",
      category: "Finance",
      displayName: "Invoice evaluator",
      targetProviderKey: "visual-graph",
      targetProviderSchemaVersion: "1"
    });
    expect(postJson.mock.calls[1][1]).toMatchObject({
      requestFingerprint: `sha256:${"a".repeat(64)}`
    });
    await rendered.unmount();
  });

  it("reconciles a lost successful Apply response through the advertised fork status relation", async () => {
    const source = definition({
      contentAuthority: { kind: "ProviderSource", authorityKey: "contoso.catalog", sourceId: "invoice-source" }
    }, [{ action: "fork-definition", allowed: true }]);
    const postJson = vi.fn(async (url: string, _body: unknown) => {
      if (url.endsWith("/fork-previews")) return forkPreview();
      if (url.includes("/fork-candidates/")) throw new StudioHttpError(
        500,
        "Outcome unknown",
        null,
        { errorCode: "activity.fork.outcome-unknown" }
      );
      throw new Error(`Unexpected POST ${url}`);
    });
    const getJson = vi.fn(async (url: string) => {
      if (url === "/capabilities") return capabilities(true, true);
      if (url === "/design/activities/definitions/definition-1") return source;
      if (url === "/design/activities/authoring-capabilities") return authoringCapabilities();
      if (url.startsWith("/design/activities/forks/")) return {
        ...forkReceipt(),
        idempotencyKey: decodeURIComponent(url.slice("/design/activities/forks/".length))
      };
      if (url === "/design/activities/drafts/draft-fork") return draftDetail({ draftId: "draft-fork" });
      throw new Error(`Unexpected GET ${url}`);
    });
    const rendered = renderPage(getJson, undefined, "/workflows/activity-definitions?definition=definition-1", postJson, [graphContribution()]);

    await waitForText(rendered.container, "Fork recommended version");
    click(buttonByText(rendered.container, "Fork recommended version"));
    click(await enabledButton(rendered.container, "Create fork preview"));
    await waitForText(rendered.container, "Apply reviewed fork");
    click(buttonByText(rendered.container, "Apply reviewed fork"));

    await waitFor(() => expect(new URLSearchParams(window.location.search).get("draft")).toBe("draft-fork"));
    expect(getJson.mock.calls.some(([url]) => String(url).startsWith("/design/activities/forks/activity-fork-apply-"))).toBe(true);
    await rendered.unmount();
  });

  it("moves the recommendation with exact head, current recommendation, target lifecycle, and reason", async () => {
    const definitionView = lifecycleDefinition("version-2", "version-1");
    const versions = [
      lifecycleVersion("version-1", "1.0.0", true),
      lifecycleVersion("version-2", "2.0.0", false, "Active", [{ action: "set-recommendation", allowed: true }])
    ];
    const putJson = vi.fn(async () => ({
      definitionId: "definition-1",
      headVersionId: "version-2",
      recommendedVersionId: "version-2",
      changedAt: "2026-07-18T12:00:00Z",
      reason: "Validated rollout"
    }));
    const getJson = lifecycleGetJson(definitionView, versions);
    const rendered = renderPage(getJson, undefined, "/workflows/activity-definitions?definition=definition-1&section=versions&version=version-2", undefined, [], putJson);

    await waitForText(rendered.container, "Make recommended");
    click(buttonByText(rendered.container, "Make recommended"));
    await waitForText(rendered.container, "Existing placed occurrences");
    change(controlByLabel<HTMLTextAreaElement>(rendered.container, "Reason"), "Validated rollout");
    click(controlByLabel<HTMLInputElement>(rendered.container, "I reviewed"));
    click(await enabledButton(rendered.container, "Move recommendation"));

    await waitFor(() => expect(putJson).toHaveBeenCalledWith("/design/activities/definitions/definition-1/recommendation", {
      expectedDefinitionHeadVersionId: "version-2",
      expectedRecommendedVersionId: "version-1",
      recommendedVersionId: "version-2",
      expectedRecommendedVersionLifecycle: "Active",
      reason: "Validated rollout"
    }));
    await rendered.unmount();
  });

  it("retires the recommended version only with an exact replacement decision", async () => {
    const definitionView = lifecycleDefinition("version-2", "version-1");
    const versions = [
      lifecycleVersion("version-1", "1.0.0", true, "Active", [{ action: "retire-version", allowed: true }]),
      lifecycleVersion("version-2", "2.0.0", false)
    ];
    const postJson = vi.fn(async () => ({ versionId: "version-1", lifecycle: "Retired", reason: "Superseded", changedAt: "2026-07-18T12:00:00Z" }));
    const rendered = renderPage(lifecycleGetJson(definitionView, versions), undefined, "/workflows/activity-definitions?definition=definition-1&section=versions&version=version-1", postJson);

    await waitForText(rendered.container, "Retire");
    click(buttonByText(rendered.container, "Retire"));
    await waitForText(rendered.container, "Required recommendation decision");
    change(controlByLabel<HTMLTextAreaElement>(rendered.container, "Reason"), "Superseded");
    click(controlByLabel<HTMLInputElement>(rendered.container, "I reviewed"));
    click(await enabledButton(rendered.container, "Retire exact version"));

    await waitFor(() => expect(postJson).toHaveBeenCalledWith("/design/activities/versions/version-1/retire", {
      expectedLifecycle: "Active",
      reason: "Superseded",
      recommendationDecision: {
        expectedDefinitionHeadVersionId: "version-2",
        expectedRecommendedVersionId: "version-1",
        disposition: "Replace",
        replacementVersionId: "version-2",
        expectedReplacementLifecycle: "Active"
      }
    }));
    await rendered.unmount();
  });

  it("restores a retired version without changing recommendation", async () => {
    const definitionView = lifecycleDefinition("version-2", "version-2");
    const versions = [
      lifecycleVersion("version-1", "1.0.0", false, "Retired", [{ action: "restore-version", allowed: true }]),
      lifecycleVersion("version-2", "2.0.0", true)
    ];
    const postJson = vi.fn(async () => ({ versionId: "version-1", lifecycle: "Active", reason: "Incident cleared", changedAt: "2026-07-18T12:00:00Z" }));
    const rendered = renderPage(lifecycleGetJson(definitionView, versions), undefined, "/workflows/activity-definitions?definition=definition-1&section=versions&version=version-1", postJson);

    await waitForText(rendered.container, "Restore");
    click(buttonByText(rendered.container, "Restore"));
    await waitForText(rendered.container, "Restoration does not recommend");
    change(controlByLabel<HTMLTextAreaElement>(rendered.container, "Reason"), "Incident cleared");
    click(controlByLabel<HTMLInputElement>(rendered.container, "I reviewed"));
    click(await enabledButton(rendered.container, "Restore exact version"));

    await waitFor(() => expect(postJson).toHaveBeenCalledWith("/design/activities/versions/version-1/restore", {
      expectedLifecycle: "Retired",
      reason: "Incident cleared",
      recommendationDecision: null
    }));
    await rendered.unmount();
  });

  it("requires authoritative direct dependencies, bounded as-of usage, and typed semantic-version confirmation for terminal revocation", async () => {
    const definitionView = lifecycleDefinition("version-2", "version-2");
    const versions = [
      lifecycleVersion("version-1", "1.0.0", false, "Retired", [{ action: "revoke-version", allowed: true }]),
      lifecycleVersion("version-2", "2.0.0", true)
    ];
    const postJson = vi.fn(async () => ({ versionId: "version-1", lifecycle: "Revoked", reason: "Security response", changedAt: "2026-07-18T12:00:00Z" }));
    const getJson = lifecycleGetJson(definitionView, versions, authoritativeUsageEvidence());
    const rendered = renderPage(getJson, undefined, "/workflows/activity-definitions?definition=definition-1&section=versions&version=version-1", postJson);

    await waitForText(rendered.container, "Revoke");
    click(buttonByText(rendered.container, "Revoke"));
    await waitForText(rendered.container, "Visible inbound uses");
    expect(rendered.container.textContent).toContain("7/18/2026");
    expect(rendered.container.textContent).toContain("Immutable artifacts, identities, dependency evidence, and Runtime Evidence are preserved");
    change(controlByLabel<HTMLInputElement>(rendered.container, "Type semantic version"), "1.0.0");
    change(controlByLabel<HTMLTextAreaElement>(rendered.container, "Reason"), "Security response");
    click(controlByLabel<HTMLInputElement>(rendered.container, "I reviewed"));
    click(await enabledButton(rendered.container, "Revoke exact version permanently"));

    await waitFor(() => expect(postJson).toHaveBeenCalledWith("/design/activities/versions/version-1/revoke", {
      expectedLifecycle: "Retired",
      reason: "Security response",
      recommendationDecision: null
    }));
    await rendered.unmount();
  });

  it("keeps stale lifecycle conflicts privacy-safe and requires a fresh review", async () => {
    const definitionView = lifecycleDefinition("version-1", "version-1");
    const versions = [lifecycleVersion("version-1", "1.0.0", true, "Active", [{ action: "retire-version", allowed: true }])];
    const postJson = vi.fn(async () => { throw new StudioHttpError(409, "hidden current version identity", null); });
    const rendered = renderPage(lifecycleGetJson(definitionView, versions), undefined, "/workflows/activity-definitions?definition=definition-1&section=versions&version=version-1", postJson);

    await waitForText(rendered.container, "Retire");
    click(buttonByText(rendered.container, "Retire"));
    click(controlByLabel<HTMLInputElement>(rendered.container, "Continue with no recommended version"));
    change(controlByLabel<HTMLTextAreaElement>(rendered.container, "Reason"), "Lifecycle cleanup");
    click(controlByLabel<HTMLInputElement>(rendered.container, "I reviewed"));
    click(await enabledButton(rendered.container, "Retire exact version"));

    await waitForText(rendered.container, "changed after review");
    expect(rendered.container.textContent).not.toContain("hidden current version identity");
    await rendered.unmount();
  });

  it.each([403, 404])("purges retained child identities when a draft collection fails closed with %s", async status => {
    let draftReads = 0;
    const getJson = vi.fn(async (url: string) => {
      if (url === "/capabilities") return capabilities();
      if (url.startsWith("/design/activities/definitions?")) return page([definition()]);
      if (url === "/design/activities/definitions/definition-1") return definition();
      if (url.startsWith("/design/activities/definitions/definition-1/drafts?")) {
        draftReads += 1;
        if (draftReads === 1) return page([{ ...draft(), draft: { ...draft().draft, presentationLabel: "Secret draft identity" } }]);
        throw new StudioHttpError(status, "Hidden draft-1", null, { detail: "Hidden draft-1" });
      }
      throw new Error(`Unexpected GET ${url}`);
    });
    const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
    const rendered = renderPage(getJson, queryClient);

    await waitForText(rendered.container, "Invoice evaluator");
    click(rendered.container.querySelector<HTMLElement>("[role='row'][tabindex='0']")!);
    await waitForText(rendered.container, "Stable Activity Definition");
    click(buttonByText(rendered.container, "Drafts"));
    await waitForText(rendered.container, "Secret draft identity");

    await queryClient.refetchQueries({ queryKey: ["activity-design", "definition", "definition-1", "drafts"] });
    await waitForText(rendered.container, "No identities or counts are shown");
    expect(rendered.container.textContent).not.toContain("Secret draft identity");
    expect(rendered.container.textContent).not.toContain("draft-1");
    expect(queryClient.getQueriesData({ queryKey: ["activity-design", "definition", "definition-1", "drafts"] }).every(([, data]) => {
      const childPage = data as { items?: unknown[]; totalCount?: number } | undefined;
      return childPage?.items?.length === 0 && childPage.totalCount === 0;
    })).toBe(true);
    await rendered.unmount();
  });

  it("uses bounded cursor paging and sends filters to the server", async () => {
    const getJson = vi.fn(async (url: string) => {
      if (url === "/capabilities") return capabilities();
      if (url.includes("cursor=cursor-2")) return page([definition({ definitionId: "definition-2", displayName: "Second definition" })]);
      return page([definition()], { continuation: "cursor-2", hasMore: true, totalCount: 2 });
    });
    const rendered = renderPage(getJson);
    await waitForText(rendered.container, "Invoice evaluator");

    change(rendered.container.querySelector<HTMLInputElement>("input[placeholder='Name, type key, or category']")!, "invoice");
    change(rendered.container.querySelector<HTMLSelectElement>("select")!, "Design");
    change(rendered.container.querySelector<HTMLInputElement>("input[placeholder='Exact provider key']")!, "visual-graph");
    await waitFor(() => getJson.mock.calls.some(([url]) => String(url).includes("search=invoice") && String(url).includes("authority=Design") && String(url).includes("providerKey=visual-graph")));

    click(buttonByText(rendered.container, "Next"));
    await waitForText(rendered.container, "Second definition");
    expect(getJson.mock.calls.some(([url]) => String(url).includes("limit=25") && String(url).includes("cursor=cursor-2"))).toBe(true);
    await rendered.unmount();
  });

  it("retains confirmed rows after a failed refresh without exposing backend error text", async () => {
    let reads = 0;
    const getJson = vi.fn(async (url: string) => {
      if (url === "/capabilities") return capabilities();
      reads += 1;
      if (reads === 1) return page([definition()]);
      throw new Error("secret backend detail for hidden-definition-99");
    });
    const rendered = renderPage(getJson);
    await waitForText(rendered.container, "Invoice evaluator");

    click(buttonByText(rendered.container, "Refresh"));
    await waitForText(rendered.container, "Refresh failed; showing retained data");

    expect(rendered.container.textContent).toContain("Invoice evaluator");
    expect(rendered.container.textContent).not.toContain("secret backend detail");
    expect(rendered.container.textContent).not.toContain("hidden-definition-99");
    await rendered.unmount();
  });

  it("clears cached identities and counts when authorization is rejected", async () => {
    const getJson = vi.fn(async (url: string) => {
      if (url === "/capabilities") return capabilities();
      throw new StudioHttpError(403, "Hidden Invoice evaluator definition-1", null, { detail: "Hidden Invoice evaluator definition-1" });
    });
    const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
    queryClient.setQueryData(activityDesignKeys.definitionCollection({ limit: 25, cursor: null, search: "", authority: "", providerKey: "" }), page([definition()]));
    queryClient.setQueryData(activityDesignKeys.definition("definition-old"), definition({ definitionId: "definition-old", displayName: "Previously visible" }));
    queryClient.setQueryData(activityDesignKeys.definitionDrafts({ definitionId: "definition-old", limit: 10, cursor: null }), page([draft()]));
    const rendered = renderPage(getJson, queryClient);

    await waitForText(rendered.container, "No identities or counts were loaded");
    expect(rendered.container.textContent).not.toContain("Invoice evaluator");
    expect(rendered.container.textContent).not.toContain("definition-1");
    await waitFor(() => queryClient.getQueriesData({ queryKey: activityDesignKeys.definitionResources }).every(([, data]) => {
      if (data !== null) throw new Error("Waiting for resource cache redaction.");
    }));
    expect(queryClient.getQueriesData({ queryKey: activityDesignKeys.definitionCollections }).every(([, data]) => {
      const page = data as { items?: unknown[]; totalCount?: number } | undefined;
      return page?.items?.length === 0 && page.totalCount === 0;
    })).toBe(true);
    await rendered.unmount();
  });

  it("restarts from the first page when a retained cursor snapshot expires", async () => {
    let firstPageReads = 0;
    const getJson = vi.fn(async (url: string) => {
      if (url === "/capabilities") return capabilities();
      if (url.includes("cursor=cursor-2")) throw new StudioHttpError(410, "expired", null);
      firstPageReads += 1;
      return page([definition()], { continuation: "cursor-2", hasMore: true, totalCount: 2 });
    });
    const rendered = renderPage(getJson);
    await waitForText(rendered.container, "Invoice evaluator");

    click(buttonByText(rendered.container, "Next"));
    await waitForText(rendered.container, "Collection snapshot expired");
    click(buttonByText(rendered.container, "Restart paging"));
    await waitFor(() => {
      if (firstPageReads < 2) throw new Error("Waiting for a fresh first-page request.");
    });
    expect(rendered.container.textContent).toContain("Invoice evaluator");
    await rendered.unmount();
  });

  it("renders an explicit unavailable contribution and never probes a legacy endpoint", async () => {
    const getJson = vi.fn(async (url: string) => url === "/capabilities" ? { capabilities: [] } : Promise.reject(new Error(`Unexpected domain request ${url}`)));
    const rendered = renderPage(getJson);

    await waitForText(rendered.container, "Unavailable contribution");
    expect(rendered.container.textContent).toContain("No legacy activity fallback is used");
    expect(getJson).toHaveBeenCalledTimes(1);
    expect(getJson).toHaveBeenCalledWith("/capabilities");
    await rendered.unmount();
  });

  it("drops non-allowlisted telemetry fields", () => {
    const listener = vi.fn();
    window.addEventListener(activityDefinitionsObservationEvent, listener);
    try {
      observeActivityDefinitions({
        event: "query-success",
        surface: "collection",
        outcome: "ready",
        resultBand: "one-to-ten",
        displayName: "Secret activity",
        definitionId: "definition-secret",
        providerManifest: { source: "secret" },
        diagnostic: "sensitive free text"
      } as never);
      const detail = (listener.mock.calls[0][0] as CustomEvent).detail;
      expect(detail).toEqual({ event: "query-success", surface: "collection", outcome: "ready", resultBand: "one-to-ten" });
      expect(JSON.stringify(detail)).not.toContain("Secret");
      expect(JSON.stringify(detail)).not.toContain("definition-secret");
    } finally {
      window.removeEventListener(activityDefinitionsObservationEvent, listener);
    }
  });
});

function renderPage(
  getJson: (url: string) => Promise<unknown>,
  queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } }),
  path = "/workflows/activity-definitions",
  postJson: (url: string, body: unknown) => Promise<unknown> = vi.fn(),
  activityEditors: StudioActivityDefinitionImplementationEditorContribution[] = [],
  putJson: (url: string, body: unknown) => Promise<unknown> = vi.fn(),
  navigateToStudioPath?: (path: string) => void
) {
  window.history.replaceState({}, "", path);
  const container = document.createElement("div");
  document.body.appendChild(container);
  const root = createRoot(container);
  const context = { baseUrl: `test://activity-definitions-${Math.random()}`, http: { getJson, postJson, putJson } } as unknown as StudioEndpointContext;
  flushSync(() => root.render(<QueryClientProvider client={queryClient}><ActivityDefinitionsPage context={context} activityEditors={() => activityEditors} navigateToStudioPath={navigateToStudioPath} /></QueryClientProvider>));
  return {
    container,
    async unmount() {
      flushSync(() => root.unmount());
      container.remove();
    }
  };
}

function capabilities(includeAuthoring = false, includeFork = false) {
  return { capabilities: [{ id: "elsa.api.activity-design", contractVersion: "1", links: [
    { rel: "activity-definitions", href: "design/activities/definitions" },
    ...(includeAuthoring ? [{ rel: "activity-authoring-capabilities", href: "design/activities/authoring-capabilities" }] : []),
    { rel: "activity-definition", href: "design/activities/definitions/{definitionId}", templated: true },
    { rel: "activity-definition-drafts", href: "design/activities/definitions/{definitionId}/drafts", templated: true },
    { rel: "activity-definition-draft", href: "design/activities/drafts/{draftId}", templated: true },
    { rel: "activity-definition-versions", href: "design/activities/definitions/{definitionId}/versions", templated: true },
    { rel: "activity-definition-version", href: "design/activities/versions/{versionId}", templated: true },
    { rel: "activity-definition-recommendation", href: "design/activities/definitions/{definitionId}/recommendation", templated: true },
    ...(includeFork ? [
      { rel: "activity-definition-fork-preview", href: "design/activities/definitions/{definitionId}/fork-previews", templated: true },
      { rel: "activity-definition-fork-apply", href: "design/activities/fork-candidates/{candidateId}/apply", templated: true },
      { rel: "activity-definition-fork-status", href: "design/activities/forks/{idempotencyKey}", templated: true }
    ] : [])
  ] }] };
}

function page<T>(items: T[], options: { continuation?: string; hasMore?: boolean; totalCount?: number } = {}): ActivityManagementPage<T> {
  return { items, count: items.length, totalCount: options.totalCount ?? items.length, hasMore: options.hasMore ?? false, continuation: options.continuation ?? null, snapshot: { snapshotId: "snapshot-1", asOf: "2026-07-17T10:00:00Z" } };
}

function definition(overrides: Partial<ActivityDefinitionManagementView["definition"]> = {}, actions: ActivityDefinitionManagementView["actions"] = []): ActivityDefinitionManagementView {
  return {
    definition: { definitionId: "definition-1", activityTypeKey: "Contoso.InvoiceEvaluator", tenantId: null, category: "Finance", displayName: "Invoice evaluator", description: "Evaluates invoice policy.", contentAuthority: { kind: "design", authorityKey: "elsa.activity-design", sourceId: null }, forkedFrom: null, headVersionId: "version-1", recommendedVersionId: "version-1", ...overrides },
    lifecycle: { draftCount: 1, versionCount: 1, head: { versionId: "version-1", version: "1.0.0", lifecycle: "active", providerKey: "visual-graph", providerSchemaVersion: "1" }, recommendation: { versionId: "version-1", version: "1.0.0", lifecycle: "active", providerKey: "visual-graph", providerSchemaVersion: "1" } },
    actions,
    updatedAt: "2026-07-17T10:00:00Z"
  };
}

function draft(overrides: Partial<ActivityDefinitionDraftManagementView["draft"]> = {}): ActivityDefinitionDraftManagementView {
  return { draft: { draftId: "draft-1", definitionId: "definition-1", revision: 3, sourceVersionId: "version-1", status: "Active", providerKey: "visual-graph", providerSchemaVersion: "1", updatedAt: "2026-07-17T10:00:00Z", presentationLabel: null, ...overrides }, actions: [] };
}

function version(overrides: Partial<ActivityDefinitionVersionManagementView> = {}): ActivityDefinitionVersionManagementView {
  return { version: { versionId: "version-1", definitionId: "definition-1", version: "1.0.0", lifecycle: "active", publishedAt: "2026-07-17T09:00:00Z" }, providerKey: "visual-graph", providerSchemaVersion: "1", isRecommended: true, actions: [], ...overrides };
}

function lifecycleDefinition(headVersionId: string, recommendedVersionId: string | null): ActivityDefinitionManagementView {
  const view = definition({ headVersionId, recommendedVersionId });
  const reference = (versionId: string) => ({
    versionId,
    version: versionId === "version-1" ? "1.0.0" : "2.0.0",
    lifecycle: "Active",
    providerKey: "visual-graph",
    providerSchemaVersion: "1"
  });
  return {
    ...view,
    lifecycle: {
      ...view.lifecycle,
      versionCount: 2,
      head: reference(headVersionId),
      recommendation: recommendedVersionId ? reference(recommendedVersionId) : null
    }
  };
}

function lifecycleVersion(
  versionId: string,
  semanticVersion: string,
  isRecommended: boolean,
  lifecycle = "Active",
  actions: ActivityDefinitionVersionManagementView["actions"] = []
): ActivityDefinitionVersionManagementView {
  return {
    version: { versionId, definitionId: "definition-1", version: semanticVersion, lifecycle, publishedAt: "2026-07-17T09:00:00Z" },
    providerKey: "visual-graph",
    providerSchemaVersion: "1",
    isRecommended,
    actions
  };
}

function lifecycleGetJson(
  definitionView: ActivityDefinitionManagementView,
  versions: ActivityDefinitionVersionManagementView[],
  evidence = authoritativeUsageEvidence()
) {
  return vi.fn(async (url: string) => {
    if (url === "/capabilities") return capabilities();
    if (url === "/design/activities/definitions/definition-1") return definitionView;
    if (url.startsWith("/design/activities/definitions/definition-1/versions?")) return page(versions);
    if (url.includes("/dependencies?")) return url.includes("direction=outbound") ? evidence.directDependencies : evidence.inboundUsage;
    const exact = versions.find(item => url === `/design/activities/versions/${item.version.versionId}`);
    if (exact) return versionDetail({
      definition: definitionView.definition,
      versionId: exact.version.versionId,
      version: exact.version.version,
      lifecycle: exact.version.lifecycle,
      publishedAt: exact.version.publishedAt
    });
    throw new Error(`Unexpected GET ${url}`);
  });
}

function authoritativeUsageEvidence() {
  const base = {
    root: { kind: "ActivityVersion", definitionId: "definition-1", versionId: "version-1", version: "1.0.0", lifecycle: "Retired" },
    items: [{
      relationshipId: "relationship-1",
      owner: { kind: "ActivityVersion", definitionId: "owner-1", versionId: "owner-version-1" },
      dependency: { kind: "ActivityVersion", definitionId: "definition-1", versionId: "version-1" },
      occurrence: { occurrenceId: "occurrence-1", nodeOrigin: [] },
      isDirect: true,
      depth: 1,
      path: []
    }],
    nextCursor: null
  };
  return {
    directDependencies: {
      ...base,
      query: { direction: "Outbound", transitive: false, include: ["Versions"] },
      consistency: { kind: "AuthoritativeDirect", isAuthoritative: true, asOfSequence: null, asOf: null, rebuildId: null }
    },
    inboundUsage: {
      ...base,
      query: { direction: "Inbound", transitive: true, include: ["Drafts", "Versions"] },
      consistency: { kind: "DerivedProjection", isAuthoritative: false, asOfSequence: 42, asOf: "2026-07-18T12:00:00Z", rebuildId: "rebuild-42" }
    }
  };
}

function draftDetail(overrides: Partial<{ draftId: string; presentationLabel: string | null }> = {}) { return { ...draftDetailBase(), ...overrides }; }
function draftDetailBase() { return { draftId: "draft-1", definitionId: "definition-1", tenantId: null, revision: 3, sourceVersionId: "version-1", status: "Active", contract: emptyContract(), provider: { providerKey: "visual-graph", schemaVersion: "1", manifestFingerprint: "fingerprint", payload: { hidden: "not rendered" } }, layout: [], validation: null, createdAt: "2026-07-17T09:00:00Z", updatedAt: "2026-07-17T10:00:00Z", presentationLabel: null }; }
function versionDetail(overrides: Record<string, unknown> = {}) { return { definition: definition().definition, versionId: "version-1", version: "1.0.0", sourceDraftId: "draft-1", sourceVersionId: null, contract: emptyContract(), provider: { providerKey: "visual-graph", schemaVersion: "1", manifestFingerprint: "fingerprint", payload: { hidden: "not rendered" } }, template: {}, lifecycle: "Active", publishedAt: "2026-07-17T09:00:00Z", ...overrides }; }
function emptyContract() { return { contractSchemaVersion: "1", inputs: [], outputs: [], outcomes: [] }; }
function authoringCapabilities() { return { contractSchemaVersions: ["1"], activityTypeKeyRules: { serverGenerated: true, allowsPreCreationOverride: false, immutable: true, prefix: "elsa.user", pattern: "^elsa\\\\.user\\\\..+$", maximumLength: 160, collisionScope: "tenant" }, providers: [{ providerKey: "visual-graph", displayName: "Visual graph", manifestSchemas: [{ schemaVersion: "1", isAuthorable: true, migratableFromSchemaVersions: ["1"] }], requiredOutcomes: [{ referenceKey: "done", name: "Done", isEmitted: true, description: null }] }], types: [], storageDriverKeys: [], snapshotFingerprint: "sha256:test" }; }
function graphContribution(): StudioActivityDefinitionImplementationEditorContribution { return { id: "visual-graph", providerKey: "visual-graph", providerSchemaVersion: "1", createInitialImplementation: () => ({ payload: { root: null }, layout: [] }), component: () => <div>Visual graph editor</div> }; }

function forkPreview() {
  return {
    candidateId: "candidate-signed",
    requestFingerprint: `sha256:${"a".repeat(64)}`,
    status: "Reserved",
    accessBinding: { fingerprint: `sha256:${"b".repeat(64)}` },
    source: { definitionId: "definition-1", versionId: "version-1", version: "1.0.0", lifecycle: "Active", providerKey: "visual-graph", providerSchemaVersion: "1", providerFingerprint: `sha256:${"c".repeat(64)}` },
    presentation: { category: "Finance", displayName: "Invoice evaluator", description: "Evaluates invoice policy." },
    target: { definitionId: "definition-fork", activityTypeKey: "elsa.user.invoice-evaluator.forked", draftId: "draft-fork", providerKey: "visual-graph", providerSchemaVersion: "1", manifestFingerprint: `sha256:${"d".repeat(64)}`, contract: emptyContract() },
    providerMigration: { sourceProviderKey: "visual-graph", sourceProviderSchemaVersion: "1", targetProviderKey: "visual-graph", targetProviderSchemaVersion: "1", targetManifestFingerprint: `sha256:${"d".repeat(64)}`, diagnostics: [] },
    contractComparison: { sourceFingerprint: `sha256:${"e".repeat(64)}`, targetFingerprint: `sha256:${"e".repeat(64)}`, isCompatible: true, changes: [] },
    createdAt: "2026-07-17T10:00:00Z",
    expiresAt: "2026-07-17T10:15:00Z"
  };
}

function forkReceipt() {
  return {
    idempotencyKey: "activity-fork-apply-test",
    candidateId: "candidate-signed",
    requestFingerprint: `sha256:${"a".repeat(64)}`,
    outcome: "Applied",
    accessBinding: { fingerprint: `sha256:${"b".repeat(64)}` },
    definition: { ...definition().definition, definitionId: "definition-fork", activityTypeKey: "elsa.user.invoice-evaluator.forked", contentAuthority: { kind: "Design", authorityKey: "elsa.activity-design" }, forkedFrom: { definitionId: "definition-1", versionId: "version-1", version: "1.0.0" } },
    draft: { ...draft().draft, draftId: "draft-fork", definitionId: "definition-fork", revision: 1 },
    appliedAt: "2026-07-17T10:01:00Z"
  };
}

function buttonByText(container: HTMLElement, text: string) {
  const button = [...container.querySelectorAll("button")].find(candidate => candidate.textContent?.includes(text));
  if (!button) throw new Error(`Button '${text}' not found.`);
  return button;
}

async function enabledButton(container: HTMLElement, text: string) {
  let button!: HTMLButtonElement;
  await waitFor(() => {
    button = buttonByText(container, text) as HTMLButtonElement;
    expect(button.disabled).toBe(false);
  });
  return button;
}

function click(element: Element) { flushSync(() => element.dispatchEvent(new MouseEvent("click", { bubbles: true }))); }
function change(element: HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement, value: string) { flushSync(() => { const prototype = element instanceof HTMLInputElement ? HTMLInputElement.prototype : element instanceof HTMLSelectElement ? HTMLSelectElement.prototype : HTMLTextAreaElement.prototype; const setter = Object.getOwnPropertyDescriptor(prototype, "value")?.set; setter?.call(element, value); element.dispatchEvent(new Event("change", { bubbles: true })); element.dispatchEvent(new Event("input", { bubbles: true })); }); }
function controlByLabel<T extends HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement>(container: HTMLElement, text: string) {
  const label = [...container.querySelectorAll("label")].find(candidate => candidate.textContent?.includes(text));
  const control = label?.querySelector("input, select, textarea");
  if (!control) throw new Error(`Control labelled '${text}' not found.`);
  return control as T;
}
async function waitForText(container: HTMLElement, text: string) { await waitFor(() => { if (!container.textContent?.includes(text)) throw new Error(`Waiting for '${text}'.`); }); }
async function waitFor(assertion: () => void) { await vi.waitFor(assertion, { timeout: 10_000 }); }
