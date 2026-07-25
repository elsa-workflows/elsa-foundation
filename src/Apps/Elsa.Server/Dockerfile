# syntax=docker/dockerfile:1

# =============================================================================
# Elsa.Server production image
#
# Build context MUST be the repository root, because the build relies on
# repo-root configuration (Directory.Packages.props, NuGet.config) and the
# project references reach across the whole src/ tree. Example:
#
#   docker build -f src/Apps/Elsa.Server/Dockerfile -t elsa-server:local .
#
# Runtime configuration is supplied via environment variables (standard .NET
# double-underscore layering, e.g. Elsa__ModuleManagement__ApiKey) and an
# optional mounted shells.json. See docs/docker.md for the full surface.
# =============================================================================

# ---- Build stage ------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# TARGETARCH is supplied automatically by BuildKit (amd64 or arm64) and drives the
# .NET runtime identifier used for the ReadyToRun publish below. It is declared here
# (after FROM) so it is in scope for the restore/publish RUN steps.
#
# Why a RID at all: ReadyToRun (spec 134) is a per-RID AOT pre-compilation of the
# app's own ~90 assemblies (crossgen2), which cuts first-request JIT cost. R2R
# therefore REQUIRES a concrete RuntimeIdentifier — a portable (RID-less) publish
# cannot be R2R-compiled. The multi-arch image (linux/amd64 + linux/arm64, see
# .github/workflows/docker.yml) builds each platform in its own target-arch SDK
# image (emulated for the non-native arch), so crossgen runs native-for-target on
# every platform — no cross-arch crossgen is involved. The publish stays
# framework-dependent (--self-contained false) so it still runs on the shared
# aspnet runtime in the final stage and only the app assemblies grow, not the image
# by a whole bundled runtime. Scoping R2R to the Dockerfile (not the csproj) keeps
# every local `dotnet build`/dev inner-loop on the fast JIT publish.
ARG TARGETARCH

# Copy repo-root build configuration first so restore layers cache well.
# Directory.Build.props carries package metadata only (no build-behavior changes);
# it is copied so the image builds identically to local/CI. This repo has no root
# Directory.Build.targets.
#
# NuGet.config is the repo-root config. Its packageSourceMapping maps every
# package the solution uses to an explicit source, so restore is fully
# self-contained here and needs no user-level NuGet config fallback.
COPY Directory.Build.props ./
COPY Directory.Packages.props ./
COPY NuGet.config ./
COPY Elsa.Server.slnx ./

# Copy the source tree (tests, docs, local dev artifacts are excluded via .dockerignore).
COPY src/ ./src/

# Restore and publish only the server host; project references are pulled in transitively.
#
# WarningsNotAsErrors=NU1603 tolerates a restore-only warning that can appear in a clean
# environment when a transitive preview dependency resolves to a newer available version,
# without relaxing the repo's TreatWarningsAsErrors for real code warnings.
# Map BuildKit's TARGETARCH (amd64/arm64) to the .NET RID (linux-x64/linux-arm64).
# Restore is RID-specific because R2R needs the target's crossgen2 + runtime pack.
RUN RID="linux-$(echo "${TARGETARCH:-amd64}" | sed 's/amd64/x64/')" \
    && dotnet restore src/Apps/Elsa.Server/Elsa.Server.csproj \
        -r "$RID" \
        -p:PublishReadyToRun=true \
        -p:WarningsNotAsErrors=NU1603

# ReadyToRun publish (spec 134): framework-dependent, RID-targeted, crossgen'd.
# TieredCompilation and TieredPGO are left at their .NET 10 defaults (both already
# on) — R2R images still participate in tiered compilation, so the defaults are
# already optimal and are deliberately NOT overridden here. InvariantGlobalization
# is deliberately NOT enabled: the host does auth + culture-sensitive work, so
# dropping ICU is unsafe (see specs/134-container-readytorun-publish/research.md).
RUN RID="linux-$(echo "${TARGETARCH:-amd64}" | sed 's/amd64/x64/')" \
    && dotnet publish src/Apps/Elsa.Server/Elsa.Server.csproj \
        -c Release \
        -r "$RID" \
        --self-contained false \
        --no-restore \
        -p:PublishReadyToRun=true \
        -p:WarningsNotAsErrors=NU1603 \
        -o /app/publish

# ---- Runtime stage ----------------------------------------------------------
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app

# curl is added solely to give the container a self-contained HTTP healthcheck
# (the aspnet base image ships no HTTP client). Installed before dropping to the
# non-root user.
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*

# Kestrel listens on 8080 (the aspnet base image default).
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

COPY --from=build /app/publish ./

# The server writes state under the content root at runtime:
#   /app/.nuplane            Nuplane reconciliation state
#   /app/packages            Nuplane directory feed (watched; optionally a volume mount)
#   /app/.elsa               GitHubCopilotAgent working directory
#   /app/extension-builder   ExtensionBuilder workspace
#   *.db / *.sqlite*.db      SQLite fallback stores (only when a SQLite lane is composed)
# so the non-root runtime user needs write access to the app directory.
RUN mkdir -p /app/packages /app/.nuplane /app/.elsa && chown -R $APP_UID:$APP_UID /app

# The root endpoint returns a small Healthy JSON payload (see Program.cs).
HEALTHCHECK --interval=15s --timeout=3s --start-period=40s --retries=10 \
    CMD curl -fsS http://localhost:8080/ || exit 1

# Run as the non-root user provided by the base image.
USER $APP_UID

ENTRYPOINT ["dotnet", "Elsa.Server.dll"]
