import type { TaskField, TaskForm } from "./taskApi";

export const taskFieldKinds = [
  { kind: "text", label: "Text input", fieldLabel: "Text field" },
  { kind: "multiline", label: "Long text", fieldLabel: "Long text field" },
  { kind: "number", label: "Number", fieldLabel: "Number field" },
  { kind: "boolean", label: "Yes / no", fieldLabel: "Yes / no field" },
  { kind: "date", label: "Date", fieldLabel: "Date field" },
  { kind: "choice", label: "Choice", fieldLabel: "Choice field" }
] as const;

export type TaskFieldKind = typeof taskFieldKinds[number]["kind"];

let nextRowId = 0;

export function createFieldRowId() {
  nextRowId++;
  return `form-field-${nextRowId}`;
}

export function createTaskField(kind: TaskFieldKind, fields: readonly TaskField[]): TaskField {
  const definition = taskFieldKinds.find(item => item.kind === kind)!;
  let suffix = 1;
  let key = `${kind}${suffix}`;
  const existingKeys = new Set(fields.map(field => field.key.trim()));
  while (existingKeys.has(key)) key = `${kind}${++suffix}`;
  return {
    key,
    label: `${definition.fieldLabel} ${suffix}`,
    kind,
    required: false,
    readOnly: false,
    ...(kind === "choice" ? { choices: ["Option 1"] } : {})
  };
}

export function duplicateTaskField(field: TaskField, fields: readonly TaskField[]): TaskField {
  const baseKey = `${field.key.trim() || field.kind}_copy`;
  const existingKeys = new Set(fields.map(item => item.key.trim()));
  let key = baseKey;
  let suffix = 2;
  while (existingKeys.has(key)) key = `${baseKey}${suffix++}`;
  return { ...field, key, label: `${field.label || "Field"} copy`, choices: field.choices ? [...field.choices] : undefined };
}

export function validateTaskFields(fields: readonly TaskField[]): string[][] {
  const keyCounts = new Map<string, number>();
  for (const field of fields) {
    const key = field.key.trim();
    if (key) keyCounts.set(key, (keyCounts.get(key) ?? 0) + 1);
  }

  return fields.map(field => {
    const errors: string[] = [];
    const key = field.key.trim();
    if (!key) errors.push("Field key is required.");
    else if ((keyCounts.get(key) ?? 0) > 1) errors.push("Field keys must be unique.");
    if (!field.label.trim()) errors.push("Field label is required.");
    if (!taskFieldKinds.some(item => item.kind === field.kind)) errors.push("Choose a supported field type.");
    if (field.kind === "choice") {
      const choices = field.choices ?? [];
      const normalized = choices.map(choice => choice.trim());
      if (!normalized.length) errors.push("Add at least one choice.");
      if (normalized.some(choice => !choice)) errors.push("Choice values cannot be blank.");
      if (new Set(normalized.filter(Boolean)).size !== normalized.filter(Boolean).length) errors.push("Choice values must be unique.");
    }
    if (field.kind === "number") {
      if (field.minimum != null && !Number.isFinite(field.minimum)) errors.push("Minimum must be a finite number.");
      if (field.maximum != null && !Number.isFinite(field.maximum)) errors.push("Maximum must be a finite number.");
      if (field.minimum != null && field.maximum != null && field.minimum > field.maximum) errors.push("Minimum cannot exceed maximum.");
    }
    if ((field.kind === "text" || field.kind === "multiline") && field.maxLength != null && (!Number.isInteger(field.maxLength) || field.maxLength < 1)) {
      errors.push("Maximum characters must be a positive whole number.");
    }
    return errors;
  });
}

export function validateTaskForm(form: Pick<TaskForm, "id" | "version" | "name" | "fields">): string[] {
  const errors: string[] = [];
  if (!form.id.trim()) errors.push("Form ID is required.");
  if (!form.version.trim()) errors.push("Version is required.");
  if (!form.name.trim()) errors.push("Form name is required.");
  if (form.fields.length > 100) errors.push("A form can have at most 100 fields.");
  errors.push(...validateTaskFields(form.fields).flat());
  return errors;
}