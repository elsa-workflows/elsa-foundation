// Spec 260 FR-423: the run view keeps the selected occurrence in the URL, so a reload or a deep link opens the same scope
// and occurrence. `?selected=` is written; the older `?activityExecutionId=` deep link is still read.

export const runSelectionParameter = "selected";
const legacySelectionParameter = "activityExecutionId";

export function readRunSelection(search: string): string | null {
  const parameters = new URLSearchParams(search);
  return parameters.get(runSelectionParameter)?.trim() || parameters.get(legacySelectionParameter)?.trim() || null;
}

/** The URL with the selection replaced, or removed when nothing is selected. Other parameters are kept. */
export function withRunSelection(href: string, activityExecutionId: string | null): string {
  const url = new URL(href);
  url.searchParams.delete(legacySelectionParameter);
  if (activityExecutionId) url.searchParams.set(runSelectionParameter, activityExecutionId);
  else url.searchParams.delete(runSelectionParameter);
  return url.toString();
}

/** Replaces the current history entry, so selecting occurrences does not fill the back stack. */
export function writeRunSelection(
  activityExecutionId: string | null,
  location: Pick<Location, "href"> = window.location,
  history: Pick<History, "replaceState" | "state"> = window.history
) {
  const next = withRunSelection(location.href, activityExecutionId);
  if (next !== location.href) history.replaceState(history.state, "", next);
}
