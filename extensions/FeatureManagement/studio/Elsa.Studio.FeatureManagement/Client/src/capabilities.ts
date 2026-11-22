export interface CapabilityFeature {
  categories: string[];
  enabled: boolean;
  pending?: boolean;
  problem?: boolean;
}

export interface CapabilityGroup<T extends CapabilityFeature = CapabilityFeature> {
  id: string;
  label: string;
  description: string;
  features: T[];
  enabledCount: number;
  pendingCount: number;
  problemCount: number;
}

const capabilityDefinitions = [
  {
    id: "bpmn",
    label: "BPMN",
    description: "Author, execute, and exchange BPMN processes.",
    categories: ["bpmn"]
  },
  {
    id: "workflow-activities",
    label: "Workflow activities",
    description: "Control flow, composition, and reusable workflow behavior.",
    categories: ["workflow activities", "workflows", "activities"]
  },
  {
    id: "persistence",
    label: "Persistence",
    description: "Storage providers and persistence capabilities.",
    categories: ["persistence", "persistence providers", "storage"]
  },
  {
    id: "integrations",
    label: "APIs & integrations",
    description: "Endpoints and connectivity to external systems.",
    categories: ["api", "apis", "integrations", "http", "webhooks"]
  },
  {
    id: "other",
    label: "Other features",
    description: "Additional features that do not yet belong to a curated capability family.",
    categories: []
  }
] as const;

const categoryToCapability = new Map<string, string>(
  capabilityDefinitions.flatMap(definition => definition.categories.map(category => [category, definition.id] as const))
);

export function getCapabilityDefinition(categories: string[]) {
  const capabilityId = categories
    .map(category => category.trim().toLowerCase())
    .map(category => categoryToCapability.get(category))
    .find(Boolean) ?? "other";

  return capabilityDefinitions.find(definition => definition.id === capabilityId) ?? capabilityDefinitions[capabilityDefinitions.length - 1];
}

export function buildCapabilityGroups<T extends CapabilityFeature>(features: T[]): CapabilityGroup<T>[] {
  const groups = new Map<string, CapabilityGroup<T>>();

  for (const feature of features) {
    const definition = getCapabilityDefinition(feature.categories);
    let group = groups.get(definition.id);
    if (!group) {
      group = {
        id: definition.id,
        label: definition.label,
        description: definition.description,
        features: [],
        enabledCount: 0,
        pendingCount: 0,
        problemCount: 0
      };
      groups.set(definition.id, group);
    }

    group.features.push(feature);
    if (feature.enabled) group.enabledCount++;
    if (feature.pending) group.pendingCount++;
    if (feature.problem) group.problemCount++;
  }

  return capabilityDefinitions
    .map(definition => groups.get(definition.id))
    .filter((group): group is CapabilityGroup<T> => !!group);
}