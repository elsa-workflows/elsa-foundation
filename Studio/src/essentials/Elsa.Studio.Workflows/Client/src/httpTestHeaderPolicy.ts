export function validateHttpTestRequestHeaderNames(names: string[]): string | undefined {
  if (names.some(name => ["authorization", "cookie", "proxy-authorization"].includes(name.trim().toLowerCase()))) {
    return "Authentication headers are supplied by the authenticated API transport.";
  }
  return undefined;
}
