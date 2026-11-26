import { Plus } from "lucide-react";
import type { StudioThemeLayout } from "@elsa-workflows/studio-sdk";

/**
 * Whether a graph scope authors inline (Add step + connection "+") instead of from the activity list.
 * Only the editorial layout does, and never in BPMN, whose events and gateways come from its shape palette.
 */
export function authorsInline(layout: StudioThemeLayout, isBpmnScope: boolean) {
  return layout === "editorial" && !isBpmnScope;
}

/**
 * The editorial layout's inline authoring entry point: a pill at the foot of the canvas that opens the
 * activity picker next to itself. It replaces reaching for a toolbox — together with the always-visible
 * "+" on every connection — so steps are added where the flow is being read.
 */
export function AddStepButton({ disabled, onOpen }: {
  disabled?: boolean;
  onOpen(anchor: { clientX: number; clientY: number }): void;
}) {
  return (
    <button
      type="button"
      className="wf-add-step"
      disabled={disabled}
      onClick={event => {
        const rect = event.currentTarget.getBoundingClientRect();
        onOpen({ clientX: rect.left + rect.width / 2, clientY: rect.top });
      }}
    >
      <Plus size={15} aria-hidden="true" /> Add step
    </button>
  );
}
