import type { StudioActivityPropertyEditorContribution, StudioDraftValidationIssue, StudioExpressionEditorContribution } from "@elsa-workflows/studio-sdk";
import type { ActivityNode, ValidationError } from "./workflowTypes";

export function contributionDraftValidationCollectors(
  propertyEditors: StudioActivityPropertyEditorContribution[],
  expressionEditors: StudioExpressionEditorContribution[]
): Array<(activity: unknown) => StudioDraftValidationIssue[] | Promise<StudioDraftValidationIssue[]>> {
  return [...propertyEditors, ...expressionEditors]
    .map(editor => editor.collectDraftValidation)
    .filter((collect): collect is NonNullable<typeof collect> => !!collect);
}

export async function collectContributionDraftValidation(
  root: ActivityNode | null | undefined,
  collectors: Array<(activity: unknown) => StudioDraftValidationIssue[] | Promise<StudioDraftValidationIssue[]>>
): Promise<ValidationError[]> {
  if (!root || collectors.length === 0) return [];
  const activities: ActivityNode[] = [];
  walkActivities(root, activity => activities.push(activity));
  const issues = (await Promise.all(activities.flatMap(activity => collectors.map(collect => collect(activity))))).flat();
  return issues.map(issue => ({
    type: issue.type,
    code: issue.type,
    message: issue.message,
    path: issue.path ?? null,
    severity: issue.severity ?? "Error"
  }));
}

export function contributionDraftValidationSignature(root: ActivityNode | null | undefined): string {
  if (!root) return "";
  const parts: string[] = [];
  walkActivities(root, activity => {
    parts.push(
      activity.nodeId,
      JSON.stringify(activity.Decision ?? activity.decision ?? null),
      JSON.stringify(activity.Inputs ?? null)
    );
  });
  return parts.join("\u001f");
}

function walkActivities(root: ActivityNode, visit: (activity: ActivityNode) => void): void {
  const seen = new Set<object>();
  const walk = (node: ActivityNode) => {
    if (seen.has(node)) return;
    seen.add(node);
    visit(node);
    visitNestedValue(node.structure?.payload, child => walk(child));
  };
  walk(root);
}

function visitNestedValue(value: unknown, visit: (activity: ActivityNode) => void): void {
  if (isActivityNode(value)) {
    visit(value);
    return;
  }
  if (Array.isArray(value)) {
    value.forEach(item => visitNestedValue(item, visit));
    return;
  }
  if (value && typeof value === "object") Object.values(value).forEach(item => visitNestedValue(item, visit));
}

function isActivityNode(value: unknown): value is ActivityNode {
  return typeof value === "object" && value !== null
    && typeof (value as ActivityNode).nodeId === "string"
    && typeof (value as ActivityNode).activityVersionId === "string";
}
