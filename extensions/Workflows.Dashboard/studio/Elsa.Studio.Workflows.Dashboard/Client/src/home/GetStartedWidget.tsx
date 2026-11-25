import React from "react";
import { ArrowUpRight, BookOpen, Check } from "lucide-react";
import { type StudioDashboardWidgetBodyProps } from "@elsa-workflows/studio-sdk";
import { navigateStudioPath } from "./homeData";
import { getStartedSteps, type GetStartedSnapshot } from "./getStartedSteps";

export type { GetStartedSnapshot } from "./getStartedSteps";

export const homeDocsLinks = [
  { title: "Try Elsa locally", href: "https://v4.elsaworkflows.io/essentials/quickstart" },
  { title: "Your first workflow", href: "https://v4.elsaworkflows.io/essentials/first-workflow" },
  { title: "Elsa Studio", href: "https://v4.elsaworkflows.io/studio" }
] as const;

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
      <progress className="home-get-started-meter" aria-label="Getting started progress" value={doneCount} max={steps.length} />
      <ol className="home-get-started-steps">
        {steps.map((step, index) => (
          <li key={step.id} data-done={step.done ? "true" : "false"}>
            <span aria-hidden="true">{step.done ? <Check size={16} /> : index + 1}</span>
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
            navigateStudioPath(next.href);
          }}
        >
          {next.title}
        </a>
      ) : <p>You have a workflow and a run. Open Home extras from Add widget anytime.</p>}
      <section className="home-docs" aria-labelledby="home-docs-title">
        <h4 id="home-docs-title">Documentation</h4>
        <p>{snapshot?.definitionExists
          ? "Guides for Studio and your next workflow."
          : "New to Elsa? These guides cover a first install and your first workflow."}</p>
        <ul>
          {homeDocsLinks.map(link => (
            <li key={link.href}>
              <a href={link.href} target="_blank" rel="noreferrer"><BookOpen size={16} aria-hidden="true" /><span>{link.title}</span><ArrowUpRight size={15} aria-hidden="true" /></a>
            </li>
          ))}
        </ul>
      </section>
    </div>
  );
}
