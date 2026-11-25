/** The three Get started steps, kept apart from the widget body so registration stays light. */
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
