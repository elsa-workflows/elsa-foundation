import { defineModuleConfig } from "../../../../../Studio/vite.module.base";
import { mergeConfig } from "vite";
export default mergeConfig(defineModuleConfig({ root: __dirname, outDir: "../wwwroot/studio/modules/bpmn-js" }), { build: { manifest: true, rollupOptions: { output: { manualChunks(id) { if (id.includes("/node_modules/diagram-js/")) return "bpmn-js-diagram"; } } } } });
