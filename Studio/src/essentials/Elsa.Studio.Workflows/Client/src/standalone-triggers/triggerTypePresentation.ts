import { CalendarClock, Globe, Radio, Timer, Zap } from "lucide-react";
import type {
  StudioTriggerConfigurationEditorContribution,
  StudioTriggerTypeAccent,
  StudioTriggerTypeIconComponent,
  StudioTriggerTypePresentationContribution,
  StudioTriggerTypePresentationMetadata
} from "@elsa-workflows/studio-sdk";
import type { StudioStatusTone } from "@elsa-workflows/studio-ui";
import type { TriggerTypeDescriptor } from "./types";

export interface ResolvedTriggerTypePresentation {
  displayName: string;
  description?: string;
  icon: StudioTriggerTypeIconComponent;
  accent: StudioTriggerTypeAccent;
  category?: string;
  order: number;
}

const DEFAULT_ORDER = 500;

export const builtInTriggerTypePresentations: readonly StudioTriggerTypePresentationContribution[] = [
  {
    id: "elsa.triggers.presentation.http",
    family: "Http",
    description: "Start workflows from an incoming HTTP request.",
    icon: Globe,
    accent: "blue",
    category: "HTTP",
    order: 10
  },
  {
    id: "elsa.triggers.presentation.event",
    family: "Event",
    description: "React to domain or integration events published at runtime.",
    icon: Radio,
    accent: "amber",
    category: "Event",
    order: 20
  },
  {
    id: "elsa.triggers.presentation.timer",
    family: "Timer",
    description: "Trigger workflows repeatedly on a fixed interval.",
    icon: Timer,
    accent: "teal",
    category: "Schedule",
    order: 30
  },
  {
    id: "elsa.triggers.presentation.cron",
    family: "Cron",
    description: "Run workflows on a calendar or cron schedule.",
    icon: CalendarClock,
    accent: "violet",
    category: "Schedule",
    order: 40
  }
];

function normalized(value?: string | null) {
  return value?.trim().toLowerCase() ?? "";
}

function builtInFallbackFor(typeKey: string, family?: string): StudioTriggerTypePresentationMetadata | undefined {
  const familyKey = normalized(family);
  const key = normalized(typeKey);
  return builtInTriggerTypePresentations.find(item => {
    const candidate = normalized(item.family);
    return (familyKey && candidate === familyKey) || (key && (key === candidate || key.endsWith(`.${candidate}`)));
  });
}

function mergeMetadata(
  layers: readonly (StudioTriggerTypePresentationMetadata | undefined)[],
  fallbackName: string,
  fallbackDescription?: string,
  fallbackCategory?: string
): ResolvedTriggerTypePresentation {
  let displayName: string | undefined;
  let description: string | undefined;
  let icon: StudioTriggerTypeIconComponent | undefined;
  let accent: StudioTriggerTypeAccent | undefined;
  let category: string | undefined;
  let order: number | undefined;

  for (const layer of layers) {
    if (!layer) continue;
    displayName ??= layer.displayName;
    description ??= layer.description;
    icon ??= layer.icon;
    accent ??= layer.accent;
    category ??= layer.category;
    order ??= layer.order;
  }

  return {
    displayName: displayName || fallbackName,
    description: description || fallbackDescription,
    icon: icon ?? Zap,
    accent: accent ?? "neutral",
    category: category ?? fallbackCategory,
    order: order ?? DEFAULT_ORDER
  };
}

export function resolveTriggerTypePresentation(
  descriptor: Pick<TriggerTypeDescriptor, "typeKey" | "schemaVersion" | "displayName" | "description" | "family" | "configurationEditorId">,
  presentations: readonly StudioTriggerTypePresentationContribution[] = [],
  editors: readonly StudioTriggerConfigurationEditorContribution[] = []
): ResolvedTriggerTypePresentation {
  const typeKey = normalized(descriptor.typeKey);
  const family = normalized(descriptor.family);

  const exactSchemaMatch = presentations.find(
    item => item.typeKey && normalized(item.typeKey) === typeKey && item.schemaVersion === descriptor.schemaVersion
  );
  const typeMatch = presentations.find(
    item => item.typeKey && normalized(item.typeKey) === typeKey && !item.schemaVersion
  );
  const editorMatch = editors.find(
    editor =>
      normalized(editor.typeKey) === typeKey
      && editor.schemaVersion === descriptor.schemaVersion
      && (!descriptor.configurationEditorId || editor.id === descriptor.configurationEditorId)
      && Boolean(editor.presentation)
  )?.presentation;
  const familyMatch = presentations.find(
    item => !item.typeKey && item.family && normalized(item.family) === family
  );
  const builtIn = builtInFallbackFor(descriptor.typeKey, descriptor.family);

  // Explicit type-level module contributions override backend descriptions; family/built-in defaults only fill gaps.
  return mergeMetadata(
    [
      exactSchemaMatch,
      typeMatch,
      editorMatch,
      descriptor.description ? { description: descriptor.description } : undefined,
      familyMatch,
      builtIn
    ],
    descriptor.displayName || descriptor.typeKey,
    descriptor.description,
    descriptor.family || undefined
  );
}

export function resolveTriggerTypeSummary(
  typeKey: string,
  schemaVersion: string,
  descriptors: readonly TriggerTypeDescriptor[] = [],
  presentations: readonly StudioTriggerTypePresentationContribution[] = [],
  editors: readonly StudioTriggerConfigurationEditorContribution[] = []
): ResolvedTriggerTypePresentation {
  const descriptor = descriptors.find(
    item => item.typeKey === typeKey && item.schemaVersion === schemaVersion
  ) ?? descriptors.find(item => item.typeKey === typeKey);

  if (descriptor) {
    return resolveTriggerTypePresentation(descriptor, presentations, editors);
  }

  return resolveTriggerTypePresentation(
    { typeKey, schemaVersion, displayName: typeKey, family: "" },
    presentations,
    editors
  );
}

export function sortTriggerTypes(
  descriptors: readonly TriggerTypeDescriptor[],
  presentations: readonly StudioTriggerTypePresentationContribution[] = [],
  editors: readonly StudioTriggerConfigurationEditorContribution[] = []
): TriggerTypeDescriptor[] {
  return [...descriptors]
    .map(descriptor => ({
      descriptor,
      presentation: resolveTriggerTypePresentation(descriptor, presentations, editors)
    }))
    .sort((left, right) => {
      if (left.presentation.order !== right.presentation.order) {
        return left.presentation.order - right.presentation.order;
      }
      const byName = left.presentation.displayName.localeCompare(right.presentation.displayName, undefined, { sensitivity: "base" });
      if (byName !== 0) return byName;
      const byKey = left.descriptor.typeKey.localeCompare(right.descriptor.typeKey);
      if (byKey !== 0) return byKey;
      return left.descriptor.schemaVersion.localeCompare(right.descriptor.schemaVersion, undefined, { numeric: true });
    })
    .map(entry => entry.descriptor);
}

export function triggerStateTone(state: string): StudioStatusTone {
  switch (state) {
    case "Enabled":
      return "success";
    case "EnabledUnavailable":
    case "Unavailable":
      return "warning";
    case "Deleted":
      return "danger";
    default:
      return "neutral";
  }
}

export function subscriptionCompatibilityTone(compatibility: string): StudioStatusTone {
  switch (compatibility) {
    case "Valid":
      return "success";
    case "ValidationPending":
      return "warning";
    case "Blocked":
      return "danger";
    default:
      return "neutral";
  }
}

export function invocationGrantTone(state: string): StudioStatusTone {
  switch (state) {
    case "Granted":
      return "success";
    case "Revoked":
      return "danger";
    case "Unavailable":
      return "warning";
    default:
      return "neutral";
  }
}

export function targetStateTone(state: string): StudioStatusTone {
  switch (state) {
    case "Available":
      return "success";
    case "Unavailable":
      return "warning";
    default:
      return "neutral";
  }
}

export function reservationStateTone(state: string): StudioStatusTone {
  switch (state) {
    case "Reserved":
      return "success";
    case "Conflict":
      return "danger";
    case "Unavailable":
      return "warning";
    default:
      return "neutral";
  }
}

export function deliveryAdmissionTone(admission: string): StudioStatusTone {
  switch (admission) {
    case "Accepted":
      return "success";
    case "PartiallyAccepted":
      return "warning";
    case "Refused":
    case "NotAcceptedDueToPolicy":
      return "danger";
    default:
      return "neutral";
  }
}

export function recipientDispatchTone(dispatch: string): StudioStatusTone {
  switch (dispatch) {
    case "Started":
    case "Queued":
      return "success";
    case "DispatchCommitted":
    case "AdmissionUnknown":
      return "warning";
    case "Failed":
      return "danger";
    default:
      return "neutral";
  }
}

export function transportStateTone(state: string): StudioStatusTone {
  switch (state) {
    case "WriteCompleted":
      return "success";
    case "AwaitingResponse":
      return "accent";
    case "ClientDisconnected":
    case "TimedOut":
    case "WriteFailed":
    case "ReceiptUnknown":
      return "warning";
    default:
      return "neutral";
  }
}

export function mappingPreviewFieldTone(state: string): StudioStatusTone {
  switch (state) {
    case "Mapped":
      return "success";
    case "Defaulted":
      return "accent";
    case "Invalid":
      return "danger";
    case "Redacted":
      return "warning";
    default:
      return "neutral";
  }
}
