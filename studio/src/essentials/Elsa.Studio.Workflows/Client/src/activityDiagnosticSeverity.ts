/** Activities Design uses camel-case enum strings on the wire; older hosts used Pascal case. */
export function activityDiagnosticSeverity(value: string): "Error" | "Warning" | "Info" {
  const normalized = value.toLowerCase();
  return normalized === "error" ? "Error" : normalized === "warning" ? "Warning" : "Info";
}
