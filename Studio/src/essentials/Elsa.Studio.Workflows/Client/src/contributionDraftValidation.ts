import type {
  StudioActivityInputDescriptor,
  StudioActivityPropertyEditorContribution,
  StudioDraftValidationContext,
  StudioDraftValidationIssue,
  StudioExpressionEditorContribution
} from "@elsa-workflows/studio-sdk";
import type { ActivityCatalogItem, ActivityNode, ValidationError } from "./workflowTypes";

export type DraftValidationCollectFn = (
  activity: unknown,
  context?: StudioDraftValidationContext
) => StudioDraftValidationIssue[] | Promise<StudioDraftValidationIssue[]>;

export type DraftValidationCollector = {
  collect: DraftValidationCollectFn;
  supportsProperty?: StudioActivityPropertyEditorContribution["supports"];
  supportsExpression?: StudioExpressionEditorContribution["supports"];
};

type CollectorSource = DraftValidationCollector | DraftValidationCollectFn;

export function contributionDraftValidationCollectors(
  propertyEditors: StudioActivityPropertyEditorContribution[],
  expressionEditors: StudioExpressionEditorContribution[]
): DraftValidationCollector[] {
  return [
    ...propertyEditors.flatMap(editor => editor.collectDraftValidation
      ? [{ collect: editor.collectDraftValidation, supportsProperty: editor.supports }]
      : []),
    ...expressionEditors.flatMap(editor => editor.collectDraftValidation
      ? [{ collect: editor.collectDraftValidation, supportsExpression: editor.supports }]
      : [])
  ];
}

export async function collectContributionDraftValidation(
  root: ActivityNode | null | undefined,
  collectors: CollectorSource[],
  catalogByVersion?: Map<string, ActivityCatalogItem>
): Promise<ValidationError[]> {
  if (!root || collectors.length === 0) return [];
  const normalized = collectors.map(item => typeof item === "function" ? { collect: item } : item);
  const activities: ActivityNode[] = [];
  walkActivities(root, activity => activities.push(activity));
  const issues = (await Promise.all(activities.flatMap(activity => {
    const context = activityDraftValidationContext(activity, catalogByVersion);
    return normalized
      .filter(collector => collectorApplies(collector, activity, context))
      .map(collector => collector.collect(activity, context));
  }))).flat();
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
      JSON.stringify(activity.Inputs ?? activity.inputs ?? null)
    );
  });
  return parts.join("\u001f");
}

export function activityDraftValidationContext(
  activity: ActivityNode,
  catalogByVersion?: Map<string, ActivityCatalogItem>
): StudioDraftValidationContext {
  const item = catalogByVersion?.get(activity.activityVersionId);
  return {
    typeName: item?.activityTypeKey || readString(activity.activityTypeKey) || readString(activity.activityType) || readString(activity.typeName) || null,
    inputs: catalogInputDescriptors(item)
  };
}

function collectorApplies(collector: DraftValidationCollector, activity: ActivityNode, context: StudioDraftValidationContext): boolean {
  const inputs = context.inputs ?? [];
  if (collector.supportsProperty) {
    if (inputs.length) {
      const propertyContext = { activity, activityId: activity.nodeId, expressionDescriptors: [], readOnly: true as const };
      return inputs.some(input => collector.supportsProperty!(input, propertyContext));
    }
    return !!context.typeName;
  }
  if (collector.supportsExpression) {
    if (inputs.length) {
      return inputs.some(input => ["Literal", "Object"].some(syntax =>
        collector.supportsExpression!({ syntax, surface: "inline", descriptor: input, activity, expressionDescriptors: [] })));
    }
    return !!context.typeName;
  }
  return true;
}

function catalogInputDescriptors(item: ActivityCatalogItem | undefined): StudioActivityInputDescriptor[] {
  if (!item) return [];
  return item.inputs.flatMap(input => {
    if (!input || typeof input !== "object") return [];
    const record = input as Record<string, unknown>;
    const name = readString(record.referenceKey) || readString(record.name);
    if (!name) return [];
    return [{
      name: readString(record.name) || name,
      referenceKey: name,
      displayName: readString(record.displayName),
      typeName: readString(record.typeName) || readString(record.type) || "",
      uiHint: readString(record.uiHint)
    }];
  });
}

function readString(value: unknown): string {
  return typeof value === "string" ? value : "";
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
