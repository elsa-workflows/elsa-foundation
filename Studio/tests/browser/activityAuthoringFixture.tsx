import { useMemo, useRef } from "react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { AnonymousAuthProvider, type StudioEndpointContext } from "@elsa-workflows/studio-sdk";
import { ActivityDefinitionDraftEditor } from "../../src/essentials/Elsa.Studio.Workflows/Client/src/ActivityDefinitionDraftEditor";
import { activityGraphSchema2ImplementationEditorContribution } from "../../src/essentials/Elsa.Studio.Workflows/Client/src/activityGraphContribution";
import { inputReferenceContribution } from "../../src/essentials/Elsa.Studio.Workflows/Client/src/inputReferenceContribution";
import type { ActivityDefinitionDraftView, ReplaceActivityDefinitionDraftRequest } from "../../src/essentials/Elsa.Studio.Workflows/Client/src/activityDefinitionTypes";
import { ThemeProvider } from "../../src/essentials/Elsa.Studio.Hosting/Client/src/app/components/ThemeProvider";
import { writeAppearancePreferences } from "../../src/essentials/Elsa.Studio.Hosting/Client/src/app/themes/appearance";
import { ShellFrame, getStudioNavigation } from "../../src/essentials/Elsa.Studio.Hosting/Client/src/app/App";
import "../../src/essentials/Elsa.Studio.Hosting/Client/src/app/styles.css";

const scenario = new URLSearchParams(location.search);
const outcomeOnly = scenario.get("contract") === "outcome-only";
const storageKey = `activity-authoring-2994-fixture${outcomeOnly ? ":outcome-only" : ""}${scenario.has("read-only") ? ":read-only" : ""}`;
const timestamp = "2026-10-19T09:00:00Z";
const contractType = { alias: "Decimal", collectionKind: "Single", supportsDurability: true };
const input = (name: string, referenceKey: string, description: string) => ({
  name, referenceKey, displayName: name, description, category: null, order: 0, uiHint: null, uiSpecifications: null,
  type: contractType, isRequired: true, isNullable: false, durability: "Required", storageDriverKey: "elsa.json", default: null
});
function initialDraft(): ActivityDefinitionDraftView {
  const subtotal = input("Subtotal", "subtotal", "The order amount before shipping.");
  return {
    draftId: "quote-draft", definitionId: "quote-activity", tenantId: "browser-fixture", revision: 1,
    status: scenario.has("read-only") ? "published" : "active", sourceVersionId: null, presentationLabel: null, createdAt: timestamp, updatedAt: timestamp, validation: null,
    contract: { contractSchemaVersion: "1", inputs: outcomeOnly ? [] : [subtotal, { ...input("Shipping", "shipping", "The shipping charge to include."), isRequired: false, default: { syntax: "Literal", value: 0 } }],
      outputs: outcomeOnly ? [] : [{ ...subtotal, referenceKey: "amount", name: "Amount", displayName: "Amount", description: "The amount returned to the calling workflow." }],
      outcomes: [{ referenceKey: outcomeOnly ? "done" : "quoted", name: outcomeOnly ? "Done" : "Quoted", isEmitted: true, description: "The quote is ready." }] },
    provider: { providerKey: "elsa.activity-graph", schemaVersion: "2", manifestFingerprint: "sha256:quote-fixture-1",
      payload: { rootActivity: { nodeId: "root", activityVersionId: "sequence-v1", inputs: [], outputs: [], structure: { kind: "Sequence", schemaVersion: "1", payload: { activities: [] } } }, variables: [], outputMappings: outcomeOnly ? [] : [{ outputReferenceKey: "amount", source: { syntax: "Input", value: { referenceKey: "subtotal" } } }], outcomeMappings: [{ sourceOutcomeReferenceKey: "done", boundaryOutcomeReferenceKey: outcomeOnly ? "done" : "quoted" }] } }, layout: []
  };
}

export function ActivityAuthoringFixture() {
  const parameters = useMemo(() => new URLSearchParams(window.location.search), []);
  writeAppearancePreferences({ themeId: "classic", mode: parameters.get("theme") === "light" ? "light" : "dark", navigation: "left" });
  const draft = useRef<ActivityDefinitionDraftView>(readDraft());
  const client = useMemo(() => new QueryClient({ defaultOptions: { queries: { retry: false } } }), []);
  const context = useMemo(() => ({
    baseUrl: "activity-authoring-2994",
    http: {
      getJson: async (url: string) => {
        if (url === "/capabilities") return { capabilities: [
          { id: "elsa.api.activity-design", contractVersion: "1", links: [
            ["activity-definition", "definitions/{definitionId}"], ["activity-definition-draft", "drafts/{draftId}"],
            ["activity-authoring-capabilities", "authoring-capabilities"], ["activity-catalog", "catalog"],
            ["activity-draft-validation", "drafts/{draftId}/validate"], ["activity-draft-contract-proposals", "drafts/{draftId}/proposals"]
          ].map(([rel, path]) => ({ rel, href: `fixture/${path}`, templated: path.includes("{") })) },
          { id: "elsa.api.expressions", contractVersion: "1", links: [{ rel: "expression-descriptors", href: "fixture/expressions" }] }
        ] };
        if (url === "/fixture/drafts/quote-draft") return structuredClone(draft.current);
        if (url === "/fixture/definitions/quote-activity") return {
          definition: { definitionId: "quote-activity", activityTypeKey: "elsa.user.quote", tenantId: "browser-fixture", displayName: parameters.get("name") ?? "Prepare order quote", category: "Commerce", description: "A reusable quote activity.", contentAuthority: { kind: "Design", authorityKey: "elsa.activity-design" }, headVersionId: null, recommendedVersionId: null },
          lifecycle: { draftCount: 1, versionCount: 0, head: null, recommendation: null }, actions: [], updatedAt: timestamp
        };
        if (url === "/fixture/authoring-capabilities" && parameters.get("capabilities") === "unavailable") throw new Error("Authoring capabilities unavailable in this fixture scenario");
        if (url === "/fixture/authoring-capabilities") return {
          contractSchemaVersions: ["1"], providers: [{ providerKey: "elsa.activity-graph", displayName: "Designer", manifestSchemas: [{ schemaVersion: "2", isAuthorable: true, migratableFromSchemaVersions: ["1"] }], requiredOutcomes: [] }],
          types: [
            { alias: "Decimal", displayName: "Number", category: "Numbers", defaultEditor: "number", supportsNull: true, supportsDurability: true, supportedCollectionKinds: ["Single", "List"], compatibleStorageDriverKeys: ["elsa.json"] },
            { alias: "String", displayName: "Text", category: "Text", defaultEditor: "text", supportsNull: true, supportsDurability: true, supportedCollectionKinds: ["Single", "List"], compatibleStorageDriverKeys: ["elsa.json"] },
            { alias: "Boolean", displayName: "Yes / No", category: "Common", defaultEditor: "checkbox", supportsNull: false, supportsDurability: true, supportedCollectionKinds: ["Single"], compatibleStorageDriverKeys: ["elsa.json"] }
          ], storageDriverKeys: ["elsa.json"], snapshotFingerprint: "sha256:fixture"
        };
        if (url === "/fixture/expressions") return { items: [{ type: "Input", displayName: "Activity input", editingMode: "reference" }, { type: "Literal", displayName: "Value", editingMode: "literal" }] };
        if (url === "/fixture/catalog") return { activities: [{ activityVersionId: "sequence-v1", activityTypeKey: "Elsa.Sequence", version: "1.0.0", displayName: "Sequence", category: "Composition", executionType: "sync", inputs: [], outputs: [], ports: [{ type: "outcome", referenceKey: "done", displayName: "Done" }], available: true, designFacets: [], authoringTemplate: { nodeId: "template", activityVersionId: "sequence-v1", inputs: [], outputs: [], structure: { kind: "Sequence", schemaVersion: "1", payload: { activities: [] } } } }] };
        throw new Error(`Unexpected fixture request: ${url}`);
      },
      putJson: async (_url: string, request: ReplaceActivityDefinitionDraftRequest) => {
        if (request.expectedRevision !== draft.current.revision) throw new Error("Fixture revision conflict");
        draft.current = { ...draft.current, ...request, revision: draft.current.revision + 1, updatedAt: timestamp, validation: null };
        localStorage.setItem(storageKey, JSON.stringify(draft.current));
        return structuredClone(draft.current);
      },
      postJson: async (url: string) => {
        const current = draft.current;
        if (url.endsWith("/proposals")) return { draftId: current.draftId, revision: current.revision, providerKey: current.provider.providerKey, providerSchemaVersion: current.provider.schemaVersion, manifestFingerprint: current.provider.manifestFingerprint, proposalFingerprint: "sha256:fixture-proposal", changes: [], diagnostics: [] };
        if (url.endsWith("/validate")) return { draftId: current.draftId, revision: current.revision, isValid: true, validatedAt: timestamp, diagnostics: [] };
        throw new Error(`Unexpected fixture mutation: ${url}`);
      }
    }
  }) as unknown as StudioEndpointContext, [parameters]);
  const navigation = getStudioNavigation([{ id: "home", label: "Home", path: "/dashboard", placement: "main" }, { id: "workflows", label: "Workflows", path: "/workflows/definitions", placement: "main" }, { id: "activities", label: "Activity definitions", path: "/workflows/activity-definitions", placement: "main" }]);
  return <ThemeProvider><AnonymousAuthProvider><ShellFrame navigation={navigation} panels={[]} path="/workflows/activity-definitions" title="Activity definitions" backendBaseUrl="https://backend.example" enabledFeatures={["Elsa.Studio.Workflows"]} onNavigate={() => undefined}>
    <QueryClientProvider client={client}><ActivityDefinitionDraftEditor context={context} definitionId="quote-activity" draftId="quote-draft" activityEditors={[activityGraphSchema2ImplementationEditorContribution]} expressionEditors={[inputReferenceContribution]} identity={{ tenantId: "browser-fixture", subject: "browser-author" }} onNavigationGuardChange={() => undefined} onBack={() => undefined} onOpenDraft={() => undefined} onOpenVersion={() => undefined} onOpenStudioPath={() => undefined} /></QueryClientProvider>
  </ShellFrame></AnonymousAuthProvider></ThemeProvider>;
}

function readDraft() {
  try { const stored = localStorage.getItem(storageKey); return stored ? JSON.parse(stored) as ActivityDefinitionDraftView : initialDraft(); } catch { return initialDraft(); }
}
