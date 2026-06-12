import { defineConfig } from "vite";
import react from "@vitejs/plugin-react";

export default defineConfig({
  plugins: [react()],
  server: {
    port: 5173,
    strictPort: false,
    proxy: {
      "/_demo": {
        target: "https://localhost:7243",
        secure: false
      },
      "/_admin": {
        target: "https://localhost:7243",
        secure: false
      },
      "/default": {
        target: "https://localhost:7243",
        secure: false
      },
      "/diagnostics": {
        target: "https://localhost:7243",
        secure: false,
        ws: true
      }
    }
  },
  build: {
    outDir: "../wwwroot",
    emptyOutDir: true,
    rollupOptions: {
      output: {
        entryFileNames: "assets/[name].js",
        chunkFileNames: "assets/[name].js",
        assetFileNames: "assets/[name].[ext]"
      }
    }
  }
});
