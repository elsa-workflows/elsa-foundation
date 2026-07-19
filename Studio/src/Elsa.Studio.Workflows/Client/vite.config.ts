import { defineModuleConfig } from "../../../vite.module.base";
import { mergeConfig } from "vite";

export default mergeConfig(
  defineModuleConfig({
    root: __dirname,
    outDir: "../wwwroot/studio/modules/workflows",
    extraExternals: ["@tanstack/react-query"]
  }),
  {
    build: {
      manifest: true
    }
  }
);
