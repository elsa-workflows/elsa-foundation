import React, { lazy, useCallback, useMemo, useRef, useState } from "react";
import { createRoot } from "react-dom/client";
import { ActivityPropertiesPanel } from "../../src/Elsa.Studio.Workflows/Client/src/ActivityPropertiesPanel";
import { WorkflowLazyBoundary } from "../../src/Elsa.Studio.Workflows/Client/src/WorkflowLazyBoundary";
import { WorkflowDefinitions } from "../../src/Elsa.Studio.Workflows/Client/src/workflow-editor/WorkflowDefinitions";
import { setDialogs } from "../../src/Elsa.Studio.Workflows/Client/src/workflow-editor/dialogs";
import { useRunDetailLayout } from "../../src/Elsa.Studio.Workflows/Client/src/workflow-editor/useRunDetailLayout";
import type { StudioActivityDescriptor, StudioAiContributionApi, StudioEndpointContext, StudioExpressionDescriptor } from "@elsa-workflows/studio-sdk";
import type { ActivityNode } from "../../src/Elsa.Studio.Workflows/Client/src/workflowTypes";
import "../../src/Elsa.Studio.Web/Client/src/app/ui/tokens.css";
import "../../src/Elsa.Studio.Workflows/Client/src/styles.css";
import "./fixture.css";

const searchParams = new URLSearchParams(window.location.search);
const scrollingFixture = searchParams.get("mode") === "scroll";
const dictionaryFixture = searchParams.get("mode") === "dictionary";
const lazyBoundaryFixture = searchParams.get("mode") === "lazy-boundary";
const runDetailFixture = searchParams.get("mode") === "run-detail";
const moveDefinitionsFixture = searchParams.get("mode") === "move-definitions";
const folderRestructureFixture = searchParams.get("mode") === "folder-restructure";
const moveDefinitionsFailureFixture = moveDefinitionsFixture && searchParams.get("move") === "failure";
const moveDefinitionsFolderSourceFixture = moveDefinitionsFixture && searchParams.get("source") === "folder";

const DeferredWorkflowPanel = lazy(() => new Promise<{ default: React.ComponentType }>(resolve => {
  window.setTimeout(() => resolve({ default: () => <section aria-label="Deferred workflow designer">Workflow designer ready</section> }), 3_000);
}));

const expressionDescriptors: StudioExpressionDescriptor[] = [
  { type: "Input", displayName: "Input", editingMode: "reference" },
  { type: "JavaScript", displayName: "JavaScript", editingMode: "text" },
  { type: "Liquid", displayName: "Liquid", editingMode: "text" },
  { type: "Literal", displayName: "Literal", editingMode: "literal" },
  { type: "Object", displayName: "Object", editingMode: "structured" },
  { type: "Secret", displayName: "Secret", editingMode: "reference" },
  { type: "Variable", displayName: "Variable", editingMode: "reference" }
];

const descriptor: StudioActivityDescriptor = {
  typeName: "Elsa.Activities.Http.Activities.HttpEndpoint",
  displayName: "HTTP Endpoint",
  inputs: [{
    name: "Path",
    typeName: "System.String",
    displayName: "Path",
    description: "The route handled by this endpoint.",
    order: 0,
    category: "General",
    isBrowsable: true,
    uiHint: "singleline",
    isWrapped: true,
    defaultSyntax: "Literal"
  }],
  outputs: [],
  ports: []
};

const dictionaryDescriptor: StudioActivityDescriptor = {
  typeName: "Elsa.Activities.Http.Activities.HttpRequest",
  displayName: "HTTP Request",
  inputs: [{
    name: "Headers",
    typeName: "System.Collections.Generic.IDictionary`2[System.String,System.String]",
    displayName: "Headers",
    description: "Headers sent with the request.",
    order: 0,
    category: "General",
    isBrowsable: true,
    isWrapped: true,
    defaultSyntax: "Literal",
    uiSpecifications: {
      dictionary: {
        keyLabel: "Header name",
        valueLabel: "Header value",
        keyPlaceholder: "Content-Type",
        valuePlaceholder: "application/json",
        keyComparison: "ordinalIgnoreCase"
      }
    }
  }],
  outputs: [],
  ports: []
};

function Fixture() {
  const [activity, setActivity] = useState<ActivityNode>({
    nodeId: "http-endpoint-1",
    activityVersionId: "http-endpoint-v1",
    inputs: [{ referenceKey: "Path", value: { value: "/orders", expressionType: "Literal" } }],
    outputs: [],
    structure: null,
    ...(dictionaryFixture ? {
      headers: {
        typeName: "System.Collections.Generic.IDictionary`2[System.String,System.String]",
        expression: {
          type: "Literal",
          value: {
            Accept: "application/json",
            "X-Correlation-Id": "{{ correlationId }}",
            "Cache-Control": "no-cache",
            "User-Agent": "Elsa Studio",
            "X-Region": "eu-west",
            "X-Trace": "enabled"
          }
        }
      }
    } : {})
  });

  const activeDescriptor = dictionaryFixture ? dictionaryDescriptor : descriptor;

  return (
    <main className="wf-editor browser-fixture">
      <div className="browser-fixture-copy">
        <h1>Workflow designer</h1>
        <p>The inspector intentionally clips its own content to reproduce the original stacking defect.</p>
      </div>
      <aside className={`wf-inspector browser-inspector${scrollingFixture ? " browser-inspector--scroll" : ""}`} aria-label="Activity inspector">
        <h2>{dictionaryFixture ? "HTTP Request" : "HTTP Endpoint"}</h2>
        <div className="browser-inspector-spacer" aria-hidden="true" />
        <ActivityPropertiesPanel
          activity={activity}
          descriptor={activeDescriptor}
          editors={[]}
          expressionEditors={[]}
          expressionDescriptors={expressionDescriptors}
          expressionDescriptorStatus="ready"
          descriptorStatus="ready"
          visibleVariables={[]}
          scopeStatus="ready"
          onChange={setActivity}
        />
        {scrollingFixture ? <div className="browser-inspector-tail" aria-hidden="true" /> : null}
      </aside>
    </main>
  );
}

function LazyBoundaryFixture() {
  return (
    <main className="browser-fixture">
      <h1>Workflow management</h1>
      <WorkflowLazyBoundary label="workflow designer">
        <DeferredWorkflowPanel />
      </WorkflowLazyBoundary>
    </main>
  );
}

function MoveDefinitionsFixture() {
  const [moved, setMoved] = useState(false);
  const movedRef = useRef(false);
  const destinationRef = useRef<string | null | undefined>(undefined);
  const movedDefinitionIdRef = useRef<string | undefined>(undefined);
  const folder = useMemo(() => ({ id: "folder-operations", parentId: null, name: "Operations", normalizedName: "operations", createdAt: "", lastModifiedAt: "" }), []);
  const definition = (id: string, name: string, folderId: string | null = null) => ({
    id, name, description: "Browser workflow", createdAt: "2026-07-19T00:00:00Z", lastModifiedAt: movedRef.current ? "2026-07-19T00:01:00Z" : "2026-07-19T00:00:00Z",
    latestVersion: "1.0.0", versionCount: 1, draftId: null, deletedAt: null, folderId
  });
  const context = useMemo(() => ({
    baseUrl: "browser-move-definitions",
    http: {
      getJson: async (url: string) => {
        if (url === "/capabilities") return { capabilities: [{
          id: "elsa.api.workflow-design", contractVersion: "1", links: [
            { rel: "workflow-definitions-page", href: "browser/definition-pages" },
            { rel: "workflow-folders", href: "browser/folders" },
            { rel: "workflow-definition-folder-move", href: "browser/definition-placement" }
          ]
        }] };
        if (url.startsWith("/browser/folders/folder-operations")) return { folder, ancestors: [] };
        if (url.startsWith("/browser/folders")) return { items: [folder], nextContinuationToken: null };
        if (url.startsWith("/browser/definition-pages")) {
          (window as Window & { capabilityRequests?: string[] }).capabilityRequests = [
            ...((window as Window & { capabilityRequests?: string[] }).capabilityRequests ?? []),
            url
          ];
          const query = new URL(url, window.location.origin).searchParams;
          const folderId = query.get("folderId");
          const unfiled = query.get("unfiled") === "true";
          const next = query.get("continuationToken");
          const movedId = movedDefinitionIdRef.current ?? "definition-browser";
          const movedName = movedId === "folder-definition-2" ? "Moved folder workflow" : "Moved workflow";
          if (folderId && moveDefinitionsFolderSourceFixture) {
            if (movedRef.current) return {
              items: [
                definition("folder-definition-1", "Folder remaining workflow", folder.id),
                ...(destinationRef.current === folderId ? [definition(movedId, movedName, folder.id)] : [])
              ],
              nextContinuationToken: null
            };
            return next
              ? { items: [definition("folder-definition-2", "Folder page 2 workflow", folder.id)], nextContinuationToken: null }
              : { items: [definition("folder-definition-1", "Folder page 1 workflow", folder.id)], nextContinuationToken: "folder-page-2" };
          }
          if (folderId) return { items: destinationRef.current === folderId ? [definition(movedId, movedName, folderId)] : [], nextContinuationToken: null };
          if (unfiled) return { items: destinationRef.current === null ? [definition(movedId, movedName)] : [], nextContinuationToken: null };
          if (movedRef.current) return { items: [definition(movedId, movedName)], nextContinuationToken: null };
          return next ? { items: [definition("definition-2", "Second page workflow")], nextContinuationToken: null } : { items: [definition("definition-1", "First page workflow")], nextContinuationToken: "page-2" };
        }
        throw new Error(`Unexpected browser fixture request: ${url}`);
      },
      postJson: async (url: string, body: unknown) => {
        (window as Window & { moveRequests?: unknown[] }).moveRequests = [...((window as Window & { moveRequests?: unknown[] }).moveRequests ?? []), { url, body }];
        if (moveDefinitionsFailureFixture) throw new Error("Destination is currently unavailable.");
        const placement = body as { definitionIds: string[]; folderId: string | null };
        movedDefinitionIdRef.current = placement.definitionIds[0];
        destinationRef.current = placement.folderId;
        movedRef.current = true;
        setMoved(true);
        return {};
      }
    }
  }) as unknown as StudioEndpointContext, [folder]);
  const ai = useMemo(() => ({ promptActions: { list: () => [] }, dispatchPrompt: () => undefined }) as unknown as StudioAiContributionApi, []);

  return <main className="wf-editor browser-fixture" data-moved={moved}>
    <h1>Workflow definitions</h1>
    <WorkflowDefinitions context={context} ai={ai} onOpen={() => undefined} />
  </main>;
}

function FolderRestructureFixture() {
  const [, setRevision] = useState(0);
  const foldersRef = useRef([
    { id: "folder-platform", parentId: null as string | null, name: "Platform", normalizedName: "platform", createdAt: "", lastModifiedAt: "" },
    { id: "folder-operations", parentId: "folder-platform" as string | null, name: "Operations", normalizedName: "operations", createdAt: "", lastModifiedAt: "" },
    { id: "folder-descendant", parentId: "folder-operations" as string | null, name: "Private descendant", normalizedName: "private descendant", createdAt: "", lastModifiedAt: "" },
    { id: "folder-archive", parentId: null as string | null, name: "Archive", normalizedName: "archive", createdAt: "", lastModifiedAt: "" },
    { id: "folder-empty", parentId: "folder-archive" as string | null, name: "Empty folder", normalizedName: "empty folder", createdAt: "", lastModifiedAt: "" }
  ]);
  const capabilityAbsent = searchParams.get("capabilities") === "absent";
  const continuationPaging = searchParams.get("paging") === "continuation";
  const rejectedOperation = searchParams.get("failure");
  const definition = useCallback((id: string, name: string, folderId: string | null = null, folderBreadcrumb?: { id: string; name: string }[]) => ({
    id, name, description: "Browser workflow", createdAt: "2026-07-19T00:00:00Z", lastModifiedAt: "2026-07-19T00:00:00Z",
    latestVersion: "1.0.0", versionCount: 1, draftId: null, deletedAt: null, folderId, ...(folderBreadcrumb ? { folderBreadcrumb } : {})
  }), []);
  const findFolder = useCallback((id: string) => foldersRef.current.find(folder => folder.id === id), []);
  const ancestorsOf = useCallback((folder: NonNullable<ReturnType<typeof findFolder>>) => {
    const result: typeof foldersRef.current = [];
    let parentId = folder.parentId;
    while (parentId) {
      const parent = findFolder(parentId);
      if (!parent) break;
      result.unshift(parent);
      parentId = parent.parentId;
    }
    return result;
  }, [findFolder]);

  setDialogs({
    confirm: async options => {
      (window as Window & { folderConfirmations?: unknown[] }).folderConfirmations = [
        ...((window as Window & { folderConfirmations?: unknown[] }).folderConfirmations ?? []),
        options
      ];
      return true;
    },
    prompt: async () => null,
    alert: async () => undefined
  });

  const context = useMemo(() => ({
    baseUrl: "browser-folder-restructure",
    http: {
      getJson: async (url: string) => {
        if (url === "/capabilities") return { capabilities: [{
          id: "elsa.api.workflow-design", contractVersion: "1", links: [
            { rel: "workflow-definitions-page", href: "browser/restructure/definition-pages" },
            { rel: "workflow-folders", href: "browser/restructure/folders" },
            ...(capabilityAbsent ? [] : [
              { rel: "workflow-folder-rename", href: "browser/restructure/folders/{folderId}/rename", templated: true },
              { rel: "workflow-folder-move", href: "browser/restructure/folders/{folderId}/move", templated: true },
              { rel: "workflow-folder-delete-empty", href: "browser/restructure/folders/{folderId}", templated: true }
            ])
          ]
        }] };
        if (url.startsWith("/browser/restructure/folders?")) {
          const query = new URL(url, window.location.origin).searchParams;
          const parentId = query.get("parentId");
          if (continuationPaging && !parentId) {
            return query.get("continuationToken") === "root-next"
              ? { items: foldersRef.current.filter(folder => folder.id === "folder-archive"), nextContinuationToken: null }
              : { items: foldersRef.current.filter(folder => folder.id === "folder-platform"), nextContinuationToken: "root-next" };
          }
          return { items: foldersRef.current.filter(folder => folder.parentId === parentId), nextContinuationToken: null };
        }
        if (url.startsWith("/browser/restructure/folders/")) {
          const id = decodeURIComponent(url.slice("/browser/restructure/folders/".length));
          const folder = findFolder(id);
          return folder ? { folder, ancestors: ancestorsOf(folder) } : null;
        }
        if (url.startsWith("/browser/restructure/definition-pages")) {
          (window as Window & { browseRequests?: string[] }).browseRequests = [
            ...((window as Window & { browseRequests?: string[] }).browseRequests ?? []),
            url
          ];
          const query = new URL(url, window.location.origin).searchParams;
          const folderId = query.get("folderId");
          const items = folderId === "folder-operations"
            ? [definition("definition-operations", "Operations workflow", folderId)]
            : folderId === "folder-platform"
              ? [definition("definition-platform", "Platform workflow", folderId)]
              : folderId === "folder-archive"
                ? [definition("definition-archive", "Archive workflow", folderId)]
                : folderId === "folder-empty"
                  ? []
                  : [definition("definition-all", "All workflow", "folder-operations", [
                    { id: "folder-platform", name: "Platform" },
                    { id: "folder-operations", name: "Operations" }
                  ])];
          return { items, nextContinuationToken: null };
        }
        throw new Error(`Unexpected browser fixture request: ${url}`);
      },
      postJson: async (url: string, body: unknown) => {
        (window as Window & { folderMutationRequests?: unknown[] }).folderMutationRequests = [
          ...((window as Window & { folderMutationRequests?: unknown[] }).folderMutationRequests ?? []),
          { method: "POST", url, body }
        ];
        const rename = url.match(/^\/browser\/restructure\/folders\/([^/]+)\/rename$/);
        if (rename) {
          if (rejectedOperation === "rename") throw new Error("Rename rejected by the server.");
          const folder = findFolder(decodeURIComponent(rename[1]));
          if (folder) {
            folder.name = (body as { name: string }).name;
            folder.normalizedName = folder.name.toLocaleLowerCase();
          }
          setRevision(current => current + 1);
          return {};
        }
        const move = url.match(/^\/browser\/restructure\/folders\/([^/]+)\/move$/);
        if (move) {
          if (rejectedOperation === "move") throw new Error("Move rejected by the server.");
          const folder = findFolder(decodeURIComponent(move[1]));
          if (folder) folder.parentId = (body as { parentId: string | null }).parentId;
          setRevision(current => current + 1);
          return {};
        }
        throw new Error(`Unexpected browser fixture POST: ${url}`);
      },
      deleteJson: async (url: string) => {
        (window as Window & { folderMutationRequests?: unknown[] }).folderMutationRequests = [
          ...((window as Window & { folderMutationRequests?: unknown[] }).folderMutationRequests ?? []),
          { method: "DELETE", url }
        ];
        if (rejectedOperation === "delete") throw new Error("Folder is not empty.");
        const id = decodeURIComponent(url.slice("/browser/restructure/folders/".length));
        foldersRef.current = foldersRef.current.filter(folder => folder.id !== id);
        setRevision(current => current + 1);
        return {};
      }
    }
  }) as unknown as StudioEndpointContext, [ancestorsOf, capabilityAbsent, continuationPaging, definition, findFolder, rejectedOperation]);
  const ai = useMemo(() => ({ promptActions: { list: () => [] }, dispatchPrompt: () => undefined }) as unknown as StudioAiContributionApi, []);

  return <main className="wf-editor browser-fixture">
    <h1>Workflow folders</h1>
    <WorkflowDefinitions context={context} ai={ai} onOpen={() => undefined} />
  </main>;
}

function RunDetailFixture() {
  const [selectedActivityId, setSelectedActivityId] = useState<string | null>(null);
  const layout = useRunDetailLayout({ selectedActivityId });

  return (
    <div className="browser-run-shell">
      <header className="browser-run-shell-header">Elsa Foundation Studio</header>
      <div className="content browser-run-content">
        <section className="wf-page wf-page--run-workbench">
          <div className="wf-page-header">
            <div><span className="wf-kicker">Workflow management</span><h2>Run</h2></div>
            <code>wfexec-browser</code>
          </div>
          <div className="wf-toolbar">
            <button type="button" onClick={() => setSelectedActivityId("activity-1")}>Select activity</button>
            <button type="button" onClick={() => {
              layout.closeInspector();
              setSelectedActivityId(null);
            }}>Close details</button>
          </div>
          <div
            ref={layout.containerRef}
            className={layout.workbenchClassName}
            style={layout.workbenchStyle}
            data-testid="run-workbench"
            data-layout-mode={layout.mode}
          >
            <section className="wf-instance-canvas-shell" aria-label="Workflow run canvas">
              <header><h3>Canvas</h3></header>
              <div className="wf-instance-canvas">Activity graph</div>
            </section>
            <div className="wf-side-resize-spacer" />
            <aside className="wf-instance-inspector" aria-label="Run details">
              <header><h3>Activity details</h3></header>
              <div className="wf-instance-section">Evaluated inputs</div>
            </aside>
          </div>
        </section>
      </div>
      <footer className="browser-run-console">Console</footer>
    </div>
  );
}

const theme = searchParams.get("theme");
document.documentElement.dataset.theme = theme === "black-glass" ? "black-glass" : "harbor";
document.documentElement.dataset.themeMode = theme === "black-glass" ? "dark" : "light";
createRoot(document.getElementById("root")!).render(
  runDetailFixture ? <RunDetailFixture /> : lazyBoundaryFixture ? <LazyBoundaryFixture /> : folderRestructureFixture ? <FolderRestructureFixture /> : moveDefinitionsFixture ? <MoveDefinitionsFixture /> : <Fixture />
);
