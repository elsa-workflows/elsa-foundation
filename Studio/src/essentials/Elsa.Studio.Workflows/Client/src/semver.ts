export interface ParsedSemVer {
  major: bigint;
  minor: bigint;
  patch: bigint;
  prerelease: string[];
}

export function parseSemVer(value: string): ParsedSemVer | null {
  const match = /^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)(?:-([0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*))?(?:\+[0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*)?$/.exec(value);
  if (!match) return null;
  const prerelease = match[4]?.split(".") ?? [];
  if (prerelease.some(segment => /^\d+$/.test(segment) && segment.length > 1 && segment.startsWith("0"))) return null;
  return { major: BigInt(match[1]!), minor: BigInt(match[2]!), patch: BigInt(match[3]!), prerelease };
}

export function compareSemVer(left: ParsedSemVer, right: ParsedSemVer) {
  for (const key of ["major", "minor", "patch"] as const) {
    if (left[key] !== right[key]) return left[key] > right[key] ? 1 : -1;
  }
  if (!left.prerelease.length && right.prerelease.length) return 1;
  if (left.prerelease.length && !right.prerelease.length) return -1;
  for (let index = 0; index < Math.max(left.prerelease.length, right.prerelease.length); index += 1) {
    const a = left.prerelease[index];
    const b = right.prerelease[index];
    if (a === undefined) return -1;
    if (b === undefined) return 1;
    if (a === b) continue;
    const numericA = /^\d+$/.test(a);
    const numericB = /^\d+$/.test(b);
    if (numericA && numericB) {
      if (a.length !== b.length) return a.length > b.length ? 1 : -1;
      return a > b ? 1 : -1;
    }
    if (numericA) return -1;
    if (numericB) return 1;
    return a > b ? 1 : -1;
  }
  return 0;
}
