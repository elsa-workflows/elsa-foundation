import { defineConfig, searchForWorkspaceRoot } from "vite";
import react from "@vitejs/plugin-react";
import { resolve } from "node:path";
import { minimalLogEntries } from "./minimalStudioData";

export default defineConfig({
  root: __dirname,
  // Keep the fixture optimizer separate from package builds and other Vite entry points.
  // Sharing their cache invalidates dependency URLs in already-open browser sessions.
  cacheDir: resolve(__dirname, "../../node_modules/.vite/browser"),
  plugins: [react(), {
    name: "minimal-log-browser-witness",
    configureServer(server) {
      server.middlewares.use((request, response, next) => {
        const url = new URL(request.url ?? "/", "http://fixture");
        if (!url.pathname.startsWith("/_elsa/studio/diagnostics/structured-logs/")) return next();
        if (url.pathname.endsWith("/stream")) {
          response.writeHead(200, { "Content-Type": "text/event-stream", "Cache-Control": "no-cache" });
          response.write(": connected\n\n");
          const heartbeat = setInterval(() => response.write(": heartbeat\n\n"), 15_000);
          response.on("close", () => clearInterval(heartbeat));
          return;
        }
        response.setHeader("Content-Type", "application/json");
        if (url.pathname.endsWith("/sources")) return response.end(JSON.stringify([{ id: "server", displayName: "Server" }, { id: "studio", displayName: "Studio" }]));
        if (url.pathname.endsWith("/recent")) {
          const category = url.searchParams.get("category") ?? "";
          return response.end(JSON.stringify(category === "empty" ? [] : minimalLogEntries.filter(entry => !category || entry.category.includes(category))));
        }
        next();
      });
    }
  }],
  // A first-use optimizer reload discards in-flight mocked authoring sessions.
  optimizeDeps: { include: ["lucide-react/dynamicIconImports"] },
  resolve: {
    alias: {
      "@elsa-workflows/studio-sdk": resolve(__dirname, "../../src/essentials/Elsa.Studio.Hosting/Client/src/sdk/index.ts"),
      "@elsa-workflows/studio-ui": resolve(__dirname, "../../src/essentials/Elsa.Studio.Hosting/StudioUi/src/index.ts"),
      "@xyflow/react": resolve(__dirname, "../../src/essentials/Elsa.Studio.Workflows/Client/node_modules/@xyflow/react")
    }
  },
  server: {
    fs: {
      allow: [searchForWorkspaceRoot(__dirname), resolve(__dirname, "../../../Extensions")]
    },
    host: "127.0.0.1",
    port: 4179,
    strictPort: true
  }
});
