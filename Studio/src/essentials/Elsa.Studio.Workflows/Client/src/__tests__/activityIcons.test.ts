import { describe, expect, it } from "vitest";
import {
  activityIconMaxLength,
  knownLucideExportNames,
  resolveActivityIcon,
  resolveActivityIconFallback,
  resolveActivityIconTone,
  resolveStudioHostBase,
  toActivityIconAssetUrl
} from "../activityIcons";
import { mappedLucideExportNames } from "../activityIconGlyphs";
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
    expect(resolveActivityIcon(catalog({ iconName: "Rocket" }))).toBe("Rocket");
  });

  it("accepts asset: and ext: catalog values", () => {
    expect(resolveActivityIcon(catalog({ iconName: "asset:Acme.Slack/icons/slack.svg" }))).toBe("asset:Acme.Slack/icons/slack.svg");
    expect(resolveActivityIcon(catalog({ iconName: "ext:slack" }))).toBe("ext:slack");
    expect(toActivityIconAssetUrl("asset:Acme.Slack/icons/slack.svg")).toBe("/_content/Acme.Slack/icons/slack.svg");
  });

  it("builds an asset: URL from the Studio host base path", () => {
    expect(toActivityIconAssetUrl("asset:Acme.Slack/icons/slack.svg", "https://host.example/elsa/studio/workflows"))
      .toBe("/elsa/_content/Acme.Slack/icons/slack.svg");
    expect(toActivityIconAssetUrl("asset:Acme.Slack/icons/slack.svg", "https://host.example/studio/workflows"))
      .toBe("/_content/Acme.Slack/icons/slack.svg");
    expect(resolveStudioHostBase("https://host.example/elsa/studio/workflows")).toBe("https://host.example/elsa/");
  });

  it("rejects an oversize, dot-segment, or parent-segment asset iconName", () => {
    const oversize = `asset:Acme.Slack/${"a".repeat(250)}.svg`;
    expect(oversize.length).toBeGreaterThan(activityIconMaxLength);
    expect(resolveActivityIcon(catalog({
      iconName: oversize,
      activityTypeKey: "Acme.Activities.Custom.UnknownHttpCall",
      displayName: "Unknown HTTP Call",
      category: "HTTP"
    }))).toBe("http");
    expect(toActivityIconAssetUrl(oversize)).toBeNull();
    expect(resolveActivityIcon(catalog({
      iconName: "asset:Acme.Slack/./slack.svg",
      activityTypeKey: "Acme.Activities.Custom.UnknownHttpCall",
      displayName: "Unknown HTTP Call",
      category: "HTTP"
    }))).toBe("http");
    expect(toActivityIconAssetUrl("asset:Acme.Slack/./slack.svg")).toBeNull();
    expect(toActivityIconAssetUrl("asset:Acme.Slack/icons/../secret.svg")).toBeNull();
  });

  it("ignores an unsafe or malformed iconName and continues to type and category fallbacks", () => {
    expect(resolveActivityIcon(catalog({
      iconName: "asset:../secrets/key.svg",
      activityTypeKey: "Elsa.Activities.Email.Activities.SendEmail",
      displayName: "Send Email",
      category: "Email"
    }))).toBe("email");
    expect(resolveActivityIcon(catalog({
      icon: "javascript:alert(1)",
      activityTypeKey: "Acme.Activities.Custom.UnknownHttpCall",
      displayName: "Unknown HTTP Call",
      category: "HTTP"
    }))).toBe("http");
    expect(resolveActivityIcon(catalog({
      iconName: "not a name",
      activityTypeKey: "Acme.Activities.Custom.OddThing",
      displayName: "Odd Thing",
      category: "Uncategorised",
      executionType: "Trigger"
    }))).toBe("trigger");
    expect(resolveActivityIconFallback(catalog({
      iconName: "Rocket",
      activityTypeKey: "Acme.Activities.Custom.OddThing",
      displayName: "Odd Thing",
      category: "Uncategorised"
    }))).toBe("activity");
  });

  it("keeps the resolver lucide map in lockstep with the renderer", () => {
    expect([...mappedLucideExportNames].sort()).toEqual([...knownLucideExportNames].sort());
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
