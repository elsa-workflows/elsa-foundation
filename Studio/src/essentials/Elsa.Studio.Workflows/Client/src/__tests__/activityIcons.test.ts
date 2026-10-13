import { describe, expect, it } from "vitest";
import { resolveActivityIcon, resolveActivityIconTone } from "../activityIcons";
import type { ActivityCatalogItem } from "../workflowTypes";
import { writeLine } from "./fixtures";

function catalog(overrides: Partial<ActivityCatalogItem>): ActivityCatalogItem {
  return { ...writeLine, ...overrides };
}

describe("resolveActivityIcon", () => {
  it("prefers an explicit kind token on catalog metadata", () => {
    expect(resolveActivityIcon(catalog({ icon: "trigger" }))).toBe("trigger");
  });

  it("renders a descriptor-supplied lucide iconName instead of the generic Boxes kind", () => {
    expect(resolveActivityIcon(catalog({ iconName: "GitBranch" }))).toBe("GitBranch");
    expect(resolveActivityIcon(catalog({ iconName: "git-branch" }))).toBe("GitBranch");
  });

  it("maps well-known activity types to distinct icons", () => {
    expect(resolveActivityIcon(writeLine)).toBe("terminal");
    expect(resolveActivityIcon(catalog({
      activityTypeKey: "Elsa.Activities.ControlFlow.Activities.If",
      displayName: "If",
      category: "Control Flow"
    }))).toBe("branch");
    expect(resolveActivityIcon(catalog({
      activityTypeKey: "Elsa.Activities.ControlFlow.Activities.ForEach",
      displayName: "For Each",
      category: "Control Flow"
    }))).toBe("loop");
    expect(resolveActivityIcon(catalog({
      activityTypeKey: "Elsa.Activities.ControlFlow.Activities.Switch",
      displayName: "Switch",
      category: "Control Flow"
    }))).toBe("fork");
    expect(resolveActivityIcon(catalog({
      activityTypeKey: "Elsa.Activities.Primitives.Activities.Fault",
      displayName: "Fault",
      category: "Primitives"
    }))).toBe("fault");
    expect(resolveActivityIcon(catalog({
      activityTypeKey: "Elsa.Activities.Http.Activities.HttpEndpoint",
      displayName: "HTTP Endpoint",
      category: "HTTP",
      executionType: "Trigger"
    }))).toBe("http");
    expect(resolveActivityIcon(catalog({
      activityTypeKey: "Elsa.Activities.Scheduling.Activities.Timer",
      displayName: "Timer",
      category: "Scheduling"
    }))).toBe("timer");
    expect(resolveActivityIcon(catalog({
      activityTypeKey: "Elsa.Activities.Scripting.Activities.RunJavaScript",
      displayName: "Run JavaScript",
      category: "Scripting"
    }))).toBe("scripting");
  });

  it("falls back to the category icon when the activity has no type icon of its own", () => {
    expect(resolveActivityIcon(catalog({
      activityTypeKey: "Acme.Activities.Custom.UnknownHttpCall",
      displayName: "Unknown HTTP Call",
      category: "HTTP"
    }))).toBe("http");
    expect(resolveActivityIcon(catalog({
      activityTypeKey: "Acme.Activities.Custom.OddPrimitive",
      displayName: "Odd Primitive",
      category: "Primitives"
    }))).toBe("variable");
  });

  it("keeps reusable activities on the reusable icon", () => {
    expect(resolveActivityIcon(catalog({
      activityDefinitionId: "invoice",
      displayName: "Invoice"
    }))).toBe("reusable");
  });
});

describe("resolveActivityIconTone", () => {
  it("colours icons by category through studio semantic tones", () => {
    expect(resolveActivityIconTone(writeLine)).toBe("terminal");
    expect(resolveActivityIconTone(catalog({ category: "Control Flow" }))).toBe("composite");
    expect(resolveActivityIconTone(catalog({ category: "Scripting" }))).toBe("success");
    expect(resolveActivityIconTone(catalog({ category: "Scheduling" }))).toBe("runtime");
    expect(resolveActivityIconTone(catalog({ category: "HTTP" }))).toBe("composite");
  });

  it("uses danger for Fault and trigger for trigger activities", () => {
    expect(resolveActivityIconTone(catalog({
      activityTypeKey: "Elsa.Activities.Primitives.Activities.Fault",
      displayName: "Fault",
      category: "Primitives"
    }))).toBe("danger");
    expect(resolveActivityIconTone(catalog({
      executionType: "Trigger",
      category: "HTTP"
    }))).toBe("trigger");
  });
});
