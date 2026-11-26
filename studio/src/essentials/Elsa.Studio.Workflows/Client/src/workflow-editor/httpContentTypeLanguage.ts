export type HttpContentTypeLanguage = "json" | "xml" | "html" | "javascript" | "plaintext";

const mediaTypePattern = /^[!#$%&'*+.^_`|~0-9A-Za-z-]+\/[!#$%&'*+.^_`|~0-9A-Za-z-]+$/;

/** Resolves an HTTP media type to the language identifier used by Studio's request-body editor. */
export function getHttpContentTypeLanguage(contentType: string | null | undefined): HttpContentTypeLanguage {
  const mediaType = contentType?.split(";", 1)[0].trim().toLowerCase();
  if (!mediaType || !mediaTypePattern.test(mediaType)) return "plaintext";

  if (mediaType === "application/json" || mediaType === "text/json" || mediaType.endsWith("+json")) {
    return "json";
  }

  if (mediaType === "text/html") return "html";

  if (mediaType === "application/xml" || mediaType === "text/xml" || mediaType.endsWith("+xml")) {
    return "xml";
  }

  if (["application/javascript", "text/javascript", "application/ecmascript", "text/ecmascript"].includes(mediaType)) {
    return "javascript";
  }

  return "plaintext";
}
