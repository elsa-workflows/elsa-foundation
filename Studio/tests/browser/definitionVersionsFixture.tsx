import { useMemo } from "react";
import { AnonymousAuthProvider, type StudioAiContributionApi, type StudioEndpointContext } from "@elsa-workflows/studio-sdk";
import { WorkflowManagementPage } from "../../src/essentials/Elsa.Studio.Workflows/Client/src/workflow-editor/pages";
import { ShellFrame, getStudioNavigation } from "../../src/apps/Elsa.Studio.Web/Client/src/app/App";
import { ThemeProvider } from "../../src/apps/Elsa.Studio.Web/Client/src/app/components/ThemeProvider";
import { writeAppearancePreferences } from "../../src/apps/Elsa.Studio.Web/Client/src/app/themes/appearance";
import { setDialogs } from "../../src/essentials/Elsa.Studio.Workflows/Client/src/workflow-editor/dialogs";
import "../../src/apps/Elsa.Studio.Web/Client/src/app/styles.css";
import type { WorkflowDefinitionSummary } from "../../src/essentials/Elsa.Studio.Workflows/Client/src/workflowTypes";

type DefinitionFixtureOverrides = Pick<WorkflowDefinitionSummary, "id" | "name" | "description">
  & Partial<Omit<WorkflowDefinitionSummary, "id" | "name" | "description">>;

function definition(overrides: DefinitionFixtureOverrides): WorkflowDefinitionSummary {
  return {
    createdAt: "2026-07-19T00:00:00Z",
    lastModifiedAt: "2026-07-19T00:00:00Z",
    draftId: null,
    versionCount: 1,
    ...overrides
  };
}

const definitions = [
  definition({
    id: "definition-draft-base-current",
    name: "Published policy workflow with a long title to exercise the definition table grid and action column alignment",
    description: "Published version 2.0.0 with a draft based on saved version 3.0.0.",
    draftId: "draft-policy",
    draftSourceVersionId: "version-3",
    draftSourceVersion: "3.0.0",
    latestVersionId: "version-3",
    latestVersion: "3.0.0",
    publishedVersionId: "version-2",
    publishedVersion: "2.0.0",
    publicationState: "published" as const,
    versionCount: 3
  }),
  definition({
    id: "definition-published-no-draft",
    name: "Published workflow without a draft",
    description: "Latest saved version 3.0.0, published version 2.0.0.",
    draftId: null,
    latestVersionId: "version-3",
    latestVersion: "3.0.0",
    publishedVersionId: "version-2",
    publishedVersion: "2.0.0",
    publicationState: "published" as const,
    versionCount: 3
  }),
  definition({
    id: "definition-draft-only",
    name: "Draft only workflow",
    description: "A new workflow with no saved or published version.",
    draftId: "draft-new-workflow",
    draftSourceVersionId: null,
    draftSourceVersion: null,
    latestVersionId: null,
    latestVersion: null,
    publishedVersionId: null,
    publishedVersion: null,
    publicationState: "draft" as const,
    versionCount: 0
  }),
  definition({
    id: "definition-retracted",
    name: "Retracted workflow with a newer saved version",
    description: "Publication was retracted after version 4.0.0; version 5.0.0 remains saved.",
    draftId: null,
    latestVersionId: "version-5",
    latestVersion: "5.0.0",
    publishedVersionId: "version-4",
    publishedVersion: "4.0.0",
    publicationState: "retracted" as const,
    versionCount: 5
  }),
  definition({
    id: "definition-design-only",
    name: "Design only workflow",
    description: "The host does not provide publication lifecycle data.",
    draftId: null,
    latestVersionId: "version-1",
    latestVersion: "1.0.0",
    versionCount: 1
  }),
  definition({
    id: "definition-retired-deleted",
    name: "Retired workflow in the deleted list",
    description: "This row has a different set of row actions.",
    deletedAt: "2026-07-26T00:00:00Z",
    draftId: null,
    latestVersionId: "version-6",
    latestVersion: "6.0.0",
    publishedVersionId: "version-4",
    publishedVersion: "4.0.0",
    publicationState: "retired" as const,
    versionCount: 6
  })
];

export function DefinitionVersionsFixture() {
  const parameters = new URLSearchParams(window.location.search);
  const scenario = parameters.get("scenario");
  writeAppearancePreferences({ themeId: "classic", mode: parameters.get("theme") === "dark" ? "dark" : "light", navigation: "left" });
  setDialogs({ confirm: async () => false, prompt: async () => null, alert: async () => undefined });
  const context = useMemo(() => ({
    baseUrl: "definition-versions-browser",
    http: {
      getJson: async (url: string) => {
        if (url === "/capabilities") return { capabilities: [{
          id: "elsa.api.workflow-design",
          contractVersion: "1",
          links: [{ rel: "workflow-definitions-page", href: "browser/definition-pages" }]
        }] };
        if (url.startsWith("/browser/definition-pages")) {
          if (scenario === "failed") throw new Error("The workflow service is unavailable.");
          if (scenario === "loading") return new Promise(() => undefined);
          const query = new URL(url, window.location.origin).searchParams;
          const state = query.get("state");
          const search = query.get("searchTerm")?.toLowerCase() ?? "";
          const sampleNames = ["Customer quote", "Order total", "Approval workflow", "Customer welcome"];
          const samples = scenario === "showcase"
            ? definitions.slice(0, 4).map((definition, index) => ({ ...definition, name: sampleNames[index], description: ["Look up a customer and prepare a quote", "Calculate the order total", "Review and approve requests", "Welcome a new customer"][index] }))
            : definitions;
          const matching = (scenario === "empty" ? [] : samples).filter(definition => `${definition.name} ${definition.description} ${definition.id}`.toLowerCase().includes(search));
          return {
            items: state === "deleted"
              ? matching.filter(definition => definition.deletedAt)
              : state === "all"
                ? matching
                : matching.filter(definition => !definition.deletedAt),
            nextContinuationToken: null
          };
        }
        throw new Error(`Unexpected definition-versions fixture request: ${url}`);
      }
    }
  }) as unknown as StudioEndpointContext, [scenario]);
  const ai = useMemo(() => ({
    promptActions: { list: () => [] },
    dispatchPrompt: () => undefined
  }) as unknown as StudioAiContributionApi, []);

  const navigation = getStudioNavigation([
    { id: "home", label: "Home", path: "/dashboard", order: 10, placement: "main" },
    { id: "workflows", label: "Workflows", path: "/workflows/definitions", order: 20, placement: "main" },
    { id: "runs", label: "Runs", path: "/workflows/instances", order: 30, placement: "main" }
  ]);
  return <ThemeProvider><AnonymousAuthProvider>
    <ShellFrame navigation={navigation} panels={[]} path="/workflows/definitions" title="Workflows" backendBaseUrl="https://backend.example/" enabledFeatures={["Elsa.Studio.Workflows"]} onNavigate={() => undefined}>
      <WorkflowManagementPage context={context} ai={ai} propertyEditors={[]} expressionEditors={[]} runInputEditors={[]} workflowDesignerPanels={[]} />
    </ShellFrame>
  </AnonymousAuthProvider></ThemeProvider>;
}
