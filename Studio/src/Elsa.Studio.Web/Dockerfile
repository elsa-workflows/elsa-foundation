# syntax=docker/dockerfile:1

# =============================================================================
# Elsa.Studio.Web production image
#
# Build context MUST be the repository root, because the build relies on
# repo-root configuration (Directory.Build.props/targets, Directory.Packages.props,
# NuGet.config). Example:
#
#   docker build -f src/Elsa.Studio.Web/Dockerfile -t elsa-studio-web:local .
#
# The frontend (React/Vite) assets are prebuilt and committed under each project's
# wwwroot, and no MSBuild target invokes pnpm/vite, so `dotnet publish` alone
# produces a complete, runnable app - no Node/pnpm stage is required.
# =============================================================================

# ---- Build stage ------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy repo-root build configuration first so restore layers cache well.
COPY Directory.Build.props Directory.Build.targets Directory.Packages.props ./
COPY NuGet.config ./
COPY Elsa.Studio.slnx ./

# Copy the source tree (tests, docs, etc. are excluded via .dockerignore).
COPY src/ ./src/

# Restore and publish only the web host; project references are pulled in transitively.
#
# WarningsNotAsErrors=NU1603 keeps the repo's TreatWarningsAsErrors intact while
# tolerating a restore-only warning that appears in a clean environment: a transitive
# preview dependency (ConsoleLogStreaming.AspNetCore preview.13) is no longer on the
# feed and NuGet resolves the newer stable version instead.
RUN dotnet restore src/Elsa.Studio.Web/Elsa.Studio.Web.csproj \
        -p:WarningsNotAsErrors=NU1603
RUN dotnet publish src/Elsa.Studio.Web/Elsa.Studio.Web.csproj \
        -c Release \
        --no-restore \
        -p:WarningsNotAsErrors=NU1603 \
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
