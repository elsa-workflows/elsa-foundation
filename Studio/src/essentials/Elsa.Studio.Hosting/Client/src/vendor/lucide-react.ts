// Runtime entry for the vendored `lucide-react` icon set. Serving a single shared
// copy through the import map (`/studio/vendor/lucide-react.js`) lets the host shell
// and every module client mark `lucide-react` external instead of each bundling its
// own copy (or, as Secrets did, externalizing it with no runtime provider).
export * from "lucide-react";
