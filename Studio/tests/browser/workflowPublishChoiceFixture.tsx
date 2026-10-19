import React, { useCallback, useMemo, useState } from "react";
import { createHttpClient, type StudioEndpointContext } from "@elsa-workflows/studio-sdk";
import { updateDraft } from "../../src/essentials/Elsa.Studio.Workflows/Client/src/api/workflowDesign";
import { PublicationReviewDialog } from "../../src/essentials/Elsa.Studio.Workflows/Client/src/workflow-editor/PublicationReviewDialog";
import { WorkflowEditorToolbar } from "../../src/essentials/Elsa.Studio.Workflows/Client/src/workflow-editor/WorkflowEditorToolbar";
import type { WorkflowEditorOperation, WorkflowErrorInput, WorkflowTestRunState } from "../../src/essentials/Elsa.Studio.Workflows/Client/src/workflow-editor/editorTypes";
import { useWorkflowOperations } from "../../src/essentials/Elsa.Studio.Workflows/Client/src/workflow-editor/useWorkflowOperations";
import type { WorkflowDefinitionDetails, WorkflowDraft } from "../../src/essentials/Elsa.Studio.Workflows/Client/src/workflowTypes";

const workflowDraft: WorkflowDraft = {
  id: "draft-browser",
  definitionId: "definition-browser",
  sourceVersionId: "version-1",
  state: {
    rootActivity: {
      nodeId: "root",
      activityVersionId: "workflow-root",
      inputs: [],
      outputs: []
    }
  },
  layout: [],
  validationErrors: []
};

const workflowDetails: WorkflowDefinitionDetails = {
  definition: {
    id: workflowDraft.definitionId,
    name: "Browser publication workflow",
    createdAt: "2026-09-01T00:00:00Z",
    lastModifiedAt: "2026-09-01T00:00:00Z",
    latestVersion: "1.0.0",
    versionCount: 1
  },
  draft: workflowDraft,
  versions: [{ id: "version-1", version: "1.0.0", createdAt: "2026-09-01T00:00:00Z" }]
};

/** Exercises publication mode selection through the real API helpers, operations hook, and review dialog. */
export function WorkflowPublishChoiceFixture() {
  const [operation, setOperation] = useState<WorkflowEditorOperation>("idle");
  const [status, setStatus] = useState("");
  const [error, setError] = useState<WorkflowErrorInput>("");
  const [autosavePaused, setAutosavePaused] = useState(false);
  const [autosaveEnabled, setAutosaveEnabled] = useState(true);
  const [publishedArtifact, setPublishedArtifact] = useState<string | null>(null);
  const [apiCalls, setApiCalls] = useState<ApiCall[]>([]);
  const busy = operation !== "idle";
  const outcome = new URLSearchParams(window.location.search).get("outcome") ?? "success";
  const context = useMemo(() => createPublicationContext(window.location.origin, outcome, call => {
    setApiCalls(calls => [...calls, call]);
  }), [outcome]);

  const saveDraft = useCallback(async (snapshot: WorkflowDraft, savedStatus: string) => {
    const saved = await updateDraft(context, snapshot);
    setStatus(savedStatus);
    return saved;
  }, [context]);
  const clearError = useCallback((value: WorkflowErrorInput) => setError(value), []);
  const operations = useWorkflowOperations({
    context,
    draft: workflowDraft,
    details: workflowDetails,
    catalog: [],
    busy,
    saveDraft,
    flushPendingSave: async () => undefined,
    reload: async () => undefined,
    startTestRun: (_testRun: WorkflowTestRunState) => undefined,
    setPublishedArtifact,
    setOperation,
    setStatus,
    setError: clearError,
    setActiveRightPanelId: () => undefined,
    setInspectorCollapsed: () => undefined,
    setAutosavePaused
  });

  const errorMessage = typeof error === "string" ? error : error.message;

  return (
    <main className="wf-editor browser-toolbar-workbench">
      <WorkflowEditorToolbar
        name={workflowDetails.definition.name}
        status={status}
        saving={operation === "saving"}
        busy={busy}
        autosaveEnabled={autosaveEnabled}
        onAutosaveChange={setAutosaveEnabled}
        canUndo={false}
        canRedo={false}
        canAutoLayout={false}
        onUndo={() => undefined}
        onRedo={() => undefined}
        onAutoLayout={() => undefined}
        onBack={() => undefined}
        onSave={() => void operations.save()}
        onPublish={mode => void operations.preparePublication(mode)}
        onRun={() => operations.run()}
        canRun
        runTitle="Run a transient test of the current design"
        moreActions={[]}
      />
      <section className="browser-toolbar-controls" aria-label="Publication fixture state">
        {errorMessage ? <p role="alert">{errorMessage}</p> : null}
        <p>Autosave {autosavePaused ? "paused" : "enabled"}</p>
        {publishedArtifact ? <p>Published artifact: {publishedArtifact}</p> : null}
        <details>
          <summary>Fixture API request log</summary>
          <output data-testid="workflow-publish-api-log">{apiCalls.map(call => `${call.method} ${call.path}`).join("\n")}</output>
        </details>
      </section>
      {operations.publicationReview ? (
        <PublicationReviewDialog
          review={operations.publicationReview}
          busy={busy}
          onReview={operations.reviewPublication}
          onPublish={operations.confirmPublication}
          onCancel={operations.cancelPublication}
          onOpenPublishedExecutable={operations.openPublishedExecutable}
        />
      ) : null}
    </main>
  );
}

interface ApiCall {
  method: string;
  path: string;
}

function createPublicationContext(baseUrl: string, outcome: string, recordCall: (call: ApiCall) => void): StudioEndpointContext {
  let publishAttempts = 0;
  const http = createHttpClient(baseUrl, {
    fetch: async (input, init) => {
      const requestUrl = input instanceof Request ? input.url : String(input);
      const path = new URL(requestUrl, baseUrl).pathname;
      const method = (init?.method ?? "GET").toUpperCase();
      const requestBody = typeof init?.body === "string" ? JSON.parse(init.body) as Record<string, unknown> : {};
      recordCall({ method, path });

      if (method === "GET" && path === "/capabilities") return response(capabilities());
      if (method === "GET" && path === "/publishing/workflows/definition-browser/policy") {
        if (outcome === "preflight-failure") return response({ title: "Policy unavailable", status: 503, detail: "policy unavailable" }, 503);
        return response({ defaultAction: "replace", defaultSlotName: "default", source: "host" });
      }
      if (method === "GET" && path === "/runtime/workflows/activation-slots/definition-browser") {
        return response({ items: [{
          slotId: "activation-slot:definition-browser:default",
          definitionId: "definition-browser",
          slotName: "default",
          activeActivationId: "publication-existing",
          sourceKind: "publishing",
          sourceId: null,
          revision: 1,
          updatedAt: "2026-09-01T00:00:00Z"
        }] });
      }
      if (method === "GET" && path === "/publishing/publications/publication-existing") {
        return response({
          publicationId: "publication-existing",
          definitionId: "definition-browser",
          versionId: "version-1",
          artifactId: "artifact-1",
          slotName: "default",
          sourceReferenceId: "reference-1",
          status: "active"
        });
      }
      if (method === "GET" && path === "/design/workflows/versions/version-1") {
        return response({ id: "version-1", version: "1.0.0", definition: workflowDetails.definition, state: workflowDraft.state, layout: [] });
      }
      if (method === "PUT" && path === "/design/workflows/drafts/draft-browser") {
        return response({ ...workflowDraft, state: requestBody.state ?? workflowDraft.state, layout: requestBody.layout ?? [] });
      }
      if (method === "POST" && path === "/design/workflows/drafts/draft-browser/promotion-preflight") {
        return response({
          isReady: true,
          assignmentMode: "automatic",
          requestedVersion: null,
          resolvedVersion: "2.0.0",
          latestVersion: "1.0.0",
          issues: []
        });
      }
      if (method === "POST" && path === "/publishing/workflows/preflight") {
        const blocked = outcome === "conflict";
        return response({
          preflightToken: "browser-preflight-token",
          candidateHash: "browser-candidate-hash",
          definitionId: "definition-browser",
          versionId: null,
          slotName: requestBody.slotName ?? "default",
          resolvedAction: requestBody.action ?? "replace",
          policySource: "request",
          policyRevision: 1,
          canActivate: !blocked,
          claims: [],
          triggers: [],
          conflicts: blocked ? [{
            key: "http:GET:/orders",
            cardinality: "exclusive",
            publicationId: "publication-existing",
            slotName: "default"
          }] : [],
          targetSlotOwner: null
        });
      }
      if (method === "POST" && path === "/design/workflows/drafts/draft-browser/promote") {
        return response({ id: "version-2", version: "2.0.0" });
      }
      if (method === "POST" && path === "/publishing/workflows/version-2/publish") {
        publishAttempts++;
        if (outcome === "publish-failure" && publishAttempts === 1) {
          return response({ title: "Activation timed out", status: 503, detail: "activation timed out" }, 503);
        }
        return response({
          publicationId: "publication-2",
          definitionId: "definition-browser",
          versionId: "version-2",
          artifactId: "artifact-2",
          slotName: "default",
          sourceReferenceId: "reference-2",
          status: "active"
        });
      }
      return response({ title: "Unexpected fixture API request", status: 404, detail: `${method} ${path}` }, 404);
    }
  });
  return { baseUrl, http };
}

function response(body: unknown, status = 200) {
  return new Response(JSON.stringify(body), { status, headers: { "Content-Type": "application/json" } });
}

function capabilities() {
  return {
    capabilities: [
      {
        id: "elsa.api.workflow-design",
        contractVersion: "1",
        links: [
          { rel: "workflow-drafts", href: "design/workflows/drafts/{draftId}", templated: true },
          { rel: "workflow-versions", href: "design/workflows/versions/{versionId}", templated: true },
          { rel: "workflow-draft-promote-version-preflight", href: "design/workflows/drafts/{draftId}/promotion-preflight", templated: true },
          { rel: "workflow-draft-promote-exact-version", href: "design/workflows/drafts/{draftId}/promote", templated: true }
        ]
      },
      {
        id: "elsa.api.publishing",
        contractVersion: "1",
        links: [
          { rel: "publication-policy", href: "publishing/workflows/{definitionId}/policy", templated: true },
          { rel: "publication-record", href: "publishing/publications/{publicationId}", templated: true },
          { rel: "publication-snapshot-preflight", href: "publishing/workflows/preflight" },
          { rel: "publication-preflight", href: "publishing/workflows/{versionId}/preflight", templated: true },
          { rel: "workflow-publish", href: "publishing/workflows/{versionId}/publish", templated: true }
        ]
      },
      {
        id: "elsa.api.runtime",
        contractVersion: "1",
        links: [
          { rel: "workflow-activation-slots", href: "runtime/workflows/activation-slots/{definitionId}", templated: true },
          { rel: "workflow-activation-slot", href: "runtime/workflows/activation-slots/{definitionId}/{slotName}", templated: true }
        ]
      }
    ]
  };
}
