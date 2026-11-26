import React, { useMemo, useState } from "react";
import { ReactFlowProvider } from "@xyflow/react";
import type { NodeProps } from "@xyflow/react";
import { ActivityPalettePanel } from "../../src/essentials/Elsa.Studio.Workflows/Client/src/workflow-editor/ActivityPalettePanel";
import { WorkflowActivityNode } from "../../src/essentials/Elsa.Studio.Workflows/Client/src/workflow-editor/graph";
import { groupActivityPalette } from "../../src/essentials/Elsa.Studio.Workflows/Client/src/workflow-editor/editorHelpers";
import { resolveActivityIcon, resolveActivityIconTone } from "../../src/essentials/Elsa.Studio.Workflows/Client/src/activityIcons";
import type { WorkflowNodeData } from "../../src/essentials/Elsa.Studio.Workflows/Client/src/workflowAdapter";
import type { ActivityCatalogItem } from "../../src/essentials/Elsa.Studio.Workflows/Client/src/workflowTypes";

const omitCustomIcons = new URLSearchParams(window.location.search).get("legacy") === "1";

const catalog: ActivityCatalogItem[] = [
  item("notify-slack", "Acme.Activities.Slack.NotifySlack", "Notify Slack", "Acme", "Posts a message to Slack.", "Action", omitCustomIcons ? undefined : "Rocket"),
  item("write-line", "Elsa.Activities.Primitives.Activities.WriteLine", "Write Line", "Primitives", "Writes text to the console."),
  item("fault", "Elsa.Activities.Primitives.Activities.Fault", "Fault", "Primitives", "Faults the workflow."),
  item("if", "Elsa.Activities.ControlFlow.Activities.If", "If", "Control Flow", "Branches on a condition."),
  item("for-each", "Elsa.Activities.ControlFlow.Activities.ForEach", "For Each", "Control Flow", "Iterates a collection."),
  item("switch", "Elsa.Activities.ControlFlow.Activities.Switch", "Switch", "Control Flow", "Selects one case."),
  item("while", "Elsa.Activities.ControlFlow.Activities.While", "While", "Control Flow", "Loops while a condition holds."),
  item("http-endpoint", "Elsa.Activities.Http.Activities.HttpEndpoint", "HTTP Endpoint", "HTTP", "Starts from an HTTP request.", "Trigger"),
  item("send-http", "Elsa.Activities.Http.Activities.SendHttpRequest", "Send HTTP Request", "HTTP", "Sends an HTTP request."),
  item("timer", "Elsa.Activities.Scheduling.Activities.Timer", "Timer", "Scheduling", "Waits for a duration."),
  item("run-js", "Elsa.Activities.Scripting.Activities.RunJavaScript", "Run JavaScript", "Scripting", "Runs a JavaScript snippet."),
  item("flowchart", "Elsa.Activities.Flowchart.Activities.Flowchart", "Flowchart", "Composition", "A flowchart container."),
  item("sequence", "Elsa.Activities.Sequence.Activities.Sequence", "Sequence", "Composition", "An ordered sequence."),
  item("bpmn", "Elsa.Activities.Bpmn.Activities.BpmnProcess", "BPMN Process", "BPMN", "A BPMN process."),
  item("email", "Elsa.Activities.Email.Activities.SendEmail", "Send Email", "Email", "Sends an email.")
];

const canvasActivities = catalog.filter(activity =>
  ["Notify Slack", "Write Line", "If", "For Each", "Fault", "HTTP Endpoint", "Timer", "Run JavaScript"].includes(activity.displayName)
);

export function ActivityIconsFixture() {
  const [search, setSearch] = useState("");
  const [expanded, setExpanded] = useState(() => new Set(catalog.map(activity => activity.category)));
  const filtered = useMemo(() => {
    const term = search.trim().toLowerCase();
    if (!term) return catalog;
    return catalog.filter(activity =>
      activity.displayName.toLowerCase().includes(term) ||
      activity.activityTypeKey.toLowerCase().includes(term) ||
      activity.category.toLowerCase().includes(term));
  }, [search]);
  const groups = useMemo(() => groupActivityPalette(filtered), [filtered]);

  return (
    <main className="wf-editor wf-page activity-icons-fixture" data-testid="activity-icons-fixture">
      <header className="wf-page-header">
        <div>
          <span className="wf-kicker">Designer</span>
          <h1>Activity icons</h1>
        </div>
      </header>
      <div className="activity-icons-workbench">
        <aside className="wf-palette" aria-label="Activity palette">
          <ActivityPalettePanel
            paletteSearch={search}
            onSearchChange={setSearch}
            groups={groups}
            expandedCategories={expanded}
            onToggleCategory={category => setExpanded(current => {
              const next = new Set(current);
              if (next.has(category)) next.delete(category);
              else next.add(category);
              return next;
            })}
            onActivityClick={() => undefined}
            onActivityDragStart={() => undefined}
            onActivityDragEnd={() => undefined}
            onActivityPointerDown={() => undefined}
          />
        </aside>
        <section className="wf-canvas-shell" aria-label="Workflow canvas">
          <div className="wf-canvas activity-icons-canvas">
            <ReactFlowProvider>
              {canvasActivities.map((activity, index) => {
                const data: WorkflowNodeData = {
                  label: activity.displayName,
                  activityVersionId: activity.activityVersionId,
                  activityTypeKey: activity.activityTypeKey,
                  category: activity.category,
                  executionType: activity.executionType,
                  description: activity.description ?? undefined,
                  icon: resolveActivityIcon(activity),
                  iconTone: resolveActivityIconTone(activity),
                  childSlots: [],
                  acceptsInbound: true,
                  sourcePorts: [{ name: "Done", displayName: "Done" }]
                };
                return (
                  <div
                    key={activity.activityVersionId}
                    className="activity-icons-node"
                    style={{ left: 36 + (index % 4) * 280, top: 36 + Math.floor(index / 4) * 130 }}
                  >
                    <WorkflowActivityNode {...({ id: activity.activityVersionId, data, selected: index === 0 } as unknown as NodeProps)} />
                  </div>
                );
              })}
            </ReactFlowProvider>
          </div>
        </section>
      </div>
    </main>
  );
}

function item(
  id: string,
  activityTypeKey: string,
  displayName: string,
  category: string,
  description: string,
  executionType = "Action",
  iconName?: string
): ActivityCatalogItem {
  return {
    activityVersionId: `${id}-v1`,
    activityTypeKey,
    version: "1.0.0",
    category,
    displayName,
    description,
    executionType,
    iconName,
    inputs: [],
    outputs: [],
    designFacets: [],
    available: true
  };
}
