import { resolve } from "node:path";
import { mergeConfig } from "vite";
import shared from "./vite.config";

// The gallery exercises one fixture. Keep its optimizer independent of other browser
// entry points so their scans and cache invalidations cannot delay its readiness.
export default mergeConfig(shared, {
  cacheDir: resolve(__dirname, "../../node_modules/.vite/outlined-fields"),
  optimizeDeps: { entries: [resolve(__dirname, "outlined-fields.html")] }
});
