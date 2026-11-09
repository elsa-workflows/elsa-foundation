import React from "react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { createEndpointContext } from "@elsa-workflows/studio-sdk";
import { ActivityAvailabilityPage } from "../../src/essentials/Elsa.Studio.Workflows/Client/src/ActivityAvailabilityPage";
import { activityDesignKeys } from "../../src/essentials/Elsa.Studio.Workflows/Client/src/api/activityDesign";
import { writeLine } from "../../src/essentials/Elsa.Studio.Workflows/Client/src/__tests__/fixtures";
import type { ActivityAvailabilityDiagnosticEntry } from "../../src/essentials/Elsa.Studio.Workflows/Client/src/workflowTypes";

const parameters = new URLSearchParams(location.search);
const items: ActivityAvailabilityDiagnosticEntry[] = Array.from({ length: parameters.has("short") ? 3 : 96 }, (_, index) => ({
  activityDefinitionId: `definition-${index + 1}`,
  activityTypeKey: `Fixture.Activity${index + 1}`,
  displayName: `Fixture activity ${String(index + 1).padStart(3, "0")}`,
  description: index === 1 ? "Activity details remain independently scrollable in a constrained workspace. ".repeat(40) : "Fixture activity.",
  category: "Primitives",
  state: index === 0 ? "BlockedByHostBaseline" : "Available",
  layer: index === 0 ? "HostBaseline" : "Catalog",
  referenceKind: "ActivityType",
  reason: index === 0 ? "Blocked by host policy." : "Available in new workflows."
}));
const extras = parameters.has("extras");
const client = new QueryClient({ defaultOptions: { queries: { retry: false, staleTime: Infinity } } });
client.setQueryData(activityDesignKeys.availabilitySettings, { scope: "host-default", mode: "AllExcept", rules: { activityTypes: [], sets: [] } });
client.setQueryData(activityDesignKeys.availabilityDiagnostics, {
  items: extras ? [...items, { state: "UnresolvedReference", layer: "ManagementSettings", referenceKind: "ActivitySet", referenceName: "Unknown set", reason: "Set not found." }] : items,
  sets: extras ? [{ name: "Core", activityTypeKeys: ["Fixture.Activity2"] }] : []
});
client.setQueryData(activityDesignKeys.catalog, items.map(item => ({ ...writeLine,
  activityVersionId: `${item.activityDefinitionId}-v1`, activityTypeKey: item.activityTypeKey, displayName: item.displayName
})));
const context = createEndpointContext("https://backend.example/");

export function ActivityAvailabilityFixture() {
  return <QueryClientProvider client={client}><ActivityAvailabilityPage context={context} /></QueryClientProvider>;
}
