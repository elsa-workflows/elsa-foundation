import React from "react";
import { flushSync } from "react-dom";
import { createRoot } from "react-dom/client";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { afterEach, describe, expect, it, vi } from "vitest";
import {
  StudioHttpError,
  requestStudioNavigation,
  type StudioActivityDefinitionImplementationEditorProps,
  type StudioActivityDefinitionImplementationEditorContribution,
  type StudioEndpointContext,
  type StudioRuntimeSettings
} from "@elsa-workflows/studio-sdk";
import { ActivityDefinitionsPage } from "../ActivityDefinitionsPage";
import { clearApiCapabilityCache } from "../api/capabilities";
import { activityDesignKeys } from "../api/activityDesign";
import { clearActivityDefinitionRecoveryForIdentity, createActivityDefinitionRecoveryStore } from "../activityDefinitionRecovery";
import { activityDefinitionsObservationEvent, type ActivityDefinitionsObservation } from "../activityDefinitionObservability";

afterEach(() => {
  clearApiCapabilityCache();
  window.history.replaceState({}, "", "/");
  window.localStorage.clear();
  vi.useRealTimers();
});

describe("Activity Definition authoring", () => {
  it("creates with the sole authorable provider and enters the exact initial draft", async () => {
    const postJson = vi.fn(async (_url: string, _body: unknown) => ({
      definition: definitionIdentity(),
      draft: draftSummary()
    }));
    const rendered = renderPage({
      getJson: async (url: string) => {
        if (url === "/capabilities") return capabilities();
        if (url.startsWith("/design/activities/definitions?")) return page([]);
        if (url === "/design/activities/authoring-capabilities") return authoringCapabilities();
        if (url === "/design/activities/drafts/activity-draft-1") return fullDraft();
        throw new Error(`Unexpected GET ${url}`);
      },
      postJson
    });

    await waitForText(rendered.container, "No Activity Definitions yet");
    click(buttonByText(rendered.container, "Create Activity Definition"));
    await waitForText(rendered.container, "Activity Graph");
    expect(rendered.container.querySelector<HTMLSelectElement>("select[name='provider']")?.value).toBe("elsa.activity-graph|1");

    change(rendered.container.querySelector<HTMLInputElement>("input[name='displayName']")!, "Invoice evaluator");
    change(rendered.container.querySelector<HTMLInputElement>("input[name='category']")!, "Finance");
    click(buttonByText(rendered.container, "Create definition"));

    await waitFor(() => expect(postJson).toHaveBeenCalledTimes(1));
    expect(postJson).toHaveBeenCalledWith("/design/activities/definitions", {
      category: "Finance",
      displayName: "Invoice evaluator",
      description: null,
      provider: {
        providerKey: "elsa.activity-graph",
        schemaVersion: "1",
        payload: {
          rootActivity: { nodeId: "root", activityVersionId: "", inputs: [], outputs: [], structure: null },
          variables: [],
          outputMappings: []
        }
      },
      contract: {
        contractSchemaVersion: "1",
        inputs: [],
        outputs: [],
        outcomes: [{ referenceKey: "done", name: "Done", isEmitted: true, description: null }]
      },
      layout: []
    });
    await waitFor(() => {
      const route = new URLSearchParams(window.location.search);
      expect(route.get("definition")).toBe("activity-def-1");
      expect(route.get("draft")).toBe("activity-draft-1");
      expect(route.get("section")).toBe("editor");
    });
    await waitForText(rendered.container, "Saved revision 1");
    await rendered.unmount();
  });

  it("does not preselect among multiple providers and sends an intentional exact key override", async () => {
    const capabilitiesResponse = authoringCapabilities();
    capabilitiesResponse.providers.push({
      providerKey: "contoso.script",
      displayName: "Script",
      manifestSchemas: [{ schemaVersion: "1", isAuthorable: true, migratableFromSchemaVersions: [] }],
      requiredOutcomes: []
    });
    const postJson = vi.fn(async (_url: string, _body: unknown) => ({ definition: definitionIdentity(), draft: draftSummary() }));
    const rendered = renderPage({
      contributions: [graphContribution(), { ...graphContribution(), id: "script", providerKey: "contoso.script" }],
      postJson,
      getJson: async (url: string) => {
        if (url === "/capabilities") return capabilities();
        if (url.startsWith("/design/activities/definitions?")) return page([]);
        if (url === "/design/activities/authoring-capabilities") return capabilitiesResponse;
        if (url === "/design/activities/drafts/activity-draft-1") return fullDraft();
        throw new Error(`Unexpected GET ${url}`);
      }
    });

    await waitForText(rendered.container, "No Activity Definitions yet");
    click(buttonByText(rendered.container, "Create Activity Definition"));
    await waitForText(rendered.container, "Script");
    const provider = rendered.container.querySelector<HTMLSelectElement>("select[name='provider']")!;
    expect(provider.value).toBe("");
    change(provider, "elsa.activity-graph|1");
    change(rendered.container.querySelector<HTMLInputElement>("input[name='displayName']")!, "Invoice evaluator");
    click(buttonByText(rendered.container, "Set an exact key"));
    change(rendered.container.querySelector<HTMLInputElement>("input[name='activityTypeKey']")!, "elsa.user.finance.invoice-evaluator");
    click(buttonByText(rendered.container, "Create definition"));

    await waitFor(() => expect(postJson).toHaveBeenCalledWith("/design/activities/definitions", expect.objectContaining({ activityTypeKey: "elsa.user.finance.invoice-evaluator" })));
    await rendered.unmount();
  });

  it("does not preselect a sole non-graph provider", async () => {
    const response = authoringCapabilities();
    response.providers = [{
      providerKey: "contoso.script",
      displayName: "Script",
      manifestSchemas: [{ schemaVersion: "1", isAuthorable: true, migratableFromSchemaVersions: [] }],
      requiredOutcomes: []
    }];
    const rendered = renderPage({
      contributions: [{ ...graphContribution(), id: "script", providerKey: "contoso.script" }],
      getJson: async (url: string) => {
        if (url === "/capabilities") return capabilities();
        if (url.startsWith("/design/activities/definitions?")) return page([]);
        if (url === "/design/activities/authoring-capabilities") return response;
        throw new Error(`Unexpected GET ${url}`);
      }
    });

    await waitForText(rendered.container, "No Activity Definitions yet");
    click(buttonByText(rendered.container, "Create Activity Definition"));
    await waitForText(rendered.container, "Script");
    expect(rendered.container.querySelector<HTMLSelectElement>("select[name='provider']")?.value).toBe("");
    await rendered.unmount();
  });

  it("clears an exact key override when the author returns to generated identity", async () => {
    const postJson = vi.fn(async (_url: string, _body: unknown) => ({ definition: definitionIdentity(), draft: draftSummary() }));
    const rendered = renderPage({
      postJson,
      getJson: async (url: string) => {
        if (url === "/capabilities") return capabilities();
        if (url.startsWith("/design/activities/definitions?")) return page([]);
        if (url === "/design/activities/authoring-capabilities") return authoringCapabilities();
        if (url === "/design/activities/drafts/activity-draft-1") return fullDraft();
        throw new Error(`Unexpected GET ${url}`);
      }
    });

    await waitForText(rendered.container, "No Activity Definitions yet");
    click(buttonByText(rendered.container, "Create Activity Definition"));
    await waitForText(rendered.container, "Activity Graph");
    change(rendered.container.querySelector<HTMLInputElement>("input[name='displayName']")!, "Generated identity");
    click(buttonByText(rendered.container, "Set an exact key"));
    change(rendered.container.querySelector<HTMLInputElement>("input[name='activityTypeKey']")!, "elsa.user.hidden.override");
    click(buttonByText(rendered.container, "Use generated key"));
    click(buttonByText(rendered.container, "Create definition"));

    await waitFor(() => expect(postJson).toHaveBeenCalledTimes(1));
    expect(postJson.mock.calls[0]?.[1]).not.toHaveProperty("activityTypeKey");
    await rendered.unmount();
  });

  it("shows an honest unavailable state when no exact provider editor contribution exists", async () => {
    const rendered = renderPage({
      path: "/workflows/activity-definitions?definition=activity-def-1&section=editor&draft=activity-draft-1",
      contributions: [],
      getJson: async (url: string) => {
        if (url === "/capabilities") return capabilities();
        if (url === "/design/activities/definitions/activity-def-1") return managementDefinition();
        if (url === "/design/activities/drafts/activity-draft-1") return fullDraft();
        throw new Error(`Unexpected GET ${url}`);
      }
    });

    await waitForText(rendered.container, "Implementation editor unavailable");
    expect(rendered.container.textContent).toContain("elsa.activity-graph");
    expect(rendered.container.textContent).toContain("schema 1");
    expect(rendered.container.textContent).not.toContain("JSON editor");
    await rendered.unmount();
  });

  it("contains provider render failures inside the autosave shell", async () => {
    const consoleError = vi.spyOn(console, "error").mockImplementation(() => undefined);
    const observations: ActivityDefinitionsObservation[] = [];
    const observe = (event: Event) => observations.push((event as CustomEvent<ActivityDefinitionsObservation>).detail);
    window.addEventListener(activityDefinitionsObservationEvent, observe);
    const rendered = renderPage({
      path: "/workflows/activity-definitions?definition=activity-def-1&section=editor&draft=activity-draft-1",
      contributions: [{ ...graphContribution(), component: () => { throw new Error("provider render detail"); } }],
      getJson: async (url: string) => {
        if (url === "/capabilities") return capabilities();
        if (url === "/design/activities/drafts/activity-draft-1") return fullDraft({ revision: 3 });
        throw new Error(`Unexpected GET ${url}`);
      }
    });

    await waitForText(rendered.container, "Implementation editor unavailable");
    expect(rendered.container.textContent).toContain("Saved revision 3");
    expect(rendered.container.textContent).not.toContain("provider render detail");
    expect(buttonByText(rendered.container, "Validate saved revision")).toBeTruthy();
    expect(observations).toContainEqual(expect.objectContaining({ event: "provider-editor", outcome: "unavailable", providerKey: "elsa.activity-graph", providerSchemaVersion: "1" }));
    await rendered.unmount();
    window.removeEventListener(activityDefinitionsObservationEvent, observe);
    consoleError.mockRestore();
  });

  it("serializes trailing autosaves against each exact returned revision", async () => {
    let resolveFirst!: (value: unknown) => void;
    const firstSave = new Promise(resolve => { resolveFirst = resolve; });
    const putJson = vi.fn(async (_url: string, body: unknown) => {
      const request = body as { expectedRevision: number; provider: { payload: { edit: number } } };
      if (request.expectedRevision === 3) return firstSave;
      return fullDraft({ revision: 5, payload: request.provider.payload });
    });
    const rendered = renderPage({
      path: "/workflows/activity-definitions?definition=activity-def-1&section=editor&draft=activity-draft-1",
      contributions: [editingContribution()],
      putJson,
      getJson: async (url: string) => {
        if (url === "/capabilities") return capabilities();
        if (url === "/design/activities/drafts/activity-draft-1") return fullDraft({ revision: 3, payload: { edit: 0 } });
        throw new Error(`Unexpected GET ${url}`);
      }
    });

    await waitForText(rendered.container, "Saved revision 3");
    click(buttonByText(rendered.container, "Edit implementation"));
    await waitFor(() => expect(putJson).toHaveBeenCalledTimes(1));
    click(buttonByText(rendered.container, "Edit implementation"));
    resolveFirst(fullDraft({ revision: 4, payload: { edit: 1 } }));

    await waitFor(() => expect(putJson).toHaveBeenCalledTimes(2));
    expect(putJson.mock.calls[0]).toEqual(["/design/activities/drafts/activity-draft-1", expect.objectContaining({ expectedRevision: 3, provider: expect.objectContaining({ payload: { edit: 1 } }) })]);
    expect(putJson.mock.calls[1]).toEqual(["/design/activities/drafts/activity-draft-1", expect.objectContaining({ expectedRevision: 4, provider: expect.objectContaining({ payload: { edit: 2 } }) })]);
    await waitForText(rendered.container, "Saved revision 5");
    await rendered.unmount();
  }, 10_000);

  it("preserves stale local work and creates an explicit parallel recovery draft", async () => {
    const putJson = vi.fn(async () => {
      throw new StudioHttpError(409, "stale", null, {
        errorCode: "activity.draft.stale-revision",
        recovery: { currentRevision: 9, relation: "activity-draft-conflict-copies" }
      });
    });
    const postJson = vi.fn(async (_url: string, body: unknown) => fullDraft({ draftId: "activity-draft-recovery", revision: 1, payload: (body as { provider: { payload: unknown } }).provider.payload }));
    const rendered = renderPage({
      path: "/workflows/activity-definitions?definition=activity-def-1&section=editor&draft=activity-draft-1",
      contributions: [editingContribution()],
      putJson,
      postJson,
      getJson: async (url: string) => {
        if (url === "/capabilities") return capabilities();
        if (url === "/design/activities/drafts/activity-draft-1") return fullDraft({ revision: 3, payload: { edit: 0 } });
        if (url === "/design/activities/drafts/activity-draft-recovery") return fullDraft({ draftId: "activity-draft-recovery", revision: 1, payload: { edit: 1 } });
        throw new Error(`Unexpected GET ${url}`);
      }
    });

    await waitForText(rendered.container, "Saved revision 3");
    click(buttonByText(rendered.container, "Edit implementation"));
    await waitForText(rendered.container, "Local work preserved");
    expect(rendered.container.textContent).toContain("revision 9");
    expect(rendered.container.textContent).not.toContain("Force overwrite");
    click(buttonByText(rendered.container, "Create parallel recovery draft"));

    await waitFor(() => expect(postJson).toHaveBeenCalledWith("/design/activities/drafts/activity-draft-1/conflict-copies", expect.objectContaining({
      expectedSourceRevision: 9,
      provider: expect.objectContaining({ payload: { edit: 1 } })
    })));
    await waitFor(() => expect(new URLSearchParams(window.location.search).get("draft")).toBe("activity-draft-recovery"));
    await rendered.unmount();
  }, 10_000);

  it("previews scoped local recovery before applying it and clears it after save", async () => {
    const runtime: StudioRuntimeSettings = {
      identity: { tenantId: "tenant-1", subject: "author-1" },
      activityDefinitions: { localRecovery: { enabled: true, ttlMinutes: 30 } }
    };
    const offline = renderPage({
      runtime,
      path: "/workflows/activity-definitions?definition=activity-def-1&section=editor&draft=activity-draft-1",
      contributions: [editingContribution()],
      putJson: vi.fn(async () => { throw new TypeError("offline"); }),
      getJson: async (url: string) => {
        if (url === "/capabilities") return capabilities();
        if (url === "/design/activities/drafts/activity-draft-1") return fullDraft({ revision: 3, payload: { edit: 0 } });
        throw new Error(`Unexpected GET ${url}`);
      }
    });
    await waitForText(offline.container, "Saved revision 3");
    click(buttonByText(offline.container, "Edit implementation 0"));
    await waitForText(offline.container, "Offline");
    expect(window.localStorage.length).toBe(1);
    await offline.unmount();

    const putJson = vi.fn(async (_url: string, body: unknown) => fullDraft({ revision: 4, payload: (body as { provider: { payload: unknown } }).provider.payload }));
    const recovered = renderPage({
      runtime,
      path: "/workflows/activity-definitions?definition=activity-def-1&section=editor&draft=activity-draft-1",
      contributions: [editingContribution()],
      putJson,
      getJson: async (url: string) => {
        if (url === "/capabilities") return capabilities();
        if (url === "/design/activities/drafts/activity-draft-1") return fullDraft({ revision: 3, payload: { edit: 0 } });
        throw new Error(`Unexpected GET ${url}`);
      }
    });

    await waitForText(recovered.container, "Unsaved local recovery available");
    expect(recovered.container.textContent).toContain("revision 3");
    expect(recovered.container.textContent).toContain("elsa.activity-graph schema 1");
    expect(recovered.container.textContent).toContain("never restores it silently");
    expect(recovered.container.textContent).toContain("Review recovered content");
    expect((buttonByText(recovered.container, "Review content before recovery") as HTMLButtonElement).disabled).toBe(true);
    expect(buttonByText(recovered.container, "Edit implementation 0")).toBeTruthy();
    openDetails(recovered.container.querySelector(".ad-recovery-card details")!);
    expect(recovered.container.textContent).toContain('"edit": 1');
    click(buttonByText(recovered.container, "Apply local recovery"));
    await waitForText(recovered.container, "Edit implementation 1");
    await waitFor(() => expect(putJson).toHaveBeenCalledWith("/design/activities/drafts/activity-draft-1", expect.objectContaining({ expectedRevision: 3, provider: expect.objectContaining({ payload: { edit: 1 } }) })));
    await waitForText(recovered.container, "Saved revision 4");
    expect(window.localStorage.length).toBe(0);
    await recovered.unmount();
  }, 12_000);

  it("creates a parallel draft when local recovery is based on an older server revision", async () => {
    const runtime: StudioRuntimeSettings = {
      identity: { tenantId: "tenant-1", subject: "author-1" },
      activityDefinitions: { localRecovery: { enabled: true, ttlMinutes: 30 } }
    };
    createActivityDefinitionRecoveryStore(runtime.activityDefinitions?.localRecovery, runtime.identity)!
      .write(fullDraft({ revision: 3, payload: { edit: 7 } }));
    const putJson = vi.fn();
    const postJson = vi.fn(async (_url: string, body: unknown) => fullDraft({ draftId: "activity-draft-recovery", revision: 1, payload: (body as { provider: { payload: unknown } }).provider.payload }));
    const rendered = renderPage({
      runtime,
      path: "/workflows/activity-definitions?definition=activity-def-1&section=editor&draft=activity-draft-1",
      contributions: [editingContribution()],
      putJson,
      postJson,
      getJson: async (url: string) => {
        if (url === "/capabilities") return capabilities();
        if (url === "/design/activities/drafts/activity-draft-1") return fullDraft({ revision: 4, payload: { edit: 8 } });
        if (url === "/design/activities/drafts/activity-draft-recovery") return fullDraft({ draftId: "activity-draft-recovery", revision: 1, payload: { edit: 7 } });
        throw new Error(`Unexpected GET ${url}`);
      }
    });

    await waitForText(rendered.container, "server is now at revision 4");
    openDetails(rendered.container.querySelector(".ad-recovery-card details")!);
    click(buttonByText(rendered.container, "Create recovery draft"));
    await waitFor(() => expect(postJson).toHaveBeenCalledWith("/design/activities/drafts/activity-draft-1/conflict-copies", expect.objectContaining({
      expectedSourceRevision: 4,
      provider: expect.objectContaining({ payload: { edit: 7 } })
    })));
    expect(putJson).not.toHaveBeenCalled();
    await rendered.unmount();
  });

  it("preserves recovery without applying it when the exact provider editor is unavailable", async () => {
    const runtime: StudioRuntimeSettings = {
      identity: { tenantId: "tenant-1", subject: "author-1" },
      activityDefinitions: { localRecovery: { enabled: true } }
    };
    createActivityDefinitionRecoveryStore(runtime.activityDefinitions?.localRecovery, runtime.identity)!
      .write(fullDraft({ revision: 3, payload: { opaque: "local" } }));
    const putJson = vi.fn();
    const postJson = vi.fn();
    const rendered = renderPage({
      runtime,
      path: "/workflows/activity-definitions?definition=activity-def-1&section=editor&draft=activity-draft-1",
      contributions: [],
      putJson,
      postJson,
      getJson: async (url: string) => {
        if (url === "/capabilities") return capabilities();
        if (url === "/design/activities/drafts/activity-draft-1") return fullDraft({ revision: 3, payload: { opaque: "server" } });
        throw new Error(`Unexpected GET ${url}`);
      }
    });

    await waitForText(rendered.container, "Implementation editor unavailable");
    expect(rendered.container.textContent).toContain("will not apply this opaque state");
    expect(rendered.container.textContent).toContain("Review recovered content");
    expect([...rendered.container.querySelectorAll("button")].some(button => button.textContent?.includes("Apply local recovery") || button.textContent?.includes("Create recovery draft"))).toBe(false);
    expect(buttonByText(rendered.container, "Discard recovery")).toBeTruthy();
    expect(putJson).not.toHaveBeenCalled();
    expect(postJson).not.toHaveBeenCalled();
    await rendered.unmount();
  });

  it("reports a failed save and keeps revision-sensitive actions paused", async () => {
    const rendered = renderPage({
      path: "/workflows/activity-definitions?definition=activity-def-1&section=editor&draft=activity-draft-1",
      contributions: [editingContribution()],
      putJson: vi.fn(async () => { throw new Error("safe test failure"); }),
      getJson: async (url: string) => {
        if (url === "/capabilities") return capabilities();
        if (url === "/design/activities/drafts/activity-draft-1") return fullDraft({ revision: 3, payload: { edit: 0 } });
        throw new Error(`Unexpected GET ${url}`);
      }
    });
    await waitForText(rendered.container, "Saved revision 3");
    click(buttonByText(rendered.container, "Edit implementation 0"));
    await waitForText(rendered.container, "Save failed");
    expect(rendered.container.textContent).toContain("Revision-sensitive lifecycle actions and navigation are paused");
    expect((buttonByText(rendered.container, "Validate saved revision") as HTMLButtonElement).disabled).toBe(true);
    expect(rendered.container.textContent).not.toContain("safe test failure");
    await rendered.unmount();
  });

  it("validates only the exact saved revision and gates validation while edits are pending", async () => {
    const postJson = vi.fn(async (_url: string, body: unknown) => ({ draftId: "activity-draft-1", revision: (body as { expectedRevision: number }).expectedRevision, isValid: true, validatedAt: "2026-07-17T10:01:00Z", diagnostics: [] }));
    const rendered = renderPage({
      path: "/workflows/activity-definitions?definition=activity-def-1&section=editor&draft=activity-draft-1",
      contributions: [editingContribution()],
      postJson,
      putJson: vi.fn(async (_url: string, body: unknown) => fullDraft({ revision: 4, payload: (body as { provider: { payload: unknown } }).provider.payload })),
      getJson: async (url: string) => {
        if (url === "/capabilities") return capabilities();
        if (url === "/design/activities/drafts/activity-draft-1") return fullDraft({ revision: 3, payload: { edit: 0 } });
        throw new Error(`Unexpected GET ${url}`);
      }
    });

    await waitForText(rendered.container, "Saved revision 3");
    click(buttonByText(rendered.container, "Validate saved revision"));
    await waitFor(() => expect(postJson).toHaveBeenCalledWith("/design/activities/drafts/activity-draft-1/validate", { expectedRevision: 3 }));
    await waitForText(rendered.container, "Revision 3 passed validation");
    click(buttonByText(rendered.container, "Edit implementation 0"));
    expect((buttonByText(rendered.container, "Validate saved revision") as HTMLButtonElement).disabled).toBe(true);
    await rendered.unmount();
  });

  it("ignores a late validation response after the author advances the draft", async () => {
    let rejectValidation!: (reason: unknown) => void;
    const validation = new Promise((_resolve, reject) => { rejectValidation = reject; });
    const rendered = renderPage({
      path: "/workflows/activity-definitions?definition=activity-def-1&section=editor&draft=activity-draft-1",
      contributions: [editingContribution()],
      postJson: vi.fn(async () => validation),
      putJson: vi.fn(async (_url: string, body: unknown) => fullDraft({ revision: 4, payload: (body as { provider: { payload: unknown } }).provider.payload })),
      getJson: async (url: string) => {
        if (url === "/capabilities") return capabilities();
        if (url === "/design/activities/drafts/activity-draft-1") return fullDraft({ revision: 3, payload: { edit: 0 } });
        throw new Error(`Unexpected GET ${url}`);
      }
    });

    await waitForText(rendered.container, "Saved revision 3");
    click(buttonByText(rendered.container, "Validate saved revision"));
    click(buttonByText(rendered.container, "Edit implementation 0"));
    await waitForText(rendered.container, "Saved revision 4");
    rejectValidation(new StudioHttpError(409, "stale", null, { errorCode: "activity.draft.stale-revision", recovery: { currentRevision: 4 } }));
    await waitFor(() => expect(rendered.container.textContent).not.toContain("Local work preserved"));
    expect(rendered.container.textContent).toContain("Saved revision 4");
    await rendered.unmount();
  });

  it("blocks editor navigation while an unsaved failure is present", async () => {
    const rendered = renderPage({
      path: "/workflows/activity-definitions?definition=activity-def-1&section=editor&draft=activity-draft-1",
      contributions: [editingContribution()],
      putJson: vi.fn(async () => { throw new Error("failed"); }),
      getJson: async (url: string) => {
        if (url === "/capabilities") return capabilities();
        if (url === "/design/activities/drafts/activity-draft-1") return fullDraft({ revision: 3, payload: { edit: 0 } });
        throw new Error(`Unexpected GET ${url}`);
      }
    });

    await waitForText(rendered.container, "Saved revision 3");
    click(buttonByText(rendered.container, "Edit implementation 0"));
    await waitForText(rendered.container, "Save failed");
    expect((buttonByText(rendered.container, "Activity Definition") as HTMLButtonElement).disabled).toBe(true);
    window.history.pushState({}, "", "/workflows/activity-definitions");
    window.dispatchEvent(new PopStateEvent("popstate"));
    expect(new URLSearchParams(window.location.search).get("section")).toBe("editor");
    expect(requestStudioNavigation(window.location.pathname, "/dashboard")).toBe(false);
    expect(buttonByText(rendered.container, "Discard local changes and return")).toBeTruthy();
    await rendered.unmount();
  });

  it("waits for an authoritative refetch instead of opening a cached draft revision", async () => {
    let resolveDraft!: (value: unknown) => void;
    const draftResponse = new Promise(resolve => { resolveDraft = resolve; });
    const rendered = renderPage({
      path: "/workflows/activity-definitions?definition=activity-def-1&section=editor&draft=activity-draft-1",
      seedDraft: fullDraft({ revision: 3, payload: { edit: 0 } }),
      getJson: async (url: string) => {
        if (url === "/capabilities") return capabilities();
        if (url === "/design/activities/drafts/activity-draft-1") return draftResponse;
        throw new Error(`Unexpected GET ${url}`);
      }
    });

    await waitForText(rendered.container, "Loading the exact Activity Definition draft");
    expect(rendered.container.textContent).not.toContain("Saved revision 3");
    resolveDraft(fullDraft({ revision: 4, payload: { edit: 1 } }));
    await waitForText(rendered.container, "Saved revision 4");
    await rendered.unmount();
  });

  it("keeps offline local work mounted across focus and reconnect events", async () => {
    let draftReads = 0;
    const rendered = renderPage({
      path: "/workflows/activity-definitions?definition=activity-def-1&section=editor&draft=activity-draft-1",
      contributions: [editingContribution()],
      putJson: vi.fn(async () => { throw new TypeError("offline"); }),
      getJson: async (url: string) => {
        if (url === "/capabilities") return capabilities();
        if (url === "/design/activities/drafts/activity-draft-1") {
          draftReads += 1;
          return fullDraft({ revision: 3, payload: { edit: 0 } });
        }
        throw new Error(`Unexpected GET ${url}`);
      }
    });

    await waitForText(rendered.container, "Saved revision 3");
    click(buttonByText(rendered.container, "Edit implementation 0"));
    await waitForText(rendered.container, "Offline");
    window.dispatchEvent(new Event("focus"));
    window.dispatchEvent(new Event("online"));
    await new Promise(resolve => window.setTimeout(resolve, 25));

    expect(draftReads).toBe(1);
    expect(rendered.container.textContent).toContain("Edit implementation 1");
    expect(rendered.container.textContent).toContain("Offline");
    await rendered.unmount();
  });
});

describe("Activity Definition local recovery policy", () => {
  it("is disabled by default and expires within the configured identity scope", () => {
    const draft = fullDraft({ revision: 3, payload: { edit: 1 } });
    expect(createActivityDefinitionRecoveryStore(undefined, { tenantId: "tenant-1", subject: "author-1" })).toBeNull();

    vi.useFakeTimers();
    vi.setSystemTime(new Date("2026-07-17T10:00:00Z"));
    const author = createActivityDefinitionRecoveryStore({ enabled: true, ttlMinutes: 1 }, { tenantId: "tenant-1", subject: "author-1" })!;
    const otherAuthor = createActivityDefinitionRecoveryStore({ enabled: true, ttlMinutes: 1 }, { tenantId: "tenant-1", subject: "author-2" })!;
    author.write(draft);
    expect(author.read(draft)?.payload).toEqual({ edit: 1 });
    expect(otherAuthor.read(draft)).toBeNull();

    vi.setSystemTime(new Date("2026-07-17T10:01:01Z"));
    expect(author.read(draft)).toBeNull();
    expect(window.localStorage.length).toBe(0);
  });

  it("clears every saved draft for the logging-out identity without crossing into another identity", () => {
    const authorIdentity = { tenantId: "tenant-1", subject: "author-1" };
    const author = createActivityDefinitionRecoveryStore({ enabled: true }, authorIdentity)!;
    const otherAuthor = createActivityDefinitionRecoveryStore({ enabled: true }, { tenantId: "tenant-1", subject: "author-2" })!;
    author.write(fullDraft({ draftId: "draft-a", payload: { edit: 1 } }));
    author.write(fullDraft({ draftId: "draft-b", payload: { edit: 2 } }));
    otherAuthor.write(fullDraft({ draftId: "draft-c", payload: { edit: 3 } }));

    clearActivityDefinitionRecoveryForIdentity(authorIdentity);

    expect(window.localStorage.length).toBe(1);
    expect(otherAuthor.read(fullDraft({ draftId: "draft-c" }))?.payload).toEqual({ edit: 3 });
  });

  it("does not persist recovery without both tenant and user identity", () => {
    expect(createActivityDefinitionRecoveryStore({ enabled: true }, { subject: "author-1" })).toBeNull();
    expect(createActivityDefinitionRecoveryStore({ enabled: true }, { tenantId: "tenant-1" })).toBeNull();
  });
});

function renderPage(options: {
  path?: string;
  getJson(url: string): Promise<unknown>;
  postJson?(url: string, body: unknown): Promise<unknown>;
  putJson?(url: string, body: unknown): Promise<unknown>;
  contributions?: StudioActivityDefinitionImplementationEditorContribution[];
  runtime?: StudioRuntimeSettings;
  seedDraft?: ReturnType<typeof fullDraft>;
}) {
  window.history.replaceState({}, "", options.path ?? "/workflows/activity-definitions");
  const container = document.createElement("div");
  document.body.appendChild(container);
  const root = createRoot(container);
  const context = {
    baseUrl: `test://activity-definition-authoring-${Math.random()}`,
    http: {
      getJson: options.getJson,
      postJson: options.postJson ?? vi.fn(),
      putJson: options.putJson ?? vi.fn()
    }
  } as unknown as StudioEndpointContext;
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  if (options.seedDraft) queryClient.setQueryData(activityDesignKeys.fullDefinitionDraft(options.seedDraft.draftId), options.seedDraft);
  const contributions = options.contributions ?? [graphContribution()];
  flushSync(() => root.render(
    <QueryClientProvider client={queryClient}>
      <ActivityDefinitionsPage context={context} activityEditors={() => contributions} runtime={options.runtime} />
    </QueryClientProvider>
  ));
  return {
    container,
    async unmount() {
      flushSync(() => root.unmount());
      container.remove();
    }
  };
}

function graphContribution(): StudioActivityDefinitionImplementationEditorContribution {
  return {
    id: "activity-graph",
    providerKey: "elsa.activity-graph",
    providerSchemaVersion: "1",
    createInitialImplementation: () => ({
      payload: {
        rootActivity: { nodeId: "root", activityVersionId: "", inputs: [], outputs: [], structure: null },
        variables: [],
        outputMappings: []
      },
      layout: []
    }),
    component: () => <div>Graph implementation editor</div>
  };
}

function editingContribution(): StudioActivityDefinitionImplementationEditorContribution {
  return {
    ...graphContribution(),
    component: ({ value, onChange }: StudioActivityDefinitionImplementationEditorProps) => {
      const payload = value.payload as { edit?: number };
      return <button type="button" onClick={() => onChange({ ...value, payload: { edit: (payload.edit ?? 0) + 1 } })}>Edit implementation {payload.edit ?? 0}</button>;
    }
  };
}

function capabilities() {
  return { capabilities: [{ id: "elsa.api.activity-design", contractVersion: "1", links: [
    { rel: "activity-definitions", href: "design/activities/definitions" },
    { rel: "activity-authoring-capabilities", href: "design/activities/authoring-capabilities" },
    { rel: "activity-definition", href: "design/activities/definitions/{definitionId}", templated: true },
    { rel: "activity-definition-draft", href: "design/activities/drafts/{draftId}", templated: true },
    { rel: "activity-draft-validation", href: "design/activities/drafts/{draftId}/validate", templated: true },
    { rel: "activity-draft-conflict-copies", href: "design/activities/drafts/{draftId}/conflict-copies", templated: true }
  ] }] };
}

function authoringCapabilities() {
  return {
    contractSchemaVersions: ["1"],
    activityTypeKeyRules: {
      serverGenerated: true,
      allowsPreCreationOverride: true,
      immutable: true,
      prefix: "elsa.user",
      pattern: "^elsa\\.user\\..+$",
      maximumLength: 160,
      collisionScope: "tenantId + activityTypeKey"
    },
    providers: [{
      providerKey: "elsa.activity-graph",
      displayName: "Activity Graph",
      manifestSchemas: [{ schemaVersion: "1", isAuthorable: true, migratableFromSchemaVersions: ["1"] }],
      requiredOutcomes: [{ referenceKey: "done", name: "Done", isEmitted: true, description: null }]
    }],
    types: [],
    storageDriverKeys: [],
    snapshotFingerprint: "sha256:test"
  };
}

function page<T>(items: T[]) {
  return { items, count: items.length, totalCount: items.length, hasMore: false, continuation: null, snapshot: { snapshotId: "snapshot-1", asOf: "2026-07-17T10:00:00Z" } };
}

function definitionIdentity() {
  return {
    definitionId: "activity-def-1",
    activityTypeKey: "elsa.user.invoice-evaluator.activity-def-1",
    tenantId: "tenant-1",
    category: "Finance",
    displayName: "Invoice evaluator",
    description: null,
    contentAuthority: { kind: "design", authorityKey: "elsa.activity-design", sourceId: null },
    forkedFrom: null,
    headVersionId: null,
    recommendedVersionId: null
  };
}

function draftSummary() {
  return {
    draftId: "activity-draft-1",
    definitionId: "activity-def-1",
    revision: 1,
    sourceVersionId: null,
    status: "active",
    providerKey: "elsa.activity-graph",
    providerSchemaVersion: "1",
    updatedAt: "2026-07-17T10:00:00Z",
    presentationLabel: null
  };
}

function fullDraft(overrides: Partial<{ draftId: string; revision: number; payload: unknown }> = {}) {
  return {
    draftId: overrides.draftId ?? "activity-draft-1",
    definitionId: "activity-def-1",
    tenantId: "tenant-1",
    revision: overrides.revision ?? 1,
    sourceVersionId: null,
    status: "active",
    contract: { contractSchemaVersion: "1", inputs: [], outputs: [], outcomes: [{ referenceKey: "done", name: "Done", isEmitted: true, description: null }] },
    provider: {
      providerKey: "elsa.activity-graph",
      schemaVersion: "1",
      manifestFingerprint: "sha256:graph",
      payload: overrides.payload ?? { rootActivity: { nodeId: "root", activityVersionId: "", inputs: [], outputs: [], structure: null }, variables: [], outputMappings: [] }
    },
    layout: [],
    validation: null,
    createdAt: "2026-07-17T10:00:00Z",
    updatedAt: "2026-07-17T10:00:00Z",
    presentationLabel: null
  };
}

function managementDefinition() {
  return {
    definition: definitionIdentity(),
    lifecycle: { draftCount: 1, versionCount: 0, head: null, recommendation: null },
    actions: [],
    updatedAt: "2026-07-17T10:00:00Z"
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

function change(element: HTMLInputElement | HTMLSelectElement, value: string) {
  flushSync(() => {
    const prototype = element instanceof HTMLInputElement ? HTMLInputElement.prototype : HTMLSelectElement.prototype;
    Object.getOwnPropertyDescriptor(prototype, "value")?.set?.call(element, value);
    element.dispatchEvent(new Event("change", { bubbles: true }));
    element.dispatchEvent(new Event("input", { bubbles: true }));
  });
}

function openDetails(element: Element) {
  flushSync(() => {
    (element as HTMLDetailsElement).open = true;
    element.dispatchEvent(new Event("toggle", { bubbles: true }));
  });
}

async function waitForText(container: HTMLElement, text: string) {
  await waitFor(() => {
    if (!container.textContent?.includes(text)) throw new Error(`Waiting for '${text}'.`);
  });
}

async function waitFor(assertion: () => void) {
  await vi.waitFor(assertion, { timeout: 10_000 });
}
