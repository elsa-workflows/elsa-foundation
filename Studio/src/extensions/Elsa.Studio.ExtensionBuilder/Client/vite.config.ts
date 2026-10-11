import { mergeConfig } from "vite";
import { defineModuleConfig } from "../../../../vite.module.base";

export default mergeConfig(
  defineModuleConfig({
    root: __dirname,
    outDir: "../wwwroot/studio/modules/extension-builder"
  }),
  {
    test: {
      // Full-page jsdom renders of the builder normally finish in well under a second, but parallel workers on a
      // loaded machine can slow them 10x+ past the default 5s. Individual tests never legitimately take this long;
      // this only delays failure detection for genuinely hung tests.
      testTimeout: 15000
    }
  }
);
