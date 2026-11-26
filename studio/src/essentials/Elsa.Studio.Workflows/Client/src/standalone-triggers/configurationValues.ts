import { copyTriggerJson } from "./json";
import type { ConfigurationField, JsonValue, TriggerView } from "./types";

const object = (value: unknown): value is Record<string, JsonValue> => value !== null && typeof value === "object" && !Array.isArray(value) && [Object.prototype, null].includes(Object.getPrototypeOf(value));

/** Apply the same literal-key and nested-path protection to both editor surfaces. */
export function preserveConfiguration(next: Record<string, JsonValue>, previous: Record<string, JsonValue>, fields: ConfigurationField[], trigger?: TriggerView) {
  const result = copyTriggerJson(next);
  const paths = fields.filter(field => field.readOnly || field.sensitive && trigger)
    .flatMap(field => field.key.includes(".") ? [[field.key], field.key.split(".")] : [[field.key]]);
  paths.push(...(trigger?.configurationRedactions?.map(item => item.path) ?? []));
  for (const path of paths) {
    if (!path.length) continue;
    let target = result, source = previous;
    let reachable = true;
    for (const key of path.slice(0, -1)) {
      if (!Object.hasOwn(target, key) && !Object.hasOwn(source, key)) { reachable = false; break; }
      if (!object(target[key]) && (!Object.hasOwn(source, key) || !object(source[key]))) { reachable = false; break; }
      if (!Object.hasOwn(target, key) || !object(target[key]))
        Object.defineProperty(target, key, { value: Object.hasOwn(source, key) && object(source[key]) ? { ...source[key] } : {}, enumerable: true, writable: true, configurable: true });
      target = target[key] as Record<string, JsonValue>;
      source = Object.hasOwn(source, key) && object(source[key]) ? source[key] : {};
    }
    if (!reachable) continue;
    const key = path[path.length - 1];
    if (Object.hasOwn(source, key)) Object.defineProperty(target, key, { value: source[key], enumerable: true, writable: true, configurable: true });
    else delete target[key];
  }
  return result;
}
