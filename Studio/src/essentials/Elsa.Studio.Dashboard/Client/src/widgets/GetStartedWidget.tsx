import React from "react";
import { Check, Circle } from "lucide-react";
import { requestStudioNavigation, type StudioDashboardWidgetBodyProps } from "@elsa-workflows/studio-sdk";

export interface GetStartedSnapshot {
  definitionExists: boolean;
  runExists: boolean;
  runDetailOpened: boolean;
}

export interface GetStartedStep {
  id: "create" | "run" | "inspect";
  title: string;
  done: boolean;
  href: string;
}

export function getStartedSteps(snapshot: GetStartedSnapshot | undefined): GetStartedStep[] {
  return [
    { id: "create", title: "Create a workflow", done: Boolean(snapshot?.definitionExists), href: "/workflows/definitions" },
    { id: "run", title: "Run it", done: Boolean(snapshot?.runExists), href: "/workflows/instances" },
    { id: "inspect", title: "See what happened", done: Boolean(snapshot?.runDetailOpened), href: "/workflows/instances" }
  ];
}

export function GetStartedWidget({ snapshot }: StudioDashboardWidgetBodyProps<GetStartedSnapshot, unknown>) {
  const steps = getStartedSteps(snapshot);
  const doneCount = steps.filter(step => step.done).length;
  const next = steps.find(step => !step.done);
  const complete = doneCount === steps.length;

  return (
    <div className="home-get-started">
      <p className="home-get-started-progress" aria-live="polite">
        {complete ? "All steps done" : `${doneCount} of ${steps.length} done`}
      </p>
      <ol className="home-get-started-steps">
        {steps.map(step => (
          <li key={step.id} data-done={step.done ? "true" : "false"}>
            <span aria-hidden="true">{step.done ? <Check size={16} /> : <Circle size={16} />}</span>
            <span>{step.title}</span>
            {step.done ? <span className="visually-hidden">Done</span> : null}
          </li>
        ))}
      </ol>
      {next ? (
        <a
          className="home-get-started-cta"
          href={next.href}
          onClick={event => {
            event.preventDefault();
            requestStudioNavigation(window.location.pathname, next.href);
            window.history.pushState({}, "", next.href);
            window.dispatchEvent(new PopStateEvent("popstate"));
          }}
        >
          {next.title}
        </a>
      ) : <p>You have a workflow and a run. Open Home extras from Add widget anytime.</p>}
    </div>
  );
}
