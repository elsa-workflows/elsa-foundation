# JavaScript expression editor

Studio expression editing and tooling are provided by the existing JavaScript editor module.

## Cross-domain contributions

The module contributes optional [expression scope preservation](../../Studio/src/essentials/Elsa.Studio.Api/EXTENSION_POINTS.md#expression-scope-preservation-sdk--elsastudiohosting) through the Studio SDK expression editor registry. Shared native-block authoring uses this language-owned verdict when adding an unrelated variable around an existing expression.

The contribution admits only bounded `binding-pure-v1` numeric and declared `args` parameter forms. Other source refuses the rewrite; the proof does not validate or execute arbitrary JavaScript. Changing the contribution revision invalidates outstanding block previews. Hosts without this contribution retain the normal editing surface and refuse transformations that require its proof.
