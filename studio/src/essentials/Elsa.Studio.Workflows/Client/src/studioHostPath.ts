/** Path before a whole `/studio` segment, or `null` when none is present. */
export function pathBeforeStudioSegment(pathname: string): string | null {
  return pathname.match(/^(.*?)\/studio(?=\/|$)/)?.[1] ?? null;
}
