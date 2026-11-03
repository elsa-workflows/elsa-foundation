# Package-only Studio host

A small application-owned composition root: public Hosting + Dashboard packages, selected shell features and demo configuration. No Web infrastructure class, project reference or frontend build.

Follow the complete [custom hosting recipe](../../../Studio/docs/custom-hosting.md) for obtaining matching original candidate archives, selecting their exact version and preview sources, materializing `CustomStudio.csproj.in` as `CustomStudio.csproj`, restoring, publishing and running outside the repository. The template suffix keeps this consumer out of the repository's production-project inventory; it is a normal Web SDK project after download.

`Auth.Enabled: false` and an empty backend URL deliberately make this a local browser-shell demo. Configure the backend and authentication before using protected server operations. Optional themes, administration and other modules are separate choices explained by the guide.
