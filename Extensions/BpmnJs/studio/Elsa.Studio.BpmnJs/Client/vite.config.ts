import { defineModuleConfig } from "../../../../../Studio/vite.module.base";

// Source qualification only; the public module entry is supplied by the later package slice.
export default defineModuleConfig({ root: __dirname, outDir: "../wwwroot/studio/modules/bpmn-js" });
