import { defineModuleConfig } from "../../../vite.module.base";

export default defineModuleConfig({
  root: __dirname,
  outDir: "../wwwroot/studio/modules/workflows",
  extraExternals: ["@tanstack/react-query"]
});
