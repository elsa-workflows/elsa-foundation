import { validateHttpTestRequestHeaderNames } from "../httpTestHeaderPolicy";

export interface HttpTestRequestHeader {
  id: number;
  name: string;
  value: string;
}

export function makeHttpTestRequestHeaders(rows: HttpTestRequestHeader[], contentType: string | null): Headers {
  const authenticationError = validateHttpTestRequestHeaderNames(rows.map(row => row.name));
  if (authenticationError) throw new Error(authenticationError);
  const headers = new Headers();
  for (const [index, row] of rows.entries()) {
    const name = row.name.trim();
    if (!name && !row.value) continue;
    if (name.toLowerCase() === "content-type") {
      throw new Error("Set Content-Type using the Request content type field.");
    }
    try {
      // Headers validates token names, byte-string values and embedded CR/LF.
      headers.append(name, row.value);
    } catch {
      // Do not include potentially sensitive header values in errors.
      throw new Error(`Header ${index + 1} has an invalid name or value.`);
    }
  }
  if (!headers.has("Accept")) headers.set("Accept", "*/*");
  if (contentType?.trim()) headers.set("Content-Type", contentType.trim());
  return headers;
}
