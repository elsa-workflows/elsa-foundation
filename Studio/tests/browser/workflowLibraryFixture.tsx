import { getTheme, themeTokenNames } from "../../src/essentials/Elsa.Studio.Hosting/Client/src/app/themes/presets";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import type { StudioAgentRegistry, StudioEndpointContext } from "@elsa-workflows/studio-sdk";
import { WorkflowOrganizationButton } from "../../src/essentials/Elsa.Studio.Workflows/Client/src/workflow-editor/WorkflowOrganizationButton";
import { WorkflowDefinitions } from "../../src/essentials/Elsa.Studio.Workflows/Client/src/workflow-editor/WorkflowDefinitions";
import { useWorkflowDefinitionBrowseSession } from "../../src/essentials/Elsa.Studio.Workflows/Client/src/workflow-editor/workflowDefinitionBrowseSession";
import type { WorkflowLibraryView, WorkflowOrganization, WorkflowTag, WorkflowTagAssignment, WorkflowTagBulkOutcome, WorkflowTagFilter, WorkflowTagInput } from "../../src/essentials/Elsa.Studio.Workflows/Client/src/api/workflowOrganization";
import type { WorkflowDefinitionSummary, WorkflowFolder } from "../../src/essentials/Elsa.Studio.Workflows/Client/src/workflowTypes";
import "../../src/essentials/Elsa.Studio.Hosting/Client/src/app/styles.css";
import "../../src/essentials/Elsa.Studio.Workflows/Client/src/styles.css";

const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
const parameters = new URLSearchParams(window.location.search);
if (parameters.get("palette") === "classic") {
  const dark = parameters.get("theme") === "dark";
  document.documentElement.dataset.theme = "classic";
  document.documentElement.dataset.themeMode = dark ? "dark" : "light";
  const palette = getTheme("classic")![dark ? "dark" : "light"];
  for (const token of themeTokenNames) document.documentElement.style.setProperty(`--${token.replace(/([A-Z])/g, "-$1").toLowerCase()}`, palette[token]);
}
const readonly = parameters.get("permissions") === "read";
const permissions = { canAssignTags: !readonly, canManageFolders: !readonly, canManageTags: !readonly, canShareViews: !readonly };
const date = "2026-10-31T12:00:00Z";
const folderLayout = parameters.get("folder-layout") === "1";
const folderName = folderLayout ? "Customer operations and international support" : "Finance";
const folder: WorkflowFolder = { id: "finance", parentId: null, name: folderName, normalizedName: folderName.toUpperCase(), createdAt: date, lastModifiedAt: date, revision: "1" };
const layoutFolders: WorkflowFolder[] = folderLayout ? [
  ...Array.from({ length: parameters.get("tall-folders") === "1" ? 8 : 0 }, (_, index) => ({ ...folder, id: `layout-${index}`, name: `Operations ${index + 1}`, normalizedName: `OPERATIONS ${index + 1}` })),
  ...Array.from({ length: parameters.get("deep-folders") === "1" ? 15 : 1 }, (_, index) => {
    const name = index === 0 ? "European customer onboarding and retention" : `International support and retention level ${index + 2}`;
    return { ...folder, id: index === 0 ? "layout-child" : `layout-depth-${index + 1}`, parentId: index === 0 ? folder.id : index === 1 ? "layout-child" : `layout-depth-${index}`, name, normalizedName: name.toUpperCase() };
  })
] : [];
const tags: WorkflowTag[] = [
  { id: "critical", name: "Critical", mode: "marker", cardinality: "single", deprecated: false, revision: "1", values: [] },
  { id: "team", name: "Owning team", mode: "controlled", cardinality: "single", deprecated: false, revision: "1", values: [{ id: "value-billing", name: "Billing" }, { id: "value-support", name: "Support" }] }
];
if (parameters.get("tags") === "empty") tags.splice(0);
if (parameters.get("tags") === "many") tags.push(...Array.from({ length: 40 }, (_, index): WorkflowTag => ({ id: `extra-${index}`, name: `Additional classification ${index + 1}`, mode: "marker", cardinality: "single", deprecated: false, revision: "1", values: [] })));
if (parameters.has("long-folder")) folder.name = "Customer operations / European customer onboarding and retention / Payment collection and exception handling";
const definitions: WorkflowDefinitionSummary[] = [
  { id: "collect-payment", name: "Collect overdue payment", createdAt: date, lastModifiedAt: date, draftId: "draft-payment", latestVersionId: "version-payment", versionCount: 1, publicationState: "published", folderId: folder.id, folderBreadcrumb: [{ id: folder.id, name: folder.name }], tags: [{ tagId: "team", valueId: "value-billing" }], favorite: false },
  { id: "welcome", name: "Welcome new customer", createdAt: date, lastModifiedAt: date, draftId: "draft-welcome", versionCount: 0, folderId: null, folderBreadcrumb: [], tags: [], favorite: false }
];
const organizations = new Map(definitions.map(definition => [definition.id, {
  definitionId: definition.id, folderId: definition.folderId ?? null,
  folderBreadcrumb: definition.folderId ? [folder] : [], tags: definition.tags ?? [], revision: "1", permissions
} satisfies WorkflowOrganization]));
const views: WorkflowLibraryView[] = [];
const requests: { method: string; url: string; body?: unknown }[] = [];
let conflict = parameters.get("conflict") === "tags";
let nextIdentity = 1;
const capabilities = { capabilities: [{ id: "elsa.api.workflow-design", contractVersion: "1", links: [
  { rel: "workflow-definitions", href: "fixture/library/definitions" },
  { rel: "workflow-definitions-page", href: "fixture/library/pages" },
  { rel: "workflow-folders", href: "fixture/library/folders" },
  { rel: "workflow-definition-folder-move", href: "fixture/library/placement" },
  { rel: "workflow-folder-rename", href: "fixture/library/folders/{folderId}/rename", templated: true },
  { rel: "workflow-folder-move", href: "fixture/library/folders/{folderId}/move", templated: true },
  { rel: "workflow-folder-delete-empty", href: "fixture/library/folders/{folderId}", templated: true },
  { rel: "workflow-tags", href: "fixture/library/tags" },
  { rel: "workflow-tag", href: "fixture/library/tags/{tagId}", templated: true },
  { rel: "workflow-organization", href: "fixture/library/organization/{definitionId}", templated: true },
  { rel: "workflow-tags-bulk", href: "fixture/library/bulk" },
  { rel: "workflow-views", href: "fixture/library/views" },
  { rel: "workflow-view", href: "fixture/library/views/{viewId}", templated: true },
  { rel: "workflow-favorites", href: "fixture/library/favorites/{definitionId}", templated: true }
].filter(link => (parameters.get("folders") !== "unavailable" || !link.rel.includes("folder"))
  && (parameters.get("organization") !== "off" || ["workflow-definitions", "workflow-definitions-page"].includes(link.rel))) }] };

Object.assign(window, { workflowLibraryRequests: requests, workflowLibraryDefinitions: definitions });

const record = (method: string, url: string, body?: unknown) => { requests.push({ method, url, ...(body === undefined ? {} : { body }) }); };
const revision = (value: string) => String(Number(value) + 1);
function controlledValues(input: WorkflowTagInput, previous?: WorkflowTag) {
  return (input.values ?? []).map(value => {
    if (value.id && !previous?.values.some(existing => existing.id === value.id)) {
      throw new Error("New controlled values must have an empty ID; the server allocates value-* identities.");
    }
    return { ...value, id: value.id || `value-fixture-${nextIdentity++}` };
  });
}
const context = { baseUrl: `${window.location.origin}/`, http: {
  getJson: async (url: string) => {
    record("GET", url);
    if (url === "/capabilities") return capabilities;
    const target = new URL(url, window.location.origin);
    if (target.pathname === "/fixture/library/tags") {
      const term = target.searchParams.get("search")?.toLowerCase() ?? "";
      return { items: tags.filter(tag => tag.name.toLowerCase().includes(term)), nextContinuationToken: null, permissions };
    }
    if (target.pathname === "/fixture/library/views") return { items: views, permissions };
    if (target.pathname.startsWith("/fixture/library/tags/")) {
      const tag = tags.find(item => item.id === target.pathname.split("/").at(-1));
      if (!tag) throw new Error("Tag unavailable");
      return structuredClone(tag);
    }
    if (target.pathname === "/fixture/library/folders") return { items: target.searchParams.has("parentId") ? layoutFolders.filter(item => item.parentId === target.searchParams.get("parentId")) : [folder, ...layoutFolders.filter(item => item.parentId === null)], nextContinuationToken: null };
    if (target.pathname === "/fixture/library/folders/finance") return { folder, ancestors: [] };
    if (target.pathname.startsWith("/fixture/library/organization/")) {
      const organization = organizations.get(target.pathname.split("/").at(-1)!);
      if (!organization) throw new Error("Workflow unavailable");
      return structuredClone(organization);
    }
    if (target.pathname === "/fixture/library/pages") {
      const query = target.searchParams;
      const paging = parameters.get("paging");
      if (["history", "count-shrinks", "count-grows"].includes(paging ?? "")) {
        const cursor = query.get("continuationToken");
        const page = cursor === "tag-page-three" || cursor === "A100" ? 3 : cursor === "tag-page-two" || cursor === "A50" ? 2 : 1;
        const scope = query.get("favoritesOnly") === "true" ? "Favorite" : "Tagged";
        const items = Array.from({ length: Number(query.get("pageSize") ?? 10) }, (_, index) => ({
          ...definitions[0], id: `${scope.toLowerCase()}-${page}-${index}`,
          name: index === 0 ? `${scope} workflow page ${page}` : `${scope} workflow ${page}-${index}`,
          favorite: scope === "Favorite", tags: [{ tagId: "critical" }]
        }));
        const totalCount = paging === "history" ? 75 : page === 1 ? 150 : paging === "count-shrinks" ? 100 : 200;
        const nextContinuationToken = paging === "history" ? page < 3 ? `tag-page-${page === 1 ? "two" : "three"}` : null
          : page === 1 ? `A${query.get("pageSize") ?? 10}` : page === 2 && paging === "count-shrinks" ? "A100" : null;
        return { items, nextContinuationToken, totalCount, permissions };
      }
      let matches = definitions.filter(definition => !definition.deletedAt);
      if (query.get("folderId")) matches = matches.filter(definition => definition.folderId === query.get("folderId"));
      if (query.get("unfiled") === "true") matches = matches.filter(definition => !definition.folderId);
      if (query.get("favoritesOnly") === "true") matches = matches.filter(definition => definition.favorite);
      if (query.get("publicationState")) matches = matches.filter(definition => definition.publicationState === query.get("publicationState"));
      const term = query.get("searchTerm")?.toLowerCase();
      if (term) matches = matches.filter(definition => definition.name.toLowerCase().includes(term));
      const clauses = JSON.parse(query.get("tagFilters") ?? "[]") as WorkflowTagFilter[];
      matches = matches.filter(definition => clauses.every(clause => {
        const assigned = definition.tags?.filter(tag => tag.tagId === clause.tagId) ?? [];
        if (clause.operator === "exists") return assigned.length > 0;
        if (clause.operator === "missing") return assigned.length === 0;
        const values = clause.valueIds ?? [];
        if (clause.operator === "all") return values.every(value => assigned.some(tag => tag.valueId === value));
        const any = assigned.some(tag => tag.valueId && values.includes(tag.valueId));
        return clause.operator === "none" ? !any : any;
      }));
      matches.sort((a, b) => a.name.localeCompare(b.name) * (query.get("sort") === "name-desc" ? -1 : 1));
      return { items: structuredClone(matches), nextContinuationToken: null, totalCount: matches.length, permissions };
    }
    return { items: [] };
  },
  putJson: async (url: string, body: unknown) => {
    record("PUT", url, structuredClone(body));
    if (readonly) throw new Error("Permission denied");
    const target = new URL(url, window.location.origin);
    const id = target.pathname.split("/").at(-1)!;
    if (target.pathname.includes("/organization/")) {
      const current = organizations.get(id)!;
      const request = body as { tags: WorkflowTagAssignment[]; revision: string };
      if (conflict || current.revision !== request.revision) { conflict = false; throw new Error("Organization changed concurrently (409)"); }
      current.tags = structuredClone(request.tags); current.revision = revision(current.revision);
      definitions.find(definition => definition.id === id)!.tags = current.tags;
      return structuredClone(current);
    }
    if (target.pathname.includes("/favorites/")) { definitions.find(definition => definition.id === id)!.favorite = true; return {}; }
    if (target.pathname.includes("/tags/")) {
      const index = tags.findIndex(tag => tag.id === id); const request = body as WorkflowTagInput;
      if (tags[index].revision !== request.revision) throw new Error("Tag changed concurrently (409)");
      tags[index] = { ...tags[index], ...request, deprecated: request.deprecated ?? false, values: controlledValues(request, tags[index]), revision: revision(tags[index].revision) };
      return structuredClone(tags[index]);
    }
    if (target.pathname.includes("/views/")) { const index = views.findIndex(view => view.id === id); views[index] = { ...views[index], ...(body as object), revision: revision(views[index].revision) }; return views[index]; }
    throw new Error(`Unexpected PUT ${url}`);
  },
  postJson: async (url: string, body: unknown) => {
    record("POST", url, structuredClone(body));
    if (readonly) throw new Error("Permission denied");
    if (url.endsWith("/tags")) { const input = body as WorkflowTagInput; const created: WorkflowTag = { ...input, id: `tag-${nextIdentity++}`, deprecated: false, revision: "1", values: controlledValues(input) }; tags.push(created); return structuredClone(created); }
    if (url.endsWith("/views")) { const created: WorkflowLibraryView = { ...(body as WorkflowLibraryView), id: `view-${nextIdentity++}`, ownerId: "current-user", revision: "1", schemaVersion: 1, canEdit: true, canDelete: true }; views.push(created); return created; }
    if (url.endsWith("/bulk")) {
      const input = body as { targets: { definitionId: string; revision: string }[]; add: WorkflowTagAssignment[]; remove: WorkflowTagAssignment[] };
      return { outcomes: input.targets.map(target => {
        const current = organizations.get(target.definitionId)!;
        if (target.definitionId === "welcome" && parameters.get("bulk") === "partial") return { definitionId: target.definitionId, status: "conflict", message: "Workflow changed after preview" };
        if (current.revision !== target.revision) return { definitionId: target.definitionId, status: "conflict" };
        const removed = current.tags.filter(tag => !input.remove.some(assignment => assignment.tagId === tag.tagId && (assignment.valueId ?? null) === (tag.valueId ?? null)));
        current.tags = [...new Map([...removed, ...input.add].map(tag => [JSON.stringify(tag), tag])).values()]; current.revision = revision(current.revision);
        definitions.find(definition => definition.id === target.definitionId)!.tags = current.tags;
        return { definitionId: target.definitionId, status: "success", revision: current.revision };
      }) satisfies WorkflowTagBulkOutcome[] };
    }
    if (url.endsWith("/placement")) { const input = body as { definitionIds: string[]; folderId: string | null }; for (const id of input.definitionIds) { const current = organizations.get(id)!; current.folderId = input.folderId; current.folderBreadcrumb = input.folderId ? [folder] : []; const definition = definitions.find(item => item.id === id)!; definition.folderId = input.folderId; definition.folderBreadcrumb = current.folderBreadcrumb; } return {}; }
    throw new Error(`Unexpected POST ${url}`);
  },
  deleteJson: async (url: string) => {
    record("DELETE", url);
    const target = new URL(url, window.location.origin); const id = target.pathname.split("/").at(-1)!;
    if (target.pathname.includes("/favorites/")) { definitions.find(definition => definition.id === id)!.favorite = false; return {}; }
    if (target.pathname.includes("/views/")) { views.splice(views.findIndex(view => view.id === id), 1); return {}; }
    throw new Error(`Unexpected DELETE ${url}`);
  }
} } as unknown as StudioEndpointContext;
const ai = { promptActions: { list: () => [] }, dispatchPrompt: () => undefined } as unknown as StudioAgentRegistry;

function Library() {
  const session = useWorkflowDefinitionBrowseSession(context);
  if (parameters.get("organization-surface") === "properties") return <main className="wf-canvas-shell wf-tokens" style={{ height: "100vh" }}><WorkflowOrganizationButton context={context} definitionId="welcome" name="Welcome new customer" onSaved={() => undefined} /><div className="wf-properties-view"><section className="wf-properties-section"><h3>Information</h3><p>Welcome new customer</p></section></div></main>;
  return <main className="wf-page wf-page--definitions"><WorkflowDefinitions context={context} ai={ai} session={session} onOpen={() => undefined} /></main>;
}
export function WorkflowLibraryFixture() { return <QueryClientProvider client={queryClient}><Library /></QueryClientProvider>; }
