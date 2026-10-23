import type { StudioActivityDiagnosticFocusResult, StudioActivityDiagnosticLocation } from "@elsa-workflows/studio-sdk";

export const activityGraphDiagnosticFocusEvent = "elsa:activity-graph:focus-diagnostic-location";

export interface ActivityGraphDiagnosticFocusEventDetail {
  location: StudioActivityDiagnosticLocation;
  complete(result: StudioActivityDiagnosticFocusResult): void;
}
