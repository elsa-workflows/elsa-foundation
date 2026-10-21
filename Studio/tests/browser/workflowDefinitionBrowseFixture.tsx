import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import type { StudioAiContributionApi, StudioEndpointContext } from "@elsa-workflows/studio-sdk";
import { WorkflowManagementPage } from "../../src/essentials/Elsa.Studio.Workflows/Client/src/workflow-editor/pages";

const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
const continuationToken = "opaque-cursor/next+002";
const folder = {
  id: "folder-operations",
  parentId: null,
  name: "Operations",
  normalizedName: "operations",
  createdAt: "2026-10-20T00:00:00Z",
  lastModifiedAt: "2026-10-20T00:00:00Z"
};

function definition(id: string, name: string) {
  return {
    id,
    name,
    description: "Browser workflow fixture",
    createdAt: "2026-10-20T00:00:00Z",
    lastModifiedAt: "2026-10-20T00:00:00Z",
    deletedAt: null,
    draftId: `draft-${id}`,
    latestVersionId: null,
    latestVersion: null,
    publishedVersionId: null,
    publishedVersion: null,
    versionCount: 0,
    folderId: folder.id,
    folderBreadcrumb: [{ id: folder.id, name: folder.name }]
  };
}

function recordDefinitionPageRequest(url: string) {
  const request = new URL(url, window.location.origin);
  const requests = (window as Window & { workflowDefinitionPageRequests?: Record<string, string>[] })
    .workflowDefinitionPageRequests ?? [];
  requests.push(Object.fromEntries(request.searchParams.entries()));
  (window as Window & { workflowDefinitionPageRequests?: Record<string, string>[] })
    .workflowDefinitionPageRequests = requests;
}

type DefinitionPage = { items: ReturnType<typeof definition>[]; nextContinuationToken: string | null };
type DeferredPageRequest = {
  searchTerm: string;
  reject(error: Error): void;
};

const deferredSearchTerms = new Set<string>();
const deferredPageRequests: DeferredPageRequest[] = [];
const browseTestControl = {
  deferNext(searchTerm: string) {
    deferredSearchTerms.add(searchTerm);
  },
  rejectPending(searchTerm: string, message: string) {
    const index = deferredPageRequests.findIndex(request => request.searchTerm === searchTerm);
    if (index < 0) throw new Error(`No deferred workflow-definition request for "${searchTerm}"`);
    deferredPageRequests.splice(index, 1)[0].reject(new Error(message));
  }
};

(window as Window & { workflowBrowseTestControl?: typeof browseTestControl }).workflowBrowseTestControl = browseTestControl;

const ai = {
  promptActions: { list: () => [] },
  dispatchPrompt: () => undefined,
  providerAvailability: { get: () => true, subscribe: () => () => undefined }
} as unknown as StudioAiContributionApi;

const context = {
  baseUrl: `${window.location.origin}/`,
  http: {
    getJson: async (url: string) => {
      if (url === "/capabilities") return {
        capabilities: [
          {
            id: "elsa.api.workflow-design",
            contractVersion: "1",
            links: [
              { rel: "workflow-definitions-page", href: "browser/definition-pages" },
              { rel: "workflow-definitions", href: "browser/definitions" },
              { rel: "workflow-folders", href: "browser/folders" }
            ]
          },
          {
            id: "elsa.api.activity-design",
            contractVersion: "1",
            links: [{ rel: "activity-catalog", href: "browser/activity-catalog" }]
          }
        ]
      };
      if (url.startsWith("/browser/definition-pages")) {
        recordDefinitionPageRequest(url);
        const query = new URL(url, window.location.origin).searchParams;
        const searchTerm = query.get("searchTerm") ?? "";
        const response: DefinitionPage = searchTerm === "renewal"
          ? query.get("continuationToken") === continuationToken
            ? { items: [definition("workflow-page-two", "Second page renewal")], nextContinuationToken: null }
            : { items: [definition("workflow-page-one", "First page renewal")], nextContinuationToken: continuationToken }
          : searchTerm === "empty"
            ? { items: [], nextContinuationToken: null }
            : searchTerm
              ? { items: [definition(`workflow-search-${searchTerm}`, "Latest workflow result")], nextContinuationToken: null }
              : { items: [definition("workflow-page-one", "First page renewal")], nextContinuationToken: continuationToken };

        if (deferredSearchTerms.delete(searchTerm)) {
          return await new Promise<DefinitionPage>((_, reject) => {
            deferredPageRequests.push({ searchTerm, reject });
          });
        }
        return response;
      }
      if (url === "/browser/folders?pageSize=100") return { items: [folder], nextContinuationToken: null };
      if (url === "/browser/folders/folder-operations") return { folder, ancestors: [] };
      if (url === "/browser/activity-catalog") return { activities: [] };
      if (url.startsWith("/browser/definitions/")) {
        const id = decodeURIComponent(url.slice("/browser/definitions/".length));
        const selected = id === "workflow-page-two"
          ? definition(id, "Second page renewal")
          : definition(id, "First page renewal");
        return {
          definition: selected,
          draft: {
            id: `draft-${id}`,
            definitionId: id,
            state: { rootActivity: null },
            layout: [],
            validationErrors: []
          },
          versions: []
        };
      }
      throw new Error(`Unexpected workflow-definition browse fixture request: ${url}`);
    }
  }
} as unknown as StudioEndpointContext;

export function WorkflowDefinitionBrowseFixture() {
  return <QueryClientProvider client={queryClient}>
    <WorkflowManagementPage
      context={context}
      ai={ai}
      propertyEditors={[]}
      expressionEditors={[]}
      runInputEditors={[]}
      workflowDesignerPanels={[]}
    />
  </QueryClientProvider>;
}
