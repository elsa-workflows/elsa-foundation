# syntax=docker/dockerfile:1

# =============================================================================
# Elsa.Studio.Web production image
#
# Build context MUST be the Elsa Foundation repository root so the Studio host can
# consume its co-located optional modules. Example:
#
#   docker build -f Studio/src/apps/Elsa.Studio.Web/Dockerfile -t elsa-studio-web:local .
#
# The frontend (React/Vite) assets are generated before this Docker build (by CI or
# by running `pnpm build` locally) under each project's wwwroot. No MSBuild target
# invokes pnpm/vite, so this Dockerfile packages the generated assets without a
# Node/pnpm stage.
# =============================================================================

# ---- Build stage ------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
ARG STUDIO_VERSION=1.0.0

# Copy Studio-owned build configuration first so restore layers cache well. The extension
# projects import these files through their studio-level Directory.Build/Packages wrappers.
COPY Studio/Directory.Build.props Studio/Directory.Packages.props Studio/NuGet.config Studio/icon.png ./Studio/
COPY Studio/Elsa.Studio.slnx ./Studio/

# Hosting and its optional administration package use these lightweight Foundation libraries.
# Their shared build/version inputs and compile-only naming analyzer are required in this context too.
COPY Directory.Build.props Directory.Packages.props VersionLines.props PackageVersioning.props PackageRanges.targets .editorconfig ./
COPY Essentials/build/Analyzers/ ./Essentials/build/Analyzers/
COPY Essentials/src/Modularity/Core/ ./Essentials/src/Modularity/Core/
COPY Essentials/src/Modularity/Nuplane/ ./Essentials/src/Modularity/Nuplane/
COPY Essentials/src/Studio/Preferences/Core/ ./Essentials/src/Studio/Preferences/Core/
COPY Essentials/src/Events/ ./Essentials/src/Events/
COPY Essentials/src/Pipelines/Core/ ./Essentials/src/Pipelines/Core/
COPY Essentials/src/Primitives/Primitives/ ./Essentials/src/Primitives/Primitives/
COPY Essentials/src/Tasks/Core/ ./Essentials/src/Tasks/Core/

# Copy the Studio host and shared modules.
COPY Studio/src/ ./Studio/src/

# Copy only the optional Studio modules consumed by the host, not every Foundation extension.
COPY Extensions/Agent/studio/ ./Extensions/Agent/studio/
COPY Extensions/Attention/studio/ ./Extensions/Attention/studio/
COPY Extensions/ConsoleStream/studio/ ./Extensions/ConsoleStream/studio/
COPY Extensions/Connections/studio/ ./Extensions/Connections/studio/
COPY Extensions/Diagnostics.OpenTelemetry/studio/ ./Extensions/Diagnostics.OpenTelemetry/studio/
COPY Extensions/Diagnostics.StructuredLogs/studio/ ./Extensions/Diagnostics.StructuredLogs/studio/
COPY Extensions/Etl/studio/ ./Extensions/Etl/studio/
COPY Extensions/ExpressionEditors.JavaScript/studio/ ./Extensions/ExpressionEditors.JavaScript/studio/
COPY Extensions/ExpressionEditors.Liquid/studio/ ./Extensions/ExpressionEditors.Liquid/studio/
COPY Extensions/ExtensionBuilder/studio/ ./Extensions/ExtensionBuilder/studio/
COPY Extensions/FeatureManagement/studio/ ./Extensions/FeatureManagement/studio/
COPY Extensions/Secrets/studio/ ./Extensions/Secrets/studio/
COPY Extensions/Weaver/studio/ ./Extensions/Weaver/studio/
COPY Extensions/Workflows.Dashboard/studio/ ./Extensions/Workflows.Dashboard/studio/

# Fail the image build clearly if the frontend build was skipped. The generated
# wwwroot files are intentionally ignored by Git, so this protects both CI and
# local Docker builds from publishing an assetless image that returns 404 at `/`.
RUN test -f Studio/src/essentials/Elsa.Studio.Hosting/wwwroot/studio/index.html

# Restore and publish only the web host; project references are pulled in transitively.
#
# WarningsNotAsErrors=NU1603 keeps the repo's TreatWarningsAsErrors intact while
# tolerating a restore-only warning that appears in a clean environment: a transitive
# preview dependency (ConsoleLogStreaming.AspNetCore preview.13) is no longer on the
# feed and NuGet resolves the newer stable version instead.
RUN dotnet restore Studio/src/apps/Elsa.Studio.Web/Elsa.Studio.Web.csproj \
        --configfile Studio/NuGet.config \
        -p:WarningsNotAsErrors=NU1603 \
        -p:Version=$STUDIO_VERSION
RUN dotnet publish Studio/src/apps/Elsa.Studio.Web/Elsa.Studio.Web.csproj \
        -c Release \
        --no-restore \
        -p:WarningsNotAsErrors=NU1603 \
        -p:Version=$STUDIO_VERSION \
        -o /app/publish

# ---- Runtime stage ----------------------------------------------------------
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app

# Kestrel listens on 8080 (the aspnet base image default).
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

COPY --from=build /app/publish ./

# Nuplane persists reconciliation state under the content root (/app/.nuplane) and the
# directory feed folder (/app/packages) is watched at runtime, so the non-root runtime
# user needs write access to the app directory. /app/packages can optionally be replaced
# with a volume mount.
RUN mkdir -p /app/packages && chown -R $APP_UID:$APP_UID /app

# Run as the non-root user provided by the base image.
USER $APP_UID

ENTRYPOINT ["dotnet", "Elsa.Studio.Web.dll"]
