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
  type StudioActivityDiagnosticFocusResult,
  type StudioEndpointContext,
  type StudioRuntimeSettings
} from "@elsa-workflows/studio-sdk";
import { ActivityDefinitionsPage } from "../ActivityDefinitionsPage";
import { clearApiCapabilityCache } from "../api/capabilities";
import { activityDesignKeys } from "../api/activityDesign";
import { clearActivityDefinitionRecoveryForIdentity, createActivityDefinitionRecoveryStore } from "../activityDefinitionRecovery";
import { activityDefinitionsObservationEvent, type ActivityDefinitionsObservation } from "../activityDefinitionObservability";
import type { ActivityDefinitionDraftView, ActivityDefinitionManagementView } from "../activityDefinitionTypes";
import { isExactVersionAtLeast, safeChangeValue } from "../ActivityDefinitionPublicationReview";

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
    await waitFor(() => expect(rendered.container.querySelector<HTMLSelectElement>("select[name='provider']")?.value).toBe("elsa.activity-graph|1"));

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

  it("authors capability-backed members and preserves exact default presence plus unknown wire fields", async () => {
    let revision = 3;
    const initial = fullDraft({ revision, payload: { edit: 0 } });
    initial.contract = {
      contractSchemaVersion: "1",
      futureContractField: { retained: true },
      inputs: [{
        referenceKey: "customer-note",
        name: "Customer note",
        displayName: "Customer note",
        description: null,
        category: null,
        order: 0,
        uiHint: null,
        uiSpecifications: { futureUiField: "retained" },
        type: { alias: "String", collectionKind: "Single", futureTypeField: "retained" },
        isRequired: false,
        isNullable: true,
        default: { syntax: "Literal", value: null, futureDefaultField: "retained" },
        storageDriverKey: "elsa.json",
        durability: "Required",
        futureMemberField: { retained: true }
      }],
      outputs: [],
      outcomes: [{ referenceKey: "done", name: "Done", isEmitted: true, description: null }]
    };
    const putJson = vi.fn(async (_url: string, body: unknown) => {
      const request = body as { expectedRevision: number; contract: typeof initial.contract };
      revision = request.expectedRevision + 1;
      return { ...initial, revision, contract: request.contract };
    });
    const rendered = renderPage({
      path: "/workflows/activity-definitions?definition=activity-def-1&section=editor&draft=activity-draft-1",
      contributions: [editingContribution()],
      putJson,
      getJson: async (url: string) => {
        if (url === "/capabilities") return capabilities();
        if (url === "/design/activities/drafts/activity-draft-1") return initial;
        if (url === "/design/activities/authoring-capabilities") {
          const response = authoringCapabilities();
          response.types[0].compatibleStorageDriverKeys.push("elsa.blob");
          response.storageDriverKeys.push("elsa.blob");
          return response;
        }
        if (url === "/expressions/descriptors") return { items: [{ type: "Python", displayName: "Python", editingMode: "text" }] };
        throw new Error(`Unexpected GET ${url}`);
      }
    });

    await waitForText(rendered.container, "Input 1: Customer note");
    const doneOutcome = [...rendered.container.querySelectorAll("fieldset")].find(fieldset => fieldset.textContent?.includes("Outcome 1: Done"))!;
    expect(doneOutcome.textContent).toContain("Required by the implementation provider");
    expect([...doneOutcome.querySelectorAll("button")].some(button => button.textContent?.includes("Remove outcome"))).toBe(false);
    expect(controlByLabel<HTMLInputElement>(doneOutcome, "Emitted by this activity").disabled).toBe(true);
    expect(controlByLabel<HTMLSelectElement>(rendered.container, "Durable storage driver").value).toBe("elsa.json");
    const required = controlByLabel<HTMLInputElement>(rendered.container, "Required effective value");
    const nullable = controlByLabel<HTMLInputElement>(rendered.container, "Allows null");
    expect(required.checked).toBe(false);
    expect(nullable.checked).toBe(true);
    expect(nullable.disabled).toBe(true);
    expect(rendered.container.textContent).toContain("Remove or change the literal null default before tightening nullability");
    click(required);
    expect(nullable.checked).toBe(true);

    await waitFor(() => expect(putJson).toHaveBeenCalledWith("/design/activities/drafts/activity-draft-1", expect.objectContaining({
      contract: expect.objectContaining({
        inputs: [expect.objectContaining({
          isRequired: true,
          isNullable: true,
          default: { syntax: "Literal", value: null, futureDefaultField: "retained" },
          futureMemberField: { retained: true },
          type: expect.objectContaining({ futureTypeField: "retained" }),
          uiSpecifications: { futureUiField: "retained" }
        })],
        futureContractField: { retained: true }
      })
    })));

    change(controlByLabel<HTMLTextAreaElement>(rendered.container, "Literal JSON value"), "\"present\"");
    click(buttonByText(rendered.container, "Apply default"));
    await waitFor(() => expect(putJson.mock.calls.some(([, body]) =>
      JSON.stringify((body as { contract: { inputs: Array<{ default: unknown }> } }).contract.inputs[0]?.default) === JSON.stringify({ syntax: "Literal", value: "present", futureDefaultField: "retained" })
    )).toBe(true));

    change(controlByLabel<HTMLSelectElement>(rendered.container, "Default"), "none");
    click(buttonByText(rendered.container, "Apply default"));
    await waitFor(() => expect(putJson.mock.calls.some(([, body]) =>
      (body as { contract: { inputs: Array<{ default: unknown }> } }).contract.inputs[0]?.default === null
    )).toBe(true));

    change(controlByLabel<HTMLSelectElement>(rendered.container, "Default"), "literal");
    click(buttonByText(rendered.container, "Apply default"));
    await waitFor(() => expect(putJson.mock.calls.some(([, body]) =>
      JSON.stringify((body as { contract: { inputs: Array<{ default: unknown }> } }).contract.inputs[0]?.default) === JSON.stringify({ syntax: "Literal", value: null })
    )).toBe(true));

    change(controlByLabel<HTMLSelectElement>(rendered.container, "Default"), "expression");
    change(controlByLabel<HTMLTextAreaElement>(rendered.container, "Expression source"), "customer.note");
    click(buttonByText(rendered.container, "Apply default"));
    await waitFor(() => expect(putJson.mock.calls.some(([, body]) => {
      const value = (body as { contract: { inputs: Array<{ default: unknown }> } }).contract.inputs[0]?.default;
      return JSON.stringify(value) === JSON.stringify({ syntax: "Python", value: "customer.note" });
    })).toBe(true));

    change(controlByLabel<HTMLInputElement>(rendered.container, "Member name"), "Result");
    click(buttonByText(rendered.container, "Add output"));
    await waitForText(rendered.container, "Output 1: Result");
    const output = [...rendered.container.querySelectorAll("fieldset")].find(fieldset => fieldset.textContent?.includes("Output 1: Result"))!;
    click(controlByLabel<HTMLInputElement>(output, "Must be produced"));
    click(controlByLabel<HTMLInputElement>(output, "Allows null"));
    await waitFor(() => expect(putJson.mock.calls.some(([, body]) => {
      const value = (body as { contract: { outputs: Array<Record<string, unknown>> } }).contract.outputs[0];
      return JSON.stringify(value) === JSON.stringify({
        referenceKey: "result",
        name: "Result",
        displayName: "Result",
        description: null,
        category: null,
        order: 0,
        uiHint: null,
        uiSpecifications: null,
        type: { alias: "String", collectionKind: "Single" },
        isRequired: true,
        isNullable: true,
        storageDriverKey: "elsa.json",
        durability: "Required"
      });
    })).toBe(true));
    await rendered.unmount();
  }, 15_000);

  it("blocks contract actions and keeps stored content private when authoring capabilities are forbidden", async () => {
    const putJson = vi.fn();
    const rendered = renderPage({
      path: "/workflows/activity-definitions?definition=activity-def-1&section=editor&draft=activity-draft-1",
      putJson,
      getJson: async (url: string) => {
        if (url === "/capabilities") return capabilities();
        if (url === "/design/activities/drafts/activity-draft-1") return fullDraft({ revision: 3 });
        if (url === "/design/activities/authoring-capabilities") throw new StudioHttpError(403, "forbidden", null);
        throw new Error(`Unexpected GET ${url}`);
      }
    });

    await waitForText(rendered.container, "Contract authoring unavailable");
    expect(rendered.container.textContent).toContain("Stored contract members remain visible");
    expect(rendered.container.textContent).toContain("Graph implementation editor");
    expect((buttonByText(rendered.container, "Add input") as HTMLButtonElement).disabled).toBe(true);
    expect([...rendered.container.querySelectorAll("option")].some(option => option.textContent?.includes("System.String"))).toBe(false);
    expect(putJson).not.toHaveBeenCalled();
    await rendered.unmount();
  });

  it("keeps unavailable historical aliases inspectable on an exact immutable version", async () => {
    const rendered = renderPage({
      path: "/workflows/activity-definitions?definition=activity-def-1&section=versions&version=activity-version-1",
      getJson: async (url: string) => {
        if (url === "/capabilities") return capabilities();
        if (url === "/design/activities/definitions/activity-def-1") return managementDefinition();
        if (url.startsWith("/design/activities/definitions/activity-def-1/versions?")) return page([]);
        if (url === "/design/activities/versions/activity-version-1") return {
          definition: definitionIdentity(),
          versionId: "activity-version-1",
          version: "1.0.0",
          lifecycle: "active",
          publishedAt: "2026-07-17T10:00:00Z",
          provider: { providerKey: "elsa.activity-graph", schemaVersion: "1", manifestFingerprint: "sha256:historical" },
          contract: {
            contractSchemaVersion: "1",
            inputs: [{
              referenceKey: "legacy-value",
              name: "Legacy value",
              type: { alias: "Removed.Alias", collectionKind: "Single" },
              isRequired: false,
              isNullable: true,
              default: null,
              storageDriverKey: "historical.driver",
              durability: "Required"
            }],
            outputs: [],
            outcomes: []
          }
        };
        throw new Error(`Unexpected GET ${url}`);
      }
    });

    await waitForText(rendered.container, "Immutable public contract");
    expect(rendered.container.textContent).toContain("Removed.Alias");
    expect(rendered.container.textContent).toContain("Stored aliases remain visible");
    expect(rendered.container.textContent).not.toContain("Replace type");
    await rendered.unmount();
  });

  it("keeps invalid literal text local, announces it, and blocks revision-sensitive actions", async () => {
    const initial = fullDraft({ revision: 3 });
    initial.contract.inputs = [{
      referenceKey: "message",
      name: "Message",
      displayName: "Message",
      type: { alias: "String", collectionKind: "Single" },
      isRequired: false,
      isNullable: true,
      default: { syntax: "Literal", value: null },
      storageDriverKey: "elsa.json",
      durability: "Required"
    }];
    const putJson = vi.fn();
    const rendered = renderPage({
      path: "/workflows/activity-definitions?definition=activity-def-1&section=editor&draft=activity-draft-1",
      putJson,
      getJson: async (url: string) => {
        if (url === "/capabilities") return capabilities();
        if (url === "/design/activities/drafts/activity-draft-1") return initial;
        if (url === "/design/activities/authoring-capabilities") return authoringCapabilities();
        throw new Error(`Unexpected GET ${url}`);
      }
    });

    await waitForText(rendered.container, "Literal JSON value");
    change(controlByLabel<HTMLTextAreaElement>(rendered.container, "Literal JSON value"), "{");
    await waitForText(rendered.container, "The visible text is not saved");
    await waitForText(rendered.container, "Contract correction required");
    expect((buttonByText(rendered.container, "Validate saved revision") as HTMLButtonElement).disabled).toBe(true);
    expect((buttonByText(rendered.container, "Save now") as HTMLButtonElement).disabled).toBe(true);
    expect(buttonByText(rendered.container, "Discard local changes and return")).toBeTruthy();
    expect(putJson).not.toHaveBeenCalled();
    await rendered.unmount();
  });

  it("reviews destructive published-baseline edits before changing only the exact draft", async () => {
    const initial = fullDraft({ revision: 3 });
    initial.sourceVersionId = "activity-version-1";
    initial.contract.inputs = [{
      referenceKey: "message",
      name: "Message",
      displayName: "Message",
      type: { alias: "String", collectionKind: "Single" },
      isRequired: false,
      isNullable: false,
      default: null,
      storageDriverKey: "elsa.json",
      durability: "Required"
    }];
    const putJson = vi.fn(async (_url: string, body: unknown) => ({ ...initial, revision: 4, contract: (body as { contract: unknown }).contract }));
    const rendered = renderPage({
      path: "/workflows/activity-definitions?definition=activity-def-1&section=editor&draft=activity-draft-1",
      putJson,
      getJson: async (url: string) => {
        if (url === "/capabilities") return capabilities();
        if (url === "/design/activities/drafts/activity-draft-1") return initial;
        if (url === "/design/activities/versions/activity-version-1") return { definition: definitionIdentity(), versionId: "activity-version-1", version: "1.0.0", contract: initial.contract, provider: initial.provider, lifecycle: "active", publishedAt: initial.updatedAt };
        if (url === "/design/activities/authoring-capabilities") return authoringCapabilities();
        throw new Error(`Unexpected GET ${url}`);
      }
    });

    await waitForText(rendered.container, "Published baseline");
    click(buttonByText(rendered.container, "Remove input"));
    await waitForText(rendered.container, "Remove published-baseline input");
    expect(putJson).not.toHaveBeenCalled();
    expect(rendered.container.textContent).toContain("Activity Definitions");
    expect(rendered.container.textContent).toContain("None rewritten");
    expect(rendered.container.textContent).toContain("Workflows and occurrences");
    click(buttonByText(rendered.container, "Confirm input removal"));
    await waitFor(() => expect(putJson).toHaveBeenCalledWith("/design/activities/drafts/activity-draft-1", expect.objectContaining({
      expectedRevision: 3,
      contract: expect.objectContaining({ inputs: [] })
    })));
    await rendered.unmount();
  });

  it("guards existing-member changes conservatively when the exact baseline cannot be loaded", async () => {
    const initial = fullDraft({ revision: 3 });
    initial.sourceVersionId = "missing-version";
    initial.contract.inputs = [{
      referenceKey: "message",
      name: "Message",
      displayName: "Message",
      type: { alias: "String", collectionKind: "Single" },
      isRequired: false,
      isNullable: false,
      default: null,
      storageDriverKey: "elsa.json",
      durability: "Required"
    }];
    const putJson = vi.fn();
    const rendered = renderPage({
      path: "/workflows/activity-definitions?definition=activity-def-1&section=editor&draft=activity-draft-1",
      putJson,
      getJson: async (url: string) => {
        if (url === "/capabilities") return capabilities();
        if (url === "/design/activities/drafts/activity-draft-1") return initial;
        if (url === "/design/activities/versions/missing-version") throw new StudioHttpError(404, "missing");
        if (url === "/design/activities/authoring-capabilities") return authoringCapabilities();
        throw new Error(`Unexpected GET ${url}`);
      }
    });

    await waitForText(rendered.container, "Existing-member changes remain conservatively guarded");
    click(buttonByText(rendered.container, "Remove input"));
    await waitForText(rendered.container, "Remove published-baseline input");
    expect(putJson).not.toHaveBeenCalled();
    await rendered.unmount();
  });

  it("autosaves presentation-only copy without impact review but stages a breaking technical name change", async () => {
    let serverDraft = fullDraft({ revision: 3 });
    serverDraft.sourceVersionId = "activity-version-1";
    serverDraft.contract.inputs = [{
      referenceKey: "message",
      name: "Message",
      displayName: "Message",
      type: { alias: "String", collectionKind: "Single" },
      isRequired: false,
      isNullable: false,
      default: null,
      storageDriverKey: "elsa.json",
      durability: "Required"
    }];
    const baselineContract = structuredClone(serverDraft.contract);
    const putJson = vi.fn(async (_url: string, body: unknown) => {
      const request = body as { expectedRevision: number; contract: typeof serverDraft.contract };
      serverDraft = { ...serverDraft, revision: request.expectedRevision + 1, contract: request.contract };
      return serverDraft;
    });
    const rendered = renderPage({
      path: "/workflows/activity-definitions?definition=activity-def-1&section=editor&draft=activity-draft-1",
      putJson,
      getJson: async (url: string) => {
        if (url === "/capabilities") return capabilities();
        if (url === "/design/activities/drafts/activity-draft-1") return serverDraft;
        if (url === "/design/activities/versions/activity-version-1") return { definition: definitionIdentity(), versionId: "activity-version-1", version: "1.0.0", contract: baselineContract, provider: serverDraft.provider, lifecycle: "active", publishedAt: serverDraft.updatedAt };
        if (url === "/design/activities/authoring-capabilities") return authoringCapabilities();
        throw new Error(`Unexpected GET ${url}`);
      }
    });

    await waitForText(rendered.container, "Published baseline");
    openDetails(rendered.container.querySelector(".ad-contract-advanced")!);
    change(controlByLabel<HTMLInputElement>(rendered.container, "Display name"), "Friendly message");
    await waitFor(() => expect(putJson).toHaveBeenCalledTimes(1));
    expect(rendered.container.textContent).not.toContain("Review breaking input change");
    await waitForText(rendered.container, "Saved revision 4");

    change(controlByLabel<HTMLInputElement>(rendered.container, "Name"), "Renamed message");
    click(buttonByText(rendered.container, "Apply name"));
    await waitForText(rendered.container, "Review breaking input change");
    expect(putJson).toHaveBeenCalledTimes(1);
    expect(rendered.container.textContent).toContain("Display name for presentation-only copy");
    click(buttonByText(rendered.container, "Apply input change"));
    await waitFor(() => expect(putJson).toHaveBeenCalledTimes(2));
    expect(putJson.mock.calls[1]?.[1]).toEqual(expect.objectContaining({
      expectedRevision: 4,
      contract: expect.objectContaining({ inputs: [expect.objectContaining({ name: "Renamed message", displayName: "Friendly message" })] })
    }));
    await rendered.unmount();
  }, 12_000);

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
    expect((buttonByText(rendered.container, "Validate saved revision") as HTMLButtonElement).disabled).toBe(false);
    expect(rendered.container.textContent).not.toContain("safe test failure");
    await rendered.unmount();
  });

  it("flushes pending autosave and validates only the exact returned saved revision", async () => {
    const postJson = vi.fn(async (_url: string, body: unknown) => ({ draftId: "activity-draft-1", revision: (body as { expectedRevision: number }).expectedRevision, isValid: true, validatedAt: "2026-07-17T10:01:00Z", diagnostics: [] }));
    const putJson = vi.fn(async (_url: string, body: unknown) => fullDraft({ revision: 4, payload: (body as { provider: { payload: unknown } }).provider.payload }));
    const rendered = renderPage({
      path: "/workflows/activity-definitions?definition=activity-def-1&section=editor&draft=activity-draft-1",
      contributions: [editingContribution()],
      postJson,
      putJson,
      getJson: async (url: string) => {
        if (url === "/capabilities") return capabilities();
        if (url === "/design/activities/drafts/activity-draft-1") return fullDraft({ revision: 3, payload: { edit: 0 } });
        throw new Error(`Unexpected GET ${url}`);
      }
    });

    await waitForText(rendered.container, "Saved revision 3");
    click(buttonByText(rendered.container, "Edit implementation 0"));
    click(buttonByText(rendered.container, "Validate saved revision"));
    await waitFor(() => expect(putJson).toHaveBeenCalledWith("/design/activities/drafts/activity-draft-1", expect.objectContaining({ expectedRevision: 3 })));
    await waitFor(() => expect(postJson).toHaveBeenCalledWith("/design/activities/drafts/activity-draft-1/validate", { expectedRevision: 4 }));
    await waitForText(rendered.container, "Revision 4 passed validation");
    await rendered.unmount();
  });

  it("renders ordered structured diagnostics with safe severity counts and typed unknown-location handling", async () => {
    const postJson = vi.fn(async () => ({
      draftId: "activity-draft-1",
      revision: 3,
      isValid: false,
      validatedAt: "2026-07-17T10:01:00Z",
      diagnostics: [
        {
          code: "activity.contract.input-invalid",
          severity: "Error",
          message: "Every input requires a reference key.",
          subject: { kind: "ActivityDraft", id: "activity-draft-1", revision: 3 },
          location: { jsonPointer: "/contract/inputs", referenceKey: "customer-note" },
          remediation: "Provide a stable reference key.",
          metadata: {}
        },
        {
          code: "activity.future-location",
          severity: "Warning",
          message: "A future provider location needs attention.",
          subject: { kind: "ActivityDraft", id: "hidden-draft-id", revision: 3 },
          location: { providerKey: "future.provider", jsonPointer: "/future/private-location" },
          remediation: "Use a provider editor that supports this location.",
          metadata: {}
        },
        {
          code: "unsafe <private-code>",
          severity: "Info",
          message: "Validation completed with additional context.",
          subject: { kind: "ActivityDraft", id: "activity-draft-1", revision: 3 },
          location: null,
          remediation: null,
          metadata: {}
        }
      ]
    }));
    const draft = fullDraft({ revision: 3 });
    draft.contract.inputs = [{
      referenceKey: "customer-note",
      name: "Customer note",
      displayName: "Customer note",
      description: null,
      category: null,
      order: 0,
      uiHint: null,
      uiSpecifications: null,
      type: { alias: "String", collectionKind: "Single" },
      isRequired: false,
      isNullable: true,
      default: null,
      storageDriverKey: "elsa.json",
      durability: "Required"
    }];
    const rendered = renderPage({
      path: "/workflows/activity-definitions?definition=activity-def-1&section=editor&draft=activity-draft-1",
      postJson,
      getJson: async (url: string) => {
        if (url === "/capabilities") return capabilities();
        if (url === "/design/activities/drafts/activity-draft-1") return draft;
        if (url === "/design/activities/authoring-capabilities") return authoringCapabilities();
        if (url === "/expressions/descriptors") return { items: [] };
        throw new Error(`Unexpected GET ${url}`);
      }
    });

    await waitForText(rendered.container, "Saved revision 3");
    click(buttonByText(rendered.container, "Validate saved revision"));
    await waitForText(rendered.container, "Draft diagnostics");
    expect(rendered.container.textContent).toContain("1 error");
    expect(rendered.container.textContent).toContain("1 warning");
    expect(rendered.container.textContent).toContain("1 info");
    expect(rendered.container.textContent).toContain("Provide a stable reference key.");
    const codes = [...rendered.container.querySelectorAll(".ad-diagnostic-code")].map(item => item.textContent);
    expect(codes).toEqual([
      "activity.contract.input-invalid",
      "activity.future-location",
      "activity.validation.issue"
    ]);
    const contractDiagnostic = rendered.container.querySelector<HTMLButtonElement>("[aria-label='Focus activity.contract.input-invalid']")!;
    click(contractDiagnostic);
    await waitForText(rendered.container, "Focused the exact provider-neutral contract location");
    expect((document.activeElement as HTMLElement)?.hasAttribute("data-contract-reference-key-control")).toBe(true);
    click(buttonByText(rendered.container, "Return to diagnostic"));
    expect(document.activeElement).toBe(contractDiagnostic);
    click(rendered.container.querySelector<HTMLButtonElement>("[aria-label='Focus activity.future-location']")!);
    await waitForText(rendered.container, "The exact diagnostic location is unavailable");
    expect(rendered.container.textContent).not.toContain("hidden-draft-id");
    expect(rendered.container.textContent).not.toContain("future.provider");
    await rendered.unmount();
  });

  it("delegates provider locations through focusDiagnosticLocation and restores keyboard context", async () => {
    const focusDiagnosticLocation = vi.fn(({ editorElement }: { editorElement: HTMLElement }) => {
      editorElement.querySelector<HTMLButtonElement>("[data-provider-focus]")?.focus();
      return { kind: "focused" as const, announcement: "Focused the exact provider control." };
    });
    const contribution: StudioActivityDefinitionImplementationEditorContribution = {
      ...graphContribution(),
      focusDiagnosticLocation,
      component: () => <button type="button" data-provider-focus>Provider target</button>
    };
    const rendered = renderPage({
      path: "/workflows/activity-definitions?definition=activity-def-1&section=editor&draft=activity-draft-1",
      contributions: [contribution],
      postJson: vi.fn(async () => ({
        draftId: "activity-draft-1",
        revision: 3,
        isValid: false,
        validatedAt: "2026-07-17T10:01:00Z",
        diagnostics: [{
          code: "activity.graph.node-invalid",
          severity: "Error",
          message: "The graph node is invalid.",
          subject: { kind: "ActivityDraft", id: "activity-draft-1", revision: 3 },
          location: { providerKey: "elsa.activity-graph", jsonPointer: "/rootActivity/activityVersionId", referenceKey: "done" },
          remediation: "Choose an exact activity version.",
          metadata: {}
        }]
      })),
      getJson: async (url: string) => {
        if (url === "/capabilities") return capabilities();
        if (url === "/design/activities/drafts/activity-draft-1") return fullDraft({ revision: 3 });
        throw new Error(`Unexpected GET ${url}`);
      }
    });

    await waitForText(rendered.container, "Saved revision 3");
    click(buttonByText(rendered.container, "Validate saved revision"));
    await waitForText(rendered.container, "activity.graph.node-invalid");
    const diagnostic = rendered.container.querySelector<HTMLButtonElement>("[aria-label='Focus activity.graph.node-invalid']")!;
    click(diagnostic);
    await waitFor(() => expect((document.activeElement as HTMLElement)?.hasAttribute("data-provider-focus")).toBe(true));
    expect(focusDiagnosticLocation).toHaveBeenCalledWith(expect.objectContaining({
      location: expect.objectContaining({ jsonPointer: "/rootActivity/activityVersionId", referenceKey: "done" }),
      subject: expect.objectContaining({ kind: "ActivityDraft" })
    }));
    await waitForText(rendered.container, "Return to diagnostic");
    click(buttonByText(rendered.container, "Return to diagnostic"));
    expect(document.activeElement).toBe(diagnostic);
    focusDiagnosticLocation.mockRejectedValueOnce(new Error("hidden provider focus failure"));
    click(diagnostic);
    await waitForText(rendered.container, "exact diagnostic location is unavailable");
    expect(rendered.container.textContent).not.toContain("hidden provider focus failure");
    expect(rendered.container.textContent).toContain("activity.graph.node-invalid");
    await rendered.unmount();
  });

  it("keeps the latest diagnostic focus context when an older provider request completes late", async () => {
    let resolveFirst!: (result: StudioActivityDiagnosticFocusResult) => void;
    let resolveSecond!: (result: StudioActivityDiagnosticFocusResult) => void;
    const focusDiagnosticLocation = vi.fn()
      .mockImplementationOnce(() => new Promise<StudioActivityDiagnosticFocusResult>(resolve => { resolveFirst = resolve; }))
      .mockImplementationOnce(() => new Promise<StudioActivityDiagnosticFocusResult>(resolve => { resolveSecond = resolve; }));
    const contribution: StudioActivityDefinitionImplementationEditorContribution = {
      ...graphContribution(),
      focusDiagnosticLocation,
      component: () => <button type="button" data-provider-focus>Provider target</button>
    };
    const rendered = renderPage({
      path: "/workflows/activity-definitions?definition=activity-def-1&section=editor&draft=activity-draft-1",
      contributions: [contribution],
      postJson: vi.fn(async () => ({
        draftId: "activity-draft-1",
        revision: 3,
        isValid: false,
        validatedAt: "2026-07-17T10:01:00Z",
        diagnostics: ["first", "second"].map(referenceKey => ({
          code: `activity.graph.${referenceKey}`,
          severity: "Error" as const,
          message: `Correct the ${referenceKey} graph location.`,
          subject: { kind: "ActivityDraft", id: "activity-draft-1", revision: 3 },
          location: { providerKey: "elsa.activity-graph", jsonPointer: `/rootActivity/${referenceKey}`, referenceKey },
          remediation: "Use the provider editor.",
          metadata: {}
        }))
      })),
      getJson: async (url: string) => {
        if (url === "/capabilities") return capabilities();
        if (url === "/design/activities/drafts/activity-draft-1") return fullDraft({ revision: 3 });
        throw new Error(`Unexpected GET ${url}`);
      }
    });

    await waitForText(rendered.container, "Saved revision 3");
    click(buttonByText(rendered.container, "Validate saved revision"));
    await waitForText(rendered.container, "activity.graph.second");
    const first = rendered.container.querySelector<HTMLButtonElement>("[aria-label='Focus activity.graph.first']")!;
    const second = rendered.container.querySelector<HTMLButtonElement>("[aria-label='Focus activity.graph.second']")!;
    click(first);
    await waitFor(() => expect(focusDiagnosticLocation).toHaveBeenCalledTimes(1));
    click(second);
    await waitFor(() => expect(focusDiagnosticLocation).toHaveBeenCalledTimes(2));

    rendered.container.querySelector<HTMLButtonElement>("[data-provider-focus]")?.focus();
    resolveSecond({ kind: "focused", announcement: "Focused the second provider location." });
    await waitForText(rendered.container, "Focused the second provider location.");
    resolveFirst({ kind: "unsupported", announcement: "Stale first request must be ignored." });
    await new Promise(resolve => window.setTimeout(resolve, 0));

    expect(rendered.container.textContent).toContain("Focused the second provider location.");
    expect(rendered.container.textContent).not.toContain("Stale first request must be ignored.");
    click(buttonByText(rendered.container, "Return to diagnostic"));
    expect(document.activeElement).toBe(second);
    await rendered.unmount();
  });

  it("maps typed contract JSON pointers to exact accessible controls before using member fallback", async () => {
    const draft = fullDraft({ revision: 3 });
    draft.contract.inputs = [{
      referenceKey: "customer-note",
      name: "Customer note",
      displayName: "Customer note",
      description: null,
      category: null,
      order: 0,
      uiHint: null,
      uiSpecifications: null,
      type: { alias: "String", collectionKind: "Single" },
      isRequired: false,
      isNullable: true,
      default: { syntax: "Literal", value: "hello" },
      storageDriverKey: "elsa.json",
      durability: "Required"
    }];
    const pointers = [
      ["activity.contract.type", "/contract/inputs/0/type/alias", "type.alias"],
      ["activity.contract.storage", "/contract/inputs/0/storageDriverKey", "storageDriverKey"],
      ["activity.contract.default", "/contract/inputs/0/default/value", "default.value"],
      ["activity.contract.reference", "/contract/inputs/0/referenceKey", "referenceKey"]
    ] as const;
    const rendered = renderPage({
      path: "/workflows/activity-definitions?definition=activity-def-1&section=editor&draft=activity-draft-1",
      postJson: vi.fn(async () => ({
        draftId: "activity-draft-1",
        revision: 3,
        isValid: false,
        validatedAt: "2026-07-17T10:01:00Z",
        diagnostics: [
          ...pointers.map(([code, jsonPointer]) => ({
            code,
            severity: "Error" as const,
            message: "Correct this exact contract field.",
            subject: { kind: "ActivityDraft", id: "activity-draft-1", revision: 3 },
            location: { jsonPointer, referenceKey: "customer-note" },
            remediation: "Use the focused control.",
            metadata: {}
          })),
          {
            code: "activity.contract.emitted",
            severity: "Error",
            message: "The required outcome must remain emitted.",
            subject: { kind: "ActivityDraft", id: "activity-draft-1", revision: 3 },
            location: { jsonPointer: "/contract/outcomes/0/isEmitted", referenceKey: "done" },
            remediation: "Retain the provider-required outcome.",
            metadata: {}
          }
        ]
      })),
      getJson: async (url: string) => {
        if (url === "/capabilities") return capabilities();
        if (url === "/design/activities/drafts/activity-draft-1") return draft;
        if (url === "/design/activities/authoring-capabilities") return authoringCapabilities();
        if (url === "/expressions/descriptors") return { items: [] };
        throw new Error(`Unexpected GET ${url}`);
      }
    });

    await waitForText(rendered.container, "Saved revision 3");
    click(buttonByText(rendered.container, "Validate saved revision"));
    await waitForText(rendered.container, "activity.contract.reference");
    for (const [code, , expectedField] of pointers) {
      click(rendered.container.querySelector<HTMLButtonElement>(`[aria-label='Focus ${code}']`)!);
      await waitFor(() => expect((document.activeElement as HTMLElement)?.dataset.contractField).toBe(expectedField));
      click(buttonByText(rendered.container, "Return to diagnostic"));
    }
    click(rendered.container.querySelector<HTMLButtonElement>("[aria-label='Focus activity.contract.emitted']")!);
    await waitFor(() => {
      const focused = document.activeElement as HTMLElement;
      expect(focused.tagName).toBe("LABEL");
      expect(focused.querySelector<HTMLInputElement>("[data-contract-field='isEmitted']")?.disabled).toBe(true);
    });
    expect(rendered.container.textContent).toContain("exact accessible context for a disabled");
    await rendered.unmount();
  });

  it.each([
    [403, "Draft validation is not authorized"],
    [404, "exact authorized draft could not be confirmed"],
    [503, "Draft validation is unavailable"]
  ])("keeps a %s validation failure privacy-safe", async (status, expected) => {
    const rendered = renderPage({
      path: "/workflows/activity-definitions?definition=activity-def-1&section=editor&draft=activity-draft-1",
      postJson: vi.fn(async () => { throw new StudioHttpError(status, "hidden provider/resource identity", null); }),
      getJson: async (url: string) => {
        if (url === "/capabilities") return capabilities();
        if (url === "/design/activities/drafts/activity-draft-1") return fullDraft({ revision: 3 });
        throw new Error(`Unexpected GET ${url}`);
      }
    });

    await waitForText(rendered.container, "Saved revision 3");
    click(buttonByText(rendered.container, "Validate saved revision"));
    await waitForText(rendered.container, expected);
    expect(rendered.container.textContent).not.toContain("hidden provider/resource identity");
    await rendered.unmount();
  });

  it("distinguishes a missing validation capability from a transport failure", async () => {
    const unavailableCapabilities = capabilities();
    unavailableCapabilities.capabilities[0]!.links = unavailableCapabilities.capabilities[0]!.links
      .filter(link => link.rel !== "activity-draft-validation");
    const unavailable = renderPage({
      path: "/workflows/activity-definitions?definition=activity-def-1&section=editor&draft=activity-draft-1",
      getJson: async (url: string) => {
        if (url === "/capabilities") return unavailableCapabilities;
        if (url === "/design/activities/drafts/activity-draft-1") return fullDraft({ revision: 3 });
        throw new Error(`Unexpected GET ${url}`);
      }
    });
    await waitForText(unavailable.container, "Saved revision 3");
    click(buttonByText(unavailable.container, "Validate saved revision"));
    await waitForText(unavailable.container, "advertised capability could not be confirmed");
    await unavailable.unmount();

    const transport = renderPage({
      path: "/workflows/activity-definitions?definition=activity-def-1&section=editor&draft=activity-draft-1",
      postJson: vi.fn(async () => { throw new TypeError("hidden network detail"); }),
      getJson: async (url: string) => {
        if (url === "/capabilities") return capabilities();
        if (url === "/design/activities/drafts/activity-draft-1") return fullDraft({ revision: 3 });
        throw new Error(`Unexpected GET ${url}`);
      }
    });
    await waitForText(transport.container, "Saved revision 3");
    click(buttonByText(transport.container, "Validate saved revision"));
    await waitForText(transport.container, "could not reach the server");
    expect(transport.container.textContent).toContain("was not classified as invalid");
    expect(transport.container.textContent).not.toContain("hidden network detail");
    await transport.unmount();
  });

  it("distinguishes exact-revision validation rejection from transport and Runtime rejection", async () => {
    const rendered = renderPage({
      path: "/workflows/activity-definitions?definition=activity-def-1&section=editor&draft=activity-draft-1",
      postJson: vi.fn(async () => {
        throw new StudioHttpError(409, "stale", null, {
          errorCode: "activity.draft.stale-revision",
          recovery: { currentRevision: 4 }
        });
      }),
      getJson: async (url: string) => {
        if (url === "/capabilities") return capabilities();
        if (url === "/design/activities/drafts/activity-draft-1") return fullDraft({ revision: 3 });
        throw new Error(`Unexpected GET ${url}`);
      }
    });

    await waitForText(rendered.container, "Saved revision 3");
    click(buttonByText(rendered.container, "Validate saved revision"));
    await waitForText(rendered.container, "backend rejected validation");
    expect(rendered.container.textContent).toContain("server draft advanced to revision 4");
    expect(rendered.container.textContent).toContain("Runtime rejection is reported by the Test Run experience");
    expect(rendered.container.textContent).not.toContain("could not reach the server");
    await rendered.unmount();
  });

  it("ignores a late validation response after the author advances the draft", async () => {
    let rejectValidation!: (reason: unknown) => void;
    const validation = new Promise((_resolve, reject) => { rejectValidation = reject; });
    const postJson = vi.fn(async () => validation);
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
    await waitFor(() => expect(postJson).toHaveBeenCalledTimes(1));
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

describe("Activity Definition publication", () => {
  it("flushes autosave, binds preflight to the exact revision and head, and publishes a higher exact version once", async () => {
    const putJson = vi.fn(async (_url: string, body: unknown) => fullDraft({
      revision: 4,
      payload: (body as { provider: { payload: unknown } }).provider.payload
    }));
    const postJson = vi.fn(async (url: string, body: unknown) => {
      if (url.endsWith("/publication-preflight")) return publicationPreflight({
        draftRevision: (body as { expectedDraftRevision: number }).expectedDraftRevision,
        definitionHeadVersionId: "version-head",
        hasBaseline: true,
        minimumVersion: "2.0.0"
      });
      if (url.endsWith("/publish")) {
        const request = body as { version: string; idempotencyKey: string; expectedDraftRevision: number; expectedDefinitionHeadVersionId?: string | null; reviewToken: string };
        return publicationReceipt({
          idempotencyKey: request.idempotencyKey,
          expectedDraftRevision: request.expectedDraftRevision,
          expectedDefinitionHeadVersionId: request.expectedDefinitionHeadVersionId ?? null,
          reviewToken: request.reviewToken,
          requestedVersion: request.version,
          outcome: {
            ...publicationReceipt().outcome!,
            version: request.version
          }
        });
      }
      throw new Error(`Unexpected POST ${url}`);
    });
    const detail: ActivityDefinitionManagementView = managementDefinition();
    detail.definition.headVersionId = "version-head";
    detail.definition.recommendedVersionId = "version-recommended";
    detail.lifecycle.head = { versionId: "version-head", version: "1.4.0", lifecycle: "Active", providerKey: "elsa.activity-graph", providerSchemaVersion: "1" };
    const rendered = renderPage({
      path: "/workflows/activity-definitions?definition=activity-def-1&section=editor&draft=activity-draft-1",
      contributions: [editingContribution()],
      putJson,
      postJson,
      getJson: async (url: string) => {
        if (url === "/capabilities") return capabilities();
        if (url === "/design/activities/drafts/activity-draft-1") return fullDraft({ revision: 3, payload: { edit: 0 } });
        if (url === "/design/activities/definitions/activity-def-1") return detail;
        throw new Error(`Unexpected GET ${url}`);
      }
    });

    await waitForText(rendered.container, "Saved revision 3");
    click(buttonByText(rendered.container, "Edit implementation 0"));
    click(buttonByText(rendered.container, "Prepare publication"));
    await waitFor(() => expect(postJson).toHaveBeenCalledWith(
      "/design/activities/drafts/activity-draft-1/publication-preflight",
      { expectedDraftRevision: 4, expectedDefinitionHeadVersionId: "version-head" }
    ));
    await waitForText(rendered.container, "Compared with published head");
    expect((controlByLabel<HTMLInputElement>(rendered.container, "Publication version")).value).toBe("2.0.0");
    change(controlByLabel<HTMLInputElement>(rendered.container, "Publication version"), "3.2.1");
    click(buttonByText(rendered.container, "Publish 3.2.1"));
    await waitForText(rendered.container, "Published immutable version 3.2.1");
    expect(rendered.container.textContent).toContain("existing recommended version was not moved");
    expect(postJson.mock.calls.filter(([url]) => String(url).endsWith("/publish"))).toHaveLength(1);
    expect(postJson).toHaveBeenCalledWith("/design/activities/drafts/activity-draft-1/publish", expect.objectContaining({
      expectedDraftRevision: 4,
      expectedDefinitionHeadVersionId: "version-head",
      version: "3.2.1",
      reviewToken: "sha256:review"
    }));
    await rendered.unmount();
  });

  it("renders first-publication baseline, impact ordering, redaction, and blocking readiness honestly", async () => {
    const rendered = renderPage({
      path: "/workflows/activity-definitions?definition=activity-def-1&section=editor&draft=activity-draft-1",
      postJson: vi.fn(async (url: string) => {
        if (url.endsWith("/publication-preflight")) return publicationPreflight({
          impactFirstChanges: [
            publicationChange({ changeId: "secret", impact: "Breaking", area: "Provider", kind: "provider.secret-rotated", before: { redacted: true }, after: { kind: "Redacted" } }),
            publicationChange({ changeId: "future", impact: "ProviderFutureImpact", area: "FutureArea", kind: "<future-kind>", message: "A provider-specific change was reported." })
          ],
          provider: { kind: "Provider", key: "elsa.activity-graph", schemaVersion: "1", status: "Unavailable", supportedSchemaVersions: [] },
          isPublishable: false,
          diagnostics: [{
            code: "activity.runtime.consumer-missing",
            severity: "Error",
            message: "Required Runtime consumer is missing.",
            subject: { kind: "ActivityDraft", id: "activity-draft-1", revision: 3 },
            location: null,
            remediation: "Install the required Runtime consumer.",
            metadata: {}
          }]
        });
        throw new Error(`Unexpected POST ${url}`);
      }),
      getJson: async (url: string) => {
        if (url === "/capabilities") return capabilities();
        if (url === "/design/activities/drafts/activity-draft-1") return fullDraft({ revision: 3 });
        if (url === "/design/activities/definitions/activity-def-1") return managementDefinition();
        throw new Error(`Unexpected GET ${url}`);
      }
    });

    await waitForText(rendered.container, "Saved revision 3");
    click(buttonByText(rendered.container, "Prepare publication"));
    await waitForText(rendered.container, "First publication · no published baseline exists");
    expect(rendered.container.textContent).toContain("Protected value redacted");
    expect(rendered.container.textContent).not.toContain("raw-secret");
    expect(rendered.container.textContent).toContain("Provider-specific change");
    expect(rendered.container.textContent).toContain("Other impact · ProviderFutureImpact");
    expect(rendered.container.textContent).toContain("Publication blocked");
    expect((buttonByText(rendered.container, "Publish 1.0.0") as HTMLButtonElement).disabled).toBe(true);
    await rendered.unmount();
  });

  it("discards a stale review without retrying publication writes", async () => {
    let publishCalls = 0;
    const rendered = renderPage({
      path: "/workflows/activity-definitions?definition=activity-def-1&section=editor&draft=activity-draft-1",
      postJson: vi.fn(async (url: string) => {
        if (url.endsWith("/publication-preflight")) return publicationPreflight();
        if (url.endsWith("/publish")) {
          publishCalls += 1;
          throw new StudioHttpError(409, "hidden stale detail", null, { errorCode: "activity.publication.review-stale" });
        }
        throw new Error(`Unexpected POST ${url}`);
      }),
      getJson: async (url: string) => {
        if (url === "/capabilities") return capabilities();
        if (url === "/design/activities/drafts/activity-draft-1") return fullDraft({ revision: 3 });
        if (url === "/design/activities/definitions/activity-def-1") return managementDefinition();
        throw new Error(`Unexpected GET ${url}`);
      }
    });

    await waitForText(rendered.container, "Saved revision 3");
    click(buttonByText(rendered.container, "Prepare publication"));
    await waitForText(rendered.container, "Minimum valid version: 1.0.0");
    click(buttonByText(rendered.container, "Publish 1.0.0"));
    await waitForText(rendered.container, "reopened preflight without publishing");
    expect(publishCalls).toBe(1);
    expect(rendered.container.textContent).not.toContain("hidden stale detail");
    expect(rendered.container.textContent).not.toContain("Published immutable version");
    await rendered.unmount();
  });

  it("reconciles an ambiguous publish response through the authoritative receipt without resubmitting", async () => {
    let publishCalls = 0;
    let receiptCalls = 0;
    let publishRequest: { version: string; idempotencyKey: string; expectedDraftRevision: number; expectedDefinitionHeadVersionId?: string | null; reviewToken: string } | null = null;
    const rendered = renderPage({
      path: "/workflows/activity-definitions?definition=activity-def-1&section=editor&draft=activity-draft-1",
      postJson: vi.fn(async (url: string, body: unknown) => {
        if (url.endsWith("/publication-preflight")) return publicationPreflight();
        if (url.endsWith("/publish")) {
          publishCalls += 1;
          publishRequest = body as typeof publishRequest;
          throw new TypeError("ambiguous transport outcome");
        }
        throw new Error(`Unexpected POST ${url}`);
      }),
      getJson: async (url: string) => {
        if (url === "/capabilities") return capabilities();
        if (url === "/design/activities/drafts/activity-draft-1") return fullDraft({ revision: 3 });
        if (url === "/design/activities/definitions/activity-def-1") return managementDefinition();
        if (url.startsWith("/design/activities/publications/")) {
          receiptCalls += 1;
          return publicationReceipt({
            idempotencyKey: publishRequest!.idempotencyKey,
            expectedDraftRevision: publishRequest!.expectedDraftRevision,
            expectedDefinitionHeadVersionId: publishRequest!.expectedDefinitionHeadVersionId ?? null,
            reviewToken: publishRequest!.reviewToken,
            requestedVersion: publishRequest!.version
          });
        }
        throw new Error(`Unexpected GET ${url}`);
      }
    });

    await waitForText(rendered.container, "Saved revision 3");
    click(buttonByText(rendered.container, "Prepare publication"));
    await waitForText(rendered.container, "Minimum valid version: 1.0.0");
    click(buttonByText(rendered.container, "Publish 1.0.0"));
    await waitForText(rendered.container, "Published immutable version 1.0.0");
    expect(publishCalls).toBe(1);
    expect(receiptCalls).toBe(1);
    await rendered.unmount();
  });

  it("validates exact semantic-version precedence and keeps protected values opaque", () => {
    expect(isExactVersionAtLeast("2.0.0", "2.0.0")).toBe(true);
    expect(isExactVersionAtLeast("7.3.2+build.4", "2.0.0")).toBe(true);
    expect(isExactVersionAtLeast("2.0.0-rc.1", "2.0.0")).toBe(false);
    expect(isExactVersionAtLeast("02.0.0", "2.0.0")).toBe(false);
    expect(safeChangeValue(publicationChange({ kind: "provider.credential-changed" }), { token: "raw" })).toBe("Protected value withheld");
    expect(safeChangeValue(publicationChange(), { nested: "opaque" })).toBe("Structured value changed");
    expect(safeChangeValue(publicationChange(), { redacted: true, nested: "must-not-render" })).toBe("Protected value redacted");
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
    { rel: "activity-definition-versions", href: "design/activities/definitions/{definitionId}/versions", templated: true },
    { rel: "activity-definition-draft", href: "design/activities/drafts/{draftId}", templated: true },
    { rel: "activity-definition-version", href: "design/activities/versions/{versionId}", templated: true },
    { rel: "activity-draft-validation", href: "design/activities/drafts/{draftId}/validate", templated: true },
    { rel: "activity-draft-conflict-copies", href: "design/activities/drafts/{draftId}/conflict-copies", templated: true }
  ] }, {
    id: "elsa.api.expressions",
    contractVersion: "1",
    links: [{ rel: "expression-descriptors", href: "expressions/descriptors" }]
  }, {
    id: "elsa.api.publishing",
    contractVersion: "1",
    links: [
      { rel: "activity-publication-preflight", href: "design/activities/drafts/{draftId}/publication-preflight", templated: true },
      { rel: "activity-publication", href: "design/activities/drafts/{draftId}/publish", templated: true },
      { rel: "activity-publication-receipt", href: "design/activities/publications/{idempotencyKey}", templated: true }
    ]
  }] };
}

function publicationChange(overrides: Partial<{
  changeId: string;
  area: string;
  kind: string;
  before: unknown;
  after: unknown;
  impact: string;
  message: string;
}> = {}) {
  return {
    changeId: overrides.changeId ?? "change-1",
    area: overrides.area ?? "Contract",
    kind: overrides.kind ?? "input-added",
    subject: { memberKind: "Input", referenceKey: "customer-id", dependencyVersionId: null, occurrenceId: null },
    before: overrides.before,
    after: overrides.after,
    impact: overrides.impact ?? "Additive",
    requiredBump: overrides.impact === "Breaking" ? "Major" : "Minor",
    message: overrides.message ?? "Input customer-id was added."
  };
}

function publicationPreflight(overrides: Record<string, unknown> = {}) {
  return {
    draftId: "activity-draft-1",
    draftRevision: 3,
    definitionId: "activity-def-1",
    definitionHeadVersionId: null,
    hasBaseline: false,
    reviewToken: "sha256:review",
    isPublishable: true,
    minimumVersion: "1.0.0",
    validVersions: ["1.0.0"],
    diff: { compatibility: "Compatible", requiredBump: "Minor", behaviorChanged: true, summary: { breaking: 0, additive: 1, nonBehavioral: 0, warnings: 0 } },
    impactFirstChanges: [publicationChange()],
    dependencies: [],
    provider: { kind: "Provider", key: "elsa.activity-graph", schemaVersion: "1", status: "Available", supportedSchemaVersions: ["1"] },
    storage: [],
    runtime: [{ kind: "Runtime", key: "elsa.activity-graph", schemaVersion: "1", status: "Available", supportedSchemaVersions: ["1"] }],
    diagnostics: [],
    ...overrides
  };
}

function publicationReceipt(overrides: Record<string, unknown> = {}) {
  return {
    idempotencyKey: "operation-1",
    status: "Applied",
    draftId: "activity-draft-1",
    expectedDraftRevision: 3,
    expectedDefinitionHeadVersionId: null,
    reviewToken: "sha256:review",
    requestedVersion: "1.0.0",
    outcome: {
      definitionId: "activity-def-1",
      definitionVersionId: "version-published",
      draftId: "activity-draft-1",
      version: "1.0.0",
      templateId: "template-1",
      templateHash: "sha256:template",
      sourceReferenceId: "source-1",
      publishedAt: "2026-07-17T11:00:00Z"
    },
    errorCode: null,
    diagnostics: [],
    updatedAt: "2026-07-17T11:00:00Z",
    ...overrides
  };
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
    types: [{
      alias: "String",
      displayName: "Text",
      category: "Primitives",
      defaultEditor: "text",
      supportedCollectionKinds: ["Single"],
      supportsNull: true,
      supportsDurability: true,
      compatibleStorageDriverKeys: ["elsa.json"]
    }],
    storageDriverKeys: ["elsa.json"],
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

function fullDraft(overrides: Partial<{ draftId: string; revision: number; payload: unknown }> = {}): ActivityDefinitionDraftView {
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

function change(element: HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement, value: string) {
  flushSync(() => {
    const prototype = element instanceof HTMLInputElement ? HTMLInputElement.prototype : element instanceof HTMLSelectElement ? HTMLSelectElement.prototype : HTMLTextAreaElement.prototype;
    Object.getOwnPropertyDescriptor(prototype, "value")?.set?.call(element, value);
    element.dispatchEvent(new Event("change", { bubbles: true }));
    element.dispatchEvent(new Event("input", { bubbles: true }));
  });
}

function controlByLabel<T extends HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement>(container: HTMLElement, text: string) {
  const label = [...container.querySelectorAll("label")].find(candidate =>
    [...candidate.querySelectorAll(":scope > span")].some(span => span.textContent?.trim() === text)
  );
  const control = label?.querySelector("input, select, textarea");
  if (!control) throw new Error(`Control labelled '${text}' not found.`);
  return control as T;
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
