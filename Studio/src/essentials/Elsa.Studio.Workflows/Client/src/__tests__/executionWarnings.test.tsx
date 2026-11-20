import { afterEach, beforeEach, describe, expect, it } from "vitest";
import { flushSync } from "react-dom";
import { createRoot, type Root } from "react-dom/client";
import { ExecutionWarningBadge, ExecutionWarningsSection, warningBadgeLabel, warningSourceLabel } from "../workflow-editor/ExecutionWarnings";
import type { ExecutionWarningView } from "../workflowTypes";

// Spec 1228 (S-8): the shared run-time warning channel in the run view.

let container: HTMLDivElement;
let root: Root;

beforeEach(() => {
  container = document.createElement("div");
  document.body.appendChild(container);
  root = createRoot(container);
});

afterEach(() => {
  flushSync(() => root.unmount());
  container.remove();
});

function warning(overrides: Partial<ExecutionWarningView> = {}): ExecutionWarningView {
  return {
    code: "VF-PREC-002",
    severity: "Warning",
    message: "The script read numbers a JavaScript number cannot hold exactly (integers beyond 9007199254740991) and computed with the nearest value it holds: variables.order.id.",
    source: "input",
    inputKey: "arguments",
    executableNodeId: "node-js",
    activityExecutionId: "actexec-js",
    details: { paths: "variables.order.id" },
    ...overrides
  };
}

describe("execution warnings", () => {
  it("names where a warning came from", () => {
    expect(warningSourceLabel(warning())).toBe('Input "arguments"');
    expect(warningSourceLabel(warning({ inputKey: null }))).toBe("An input");
    expect(warningSourceLabel(warning({ source: "step", inputKey: null }))).toBe("The activity");
    expect(warningBadgeLabel(1)).toBe("1 warning");
    expect(warningBadgeLabel(3)).toBe("3 warnings");
  });

  it("lists the input expression's and the activity's warnings with their code, message and paths", () => {
    flushSync(() => root.render(<ExecutionWarningsSection warnings={[
      warning(),
      warning({ source: "step", inputKey: null, details: { paths: "args.order.id" } })
    ]} />));

    const section = container.querySelector('[data-testid="execution-warnings"]')!;
    expect(section.querySelector("h4")?.textContent).toContain("Warnings");
    expect(section.querySelector('[data-testid="execution-warning-badge"]')?.textContent).toContain("2 warnings");
    const items = [...section.querySelectorAll('[role="listitem"]')];
    expect(items).toHaveLength(2);
    expect(items[0].textContent).toContain('Input "arguments"');
    expect(items[0].textContent).toContain("VF-PREC-002");
    expect(items[0].textContent).toContain("variables.order.id");
    expect(items[1].textContent).toContain("The activity");
    expect(items[1].querySelector("dt")?.textContent).toBe("Paths");
    expect(items[1].querySelector("code")?.textContent).toBe("args.order.id");
  });

  it("renders nothing when the execution raised no warning or the host does not report them", () => {
    flushSync(() => root.render(<><ExecutionWarningsSection warnings={[]} /><ExecutionWarningsSection warnings={undefined} /><ExecutionWarningBadge warnings={null} /></>));

    expect(container.innerHTML).toBe("");
  });
});
