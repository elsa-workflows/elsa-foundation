/** Rejects path segments that intermediaries could reinterpret after URL parsing. */
export function assertSafePathSegments(pathname: string): void {
  for (const segment of pathname.split("/")) {
    let decoded = segment;
    if (decoded === "." || decoded === "..") throw new Error("Dot segments are not allowed in the request path.");

    // Decode repeatedly because an intermediary can decode a value before the backend sees it. Each
    // successful pass reduces the number of encoded layers, so deeply encoded input remains bounded.
    while (decoded.includes("%")) {
      let next: string;
      try {
        next = decodeURIComponent(decoded);
      } catch {
        throw new Error("The request path contains invalid percent encoding.");
      }
      if (next === decoded) break;
      if (next.includes("/") || next.includes("\\")) {
        throw new Error("Encoded path separators are not allowed in the request path.");
      }
      if (next === "." || next === "..") {
        throw new Error("Dot segments are not allowed in the request path.");
      }
      decoded = next;
    }
  }
}
