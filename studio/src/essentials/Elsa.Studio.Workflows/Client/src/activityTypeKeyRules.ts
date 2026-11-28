import type { ActivityTypeKeyRules } from "./activityDefinitionTypes";

const defaultActivityTypeKeyPattern = "^elsa\\.user\\.[a-z0-9]+(?:-[a-z0-9]+)*\\.[a-z0-9]+(?:-[a-z0-9]+)*$";

export function describeActivityTypeKeyRules(rules: ActivityTypeKeyRules): string[] {
  const prefix = activityTypeKeyPrefix(rules);
  const format = rules.pattern === defaultActivityTypeKeyPattern
    ? "Use two segments after the prefix; each segment uses lowercase letters and digits, with words separated by single hyphens."
    : `Matches pattern ${rules.pattern}`;

  return [
    `Starts with ${prefix}`,
    format,
    `At most ${rules.maximumLength} characters.`,
    `Must be unique within ${rules.collisionScope}; Studio never adds a suffix.`,
    `Example: ${prefix}team-name.activity-name`
  ];
}

export function validateActivityTypeKeyOverride(key: string, rules: ActivityTypeKeyRules): string | null {
  const normalizedKey = key.trim().normalize("NFC").toLowerCase();
  if (!normalizedKey) return null;
  if (normalizedKey.length > rules.maximumLength) return `Use at most ${rules.maximumLength} characters.`;

  const prefix = activityTypeKeyPrefix(rules);
  if (!normalizedKey.startsWith(prefix)) return `Must start with ${prefix}`;

  try {
    if (!new RegExp(rules.pattern).test(normalizedKey)) return "Match the displayed key format.";
  } catch {
    return null;
  }

  return null;
}

export function activityTypeKeyPrefix(rules: ActivityTypeKeyRules): string {
  return rules.prefix.endsWith(".") ? rules.prefix : `${rules.prefix}.`;
}