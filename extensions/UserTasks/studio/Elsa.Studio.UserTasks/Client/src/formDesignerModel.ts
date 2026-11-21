import type { TaskField, TaskFieldDisplay, TaskForm } from "./taskApi";
import { builtInTaskFieldControls, resolveTaskFieldControl, validateTaskFieldConfigurationShape, validateTaskFieldControlConfiguration, type TaskFieldControlSource } from "./fieldControls";

export const taskFieldKinds = builtInTaskFieldControls.map(control => ({
  kind: control.legacyKind!,
  label: control.label,
  fieldLabel: control.legacyKind === "multiline" ? "Long text field" : `${control.label} field`
}));

export type TaskFieldKind = typeof taskFieldKinds[number]["kind"];

let nextRowId = 0;

export function createFieldRowId() {
  nextRowId++;
  return `form-field-${nextRowId}`;
}

export function createTaskField(kind: TaskFieldKind, fields: readonly TaskField[]): TaskField {
  const control = builtInTaskFieldControls.find(item => item.legacyKind === kind);
  if (!control) throw new Error(`No built-in field control is registered for “${kind}”.`);
  return control.createField(fields) as TaskField;
}

export function duplicateTaskField(field: TaskField, fields: readonly TaskField[]): TaskField {
  const baseKey = `${field.key.trim() || field.kind}_copy`;
  const existingKeys = new Set(fields.map(item => item.key.trim()));
  let key = baseKey;
  let suffix = 2;
  while (existingKeys.has(key)) key = `${baseKey}${suffix++}`;
  return { ...field, key, label: `${field.label || "Field"} copy`, choices: field.choices ? [...field.choices] : undefined,
    configuration: field.configuration ? structuredClone(field.configuration) : undefined };
}

export function validateTaskFields(fields: readonly TaskField[], controls: TaskFieldControlSource = builtInTaskFieldControls): string[][] {
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
    const resolved = resolveTaskFieldControl(field, controls);
    if (resolved.status !== "available") errors.push(resolved.reason ?? "Choose an available field control.");
    else if (field.controlId && resolved.control) {
      errors.push(...validateTaskFieldControlConfiguration(resolved.control, field.configuration));
      if (field.minimum != null || field.maximum != null || field.maxLength != null || field.choices != null || field.presentation != null || field.mustBeTrue) {
        errors.push("Custom field settings must be stored in its configuration object.");
      }
    }
    if (!field.controlId && field.kind === "choice") {
      const choices = field.choices ?? [];
      const normalized = choices.map(choice => choice.trim());
      if (!normalized.length) errors.push("Add at least one choice.");
      if (normalized.some(choice => !choice)) errors.push("Choice values cannot be blank.");
      if (new Set(normalized.filter(Boolean)).size !== normalized.filter(Boolean).length) errors.push("Choice values must be unique.");
    }
    if (!field.controlId && field.mustBeTrue && field.kind !== "boolean") errors.push("Must be checked is only supported for Boolean fields.");
    if (!field.controlId && field.presentation != null) {
      const { display, trueLabel, falseLabel, unansweredLabel } = field.presentation;
      const supported = field.kind === "boolean"
        ? ["checkbox", "dropdown", "radio"].includes(display)
        : field.kind === "choice" && ["dropdown", "radio"].includes(display);
      if (!supported) errors.push("Choose a display supported by this field type.");
      if (field.kind !== "boolean" && (trueLabel !== undefined || falseLabel !== undefined || unansweredLabel !== undefined)) {
        errors.push("Answer labels are only supported for Boolean fields.");
      }
      for (const [label, value] of [["Yes", trueLabel], ["No", falseLabel], ["Unanswered", unansweredLabel]] as const) {
        if (value != null && (typeof value !== "string" || !value.trim() || value.length > 256)) errors.push(`${label} label must contain 1–256 characters.`);
      }
    }
    if (!field.controlId && field.kind === "number") {
      if (field.minimum != null && !Number.isFinite(field.minimum)) errors.push("Minimum must be a finite number.");
      if (field.maximum != null && !Number.isFinite(field.maximum)) errors.push("Maximum must be a finite number.");
      if (field.minimum != null && field.maximum != null && field.minimum > field.maximum) errors.push("Minimum cannot exceed maximum.");
    }
    if (!field.controlId && (field.kind === "text" || field.kind === "multiline") && field.maxLength != null && (!Number.isInteger(field.maxLength) || field.maxLength < 1)) {
      errors.push("Maximum characters must be a positive whole number.");
    }
    return errors;
  });
}

export function validateTaskForm(form: Pick<TaskForm, "id" | "version" | "name" | "fields">, controls: TaskFieldControlSource = builtInTaskFieldControls): string[] {
  const errors: string[] = [];
  if (!form.id.trim()) errors.push("Form ID is required.");
  if (!form.version.trim()) errors.push("Version is required.");
  if (!form.name.trim()) errors.push("Form name is required.");
  if (form.fields.length > 100) errors.push("A form can have at most 100 fields.");
  errors.push(...validateTaskFields(form.fields, controls).flat());
  return errors;
}

export function serializeTaskFormSource(form: TaskForm) {
  return JSON.stringify(form, null, 2);
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === "object" && value !== null && !Array.isArray(value);
}

function rejectUnknownProperties(value: Record<string, unknown>, allowed: readonly string[], label: string) {
  const unexpected = Object.keys(value).find(key => !allowed.includes(key));
  if (unexpected) throw new Error(`Unsupported property "${unexpected}" in ${label}.`);
}

export function parseTaskFormSource(source: string, currentForm: TaskForm, controls: TaskFieldControlSource = builtInTaskFieldControls): TaskForm {
  let value: unknown;
  try {
    value = JSON.parse(source);
  } catch {
    throw new Error("Source must contain valid JSON.");
  }

  if (!isRecord(value)) throw new Error("JSON must contain a form object.");
  rejectUnknownProperties(value, ["id", "version", "name", "provider", "fields"], "form source");
  if (value.id !== currentForm.id) throw new Error("Form identifier cannot be changed in source.");
  if (value.version !== currentForm.version || value.provider !== currentForm.provider) {
    throw new Error("Version and provider are system-managed and cannot be changed in source.");
  }
  if (typeof value.name !== "string") throw new Error("Form name must be a string.");
  if (!Array.isArray(value.fields)) throw new Error("Form fields must be an array.");
  if (value.fields.length > 100) throw new Error("A form can have at most 100 fields.");

  const fields: TaskField[] = value.fields.map((field, index) => {
    if (!isRecord(field)) throw new Error(`Field ${index + 1} must be an object.`);
    rejectUnknownProperties(field, ["key", "label", "kind", "required", "readOnly", "help", "minimum", "maximum", "maxLength", "choices", "presentation", "mustBeTrue", "controlId", "configuration"], `field ${index + 1}`);
    if (typeof field.key !== "string" || typeof field.label !== "string") throw new Error(`Field ${index + 1} key and label must be strings.`);
    if (typeof field.kind !== "string" || !field.kind.trim() || field.kind.length > 64) throw new Error(`Field ${index + 1} has an unsupported type.`);
    if (typeof field.required !== "boolean" || typeof field.readOnly !== "boolean") throw new Error(`Field ${index + 1} required and readOnly values must be booleans.`);
    if (field.mustBeTrue != null && typeof field.mustBeTrue !== "boolean") throw new Error(`Field ${index + 1} mustBeTrue must be a boolean.`);
    if (field.controlId != null && (typeof field.controlId !== "string" || field.controlId.length > 128)) throw new Error(`Field ${index + 1} controlId must be a versioned string identity.`);

    const parsed: TaskField = { key: field.key, label: field.label, kind: field.kind, required: field.required, readOnly: field.readOnly };
    if (typeof field.controlId === "string") parsed.controlId = field.controlId;
    if (field.mustBeTrue === true) parsed.mustBeTrue = true;
    if (field.help != null) {
      if (typeof field.help !== "string") throw new Error(`Field ${index + 1} help must be a string.`);
      parsed.help = field.help;
    }
    for (const key of ["minimum", "maximum", "maxLength"] as const) {
      const property = field[key];
      if (property != null) {
        if (typeof property !== "number" || !Number.isFinite(property)) throw new Error(`Field ${index + 1} ${key} must be a finite number.`);
        parsed[key] = property;
      }
    }
    if (field.choices != null) {
      if (!Array.isArray(field.choices) || !field.choices.every(choice => typeof choice === "string")) throw new Error(`Field ${index + 1} choices must be an array of strings.`);
      parsed.choices = [...field.choices];
    }
    if (field.configuration != null) {
      if (!isRecord(field.configuration)) throw new Error(`Field ${index + 1} configuration must be a JSON object.`);
      const shapeErrors = validateTaskFieldConfigurationShape(field.configuration);
      if (shapeErrors.length) throw new Error(`Field ${index + 1}: ${shapeErrors.join(" ")}`);
      parsed.configuration = structuredClone(field.configuration);
    }
    if (field.presentation != null) {
      if (!isRecord(field.presentation)) throw new Error(`Field ${index + 1} presentation must be an object.`);
      rejectUnknownProperties(field.presentation, ["display", "trueLabel", "falseLabel", "unansweredLabel"], `field ${index + 1} presentation`);
      const { display, trueLabel, falseLabel, unansweredLabel } = field.presentation;
      if (typeof display !== "string" || !["checkbox", "dropdown", "radio"].includes(display)) throw new Error(`Field ${index + 1} has an unsupported presentation display.`);
      for (const [name, label] of [["trueLabel", trueLabel], ["falseLabel", falseLabel], ["unansweredLabel", unansweredLabel]] as const) {
        if (label != null && typeof label !== "string") throw new Error(`Field ${index + 1} ${name} must be a string.`);
      }
      parsed.presentation = {
        display: display as TaskFieldDisplay,
        ...(typeof trueLabel === "string" ? { trueLabel } : {}),
        ...(typeof falseLabel === "string" ? { falseLabel } : {}),
        ...(typeof unansweredLabel === "string" ? { unansweredLabel } : {})
      };
    }
    if (!parsed.controlId) {
      const fieldErrors = validateTaskFields([parsed], controls)[0];
      if (fieldErrors.length) throw new Error(`Field ${index + 1}: ${fieldErrors.join(" ")}`);
    } else {
      const resolved = resolveTaskFieldControl(parsed, controls);
      if (resolved.status === "available" && resolved.control) {
        const fieldErrors = validateTaskFields([parsed], controls)[0];
        if (fieldErrors.length) throw new Error(`Field ${index + 1}: ${fieldErrors.join(" ")}`);
      }
    }
    return parsed;
  });

  return { id: currentForm.id, version: currentForm.version, name: value.name, provider: currentForm.provider, fields };
}