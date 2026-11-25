# syntax=docker/dockerfile:1

# =============================================================================
# Elsa.Studio.Essentials.Host production image (published as elsaworkflows/elsa-studio)
#
# The Studio side of the Essentials server: the bare shell plus the modules whose backend capabilities
# Elsa.Essentials.Host advertises. Every other module is a Nuplane add-on installed from the feed.
#
# Build context MUST be the Elsa Foundation repository root so the Studio host can
# consume its co-located optional modules. Example:
#
#   docker build -f Studio/src/apps/Elsa.Studio.Essentials.Host/Dockerfile -t elsa-studio-essentials:local .
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

# Hosting and its optional administration package use these lightweight Foundation libraries.
# Their shared build/version inputs and compile-only naming analyzer are required in this context too.
COPY Directory.Build.props Directory.Packages.props VersionLines.props PackageVersioning.props PackageRanges.targets .editorconfig ./
COPY essentials/build/Analyzers/ ./essentials/build/Analyzers/
COPY essentials/src/Modularity/Core/ ./essentials/src/Modularity/Core/
COPY essentials/src/Modularity/Nuplane/ ./essentials/src/Modularity/Nuplane/
COPY essentials/src/Studio/Preferences/Core/ ./essentials/src/Studio/Preferences/Core/
COPY essentials/src/Events/ ./essentials/src/Events/
COPY essentials/src/Pipelines/Core/ ./essentials/src/Pipelines/Core/
COPY essentials/src/Primitives/Primitives/ ./essentials/src/Primitives/Primitives/
COPY essentials/src/Tasks/Core/ ./essentials/src/Tasks/Core/

# Copy the Studio host and the shared Studio projects in its project closure.
COPY Studio/src/apps/Elsa.Studio.Essentials.Host/ ./Studio/src/apps/Elsa.Studio.Essentials.Host/
COPY Studio/src/essentials/Elsa.Studio.Api/ ./Studio/src/essentials/Elsa.Studio.Api/
COPY Studio/src/essentials/Elsa.Studio.Backends/ ./Studio/src/essentials/Elsa.Studio.Backends/
COPY Studio/src/essentials/Elsa.Studio.Core/ ./Studio/src/essentials/Elsa.Studio.Core/
COPY Studio/src/essentials/Elsa.Studio.Hosting/ ./Studio/src/essentials/Elsa.Studio.Hosting/
COPY Studio/src/essentials/Elsa.Studio.Hosting.Distribution/ ./Studio/src/essentials/Elsa.Studio.Hosting.Distribution/
COPY Studio/src/essentials/Elsa.Studio.Hosting.Nuplane/ ./Studio/src/essentials/Elsa.Studio.Hosting.Nuplane/
COPY Studio/src/essentials/Elsa.Studio.Hosting.Themes/ ./Studio/src/essentials/Elsa.Studio.Hosting.Themes/
COPY Studio/src/essentials/Elsa.Studio.Workflows/ ./Studio/src/essentials/Elsa.Studio.Workflows/

# Copy only the optional Studio modules the app compiles in (its project closure). Every other module is a feed add-on.
COPY extensions/Attention/studio/ ./extensions/Attention/studio/
COPY extensions/ExpressionEditors.JavaScript/studio/ ./extensions/ExpressionEditors.JavaScript/studio/
COPY extensions/ExpressionEditors.Liquid/studio/ ./extensions/ExpressionEditors.Liquid/studio/
COPY extensions/FeatureManagement/studio/ ./extensions/FeatureManagement/studio/
COPY extensions/Secrets/studio/ ./extensions/Secrets/studio/
COPY extensions/UserTasks/studio/ ./extensions/UserTasks/studio/
COPY extensions/Workflows.Dashboard/studio/ ./extensions/Workflows.Dashboard/studio/

# Fail the image build clearly if the frontend build was skipped. The generated
# wwwroot files are intentionally ignored by Git, so this protects both CI and
# local Docker builds from publishing an assetless image that returns 404 at `/`.
RUN test -f Studio/src/essentials/Elsa.Studio.Hosting/wwwroot/studio/index.html

# Restore and publish only the web host; project references are pulled in transitively.
# WarningsNotAsErrors=NU1603 keeps TreatWarningsAsErrors while tolerating the restore-only
# NU1603 warning, as the Workbench image does.
RUN dotnet restore Studio/src/apps/Elsa.Studio.Essentials.Host/Elsa.Studio.Essentials.Host.csproj \
        --configfile Studio/NuGet.config \
        -p:WarningsNotAsErrors=NU1603 \
        -p:Version=$STUDIO_VERSION
RUN dotnet publish Studio/src/apps/Elsa.Studio.Essentials.Host/Elsa.Studio.Essentials.Host.csproj \
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

ENTRYPOINT ["dotnet", "Elsa.Studio.Essentials.Host.dll"]
