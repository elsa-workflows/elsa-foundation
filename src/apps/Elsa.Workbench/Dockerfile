# syntax=docker/dockerfile:1.26
# Keep this pin within a few releases of the BuildKit that docker/github-builder
# runs -- moby/buildkit v0.32.2, fixed by the @v1.16.0 pin in
# .github/workflows/docker.yml, so the two move together. BuildKit negotiates
# capabilities with the frontend, and a frontend older than the daemon fails outright:
# 1.20 (2025-11) predates the source.git.fetchbycommit capability added in
# 2026-04, and every CI build died with "unknown API capability
# source.git.fetchbycommit" while parsing this file. Local builds do not
# reproduce it -- only Buildx >= v0.36 sends the git query that makes the
# frontend ask for that capability.

# =============================================================================
# Elsa.Workbench container image (development and demo host, not the shipped product)
#
# Build context MUST be the repository root, because the build relies on
# repo-root configuration (Directory.Packages.props, NuGet.config) and the
# project references reach across the whole src/ tree. Example:
#
#   docker build -f src/apps/Elsa.Workbench/Dockerfile -t elsa-workbench:local .
#
# Runtime configuration is supplied via environment variables (standard .NET
# double-underscore layering, e.g. Elsa__ModuleManagement__ApiKey) and an
# optional mounted shells.json. See docs/docker.md for the full surface.
# =============================================================================

# ---- Restore stage ----------------------------------------------------------
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS restore
WORKDIR /src

# TARGETARCH is supplied automatically by BuildKit (amd64 or arm64) and drives the
# .NET runtime identifier used for the ReadyToRun publish below. It is declared here
# (after FROM) so it is in scope for the restore/publish RUN steps.
#
# Why a RID at all: ReadyToRun (spec 134) is a per-RID AOT pre-compilation of the
# app's own ~90 assemblies (crossgen2), which cuts first-request JIT cost. R2R
# therefore REQUIRES a concrete RuntimeIdentifier — a portable (RID-less) publish
# cannot be R2R-compiled. Docker's GitHub builder routes linux/amd64 and linux/arm64
# to matching native runners, so crossgen runs natively for each target instead of
# under QEMU. The publish stays framework-dependent (--self-contained false) so it
# still runs on the shared aspnet runtime in the final stage and only the app
# assemblies grow, not the image by a whole bundled runtime. Scoping R2R to the
# Dockerfile (not the csproj) keeps every local `dotnet build`/dev inner-loop on the
# fast JIT publish.
ARG TARGETARCH

# Copy repo-root build configuration first so restore layers cache well.
# Directory.Build.props carries the shared TargetFramework/Nullable/ImplicitUsings
# and package metadata, so it is load-bearing for restore, not just cosmetic --
# without it the project files below have no target framework.
#
# *.props and *.targets, not a list of names: Directory.Build.props imports sibling
# root files (VersionLines.props, PackageVersioning.props, PackageRanges.targets),
# and a missing one does not fail here -- restore ignores missing imports, so it
# silently restores the wrong graph and only the --no-restore publish below dies
# with MSB4019.
#
# NuGet.config is the repo-root config. Its packageSourceMapping maps every
# package the solution uses to an explicit source, so restore is fully
# self-contained here and needs no user-level NuGet config fallback.
COPY *.props *.targets NuGet.config Elsa.Server.slnx ./

# Copy only restore inputs while preserving their source-tree paths. Ordinary source
# changes no longer invalidate the expensive RID-specific restore layer. The recursive
# patterns cover the complete project graph without a manually maintained file list,
# each project's committed NuGet lock file included (#2122).
COPY --parents src/**/*.csproj src/**/*.targets src/**/packages.lock.json ./

# Locked, and without a runtime identifier (#2122): restores exactly the versions the
# committed lock files name, and fails when one no longer matches its project or names a
# version the feed no longer has. The RID-specific restores below cannot be locked: -r
# applies to every project in the graph, so each would need a lock file for exactly that
# one runtime, and one committed lock file cannot hold linux-x64, linux-arm64 and the
# runtime-less restore CI locks. They resolve afresh instead. NuGet takes the lowest
# version each range admits, which is what the lock files recorded, so they land on the
# versions this restore has just shown the feed still serves.
# See docs/reference/nuget-lock-files.md.
RUN dotnet restore src/apps/Elsa.Workbench/Elsa.Workbench.csproj \
    --locked-mode \
    -p:WarningsNotAsErrors=NU1603

# Restore only the server host; project references are pulled in transitively.
#
# WarningsNotAsErrors=NU1603 tolerates a restore-only warning that can appear in a clean
# environment when a transitive preview dependency resolves to a newer available version,
# without relaxing the repo's TreatWarningsAsErrors for real code warnings.
# Map BuildKit's TARGETARCH (amd64/arm64) to the .NET RID (linux-x64/linux-arm64).
# Restore is RID-specific because R2R needs the target's crossgen2 + runtime pack.
RUN RID="linux-$(echo "${TARGETARCH:-amd64}" | sed 's/amd64/x64/')" \
    && dotnet restore src/apps/Elsa.Workbench/Elsa.Workbench.csproj \
        -r "$RID" \
        -p:PublishReadyToRun=true \
        -p:WarningsNotAsErrors=NU1603

# ---- Build stage ------------------------------------------------------------
FROM restore AS build

# Copy the source tree (tests, docs, local dev artifacts are excluded via .dockerignore).
# One root carries everything the Workbench composes: src/essentials, src/extensions and src/apps (#1815).
COPY src/ ./src/

# Package versions (ADR 0067, spec 150, #2084). The Docker Images workflow passes
# ELSA_PACKAGE_VERSIONS: the pack-properties file the version calculator writes for
# this commit, gzipped and base64-encoded onto one line, because the builder takes
# build args one per line. Both commands below read it through
# -p:CustomBeforeDirectoryBuildProps, which gives every Elsa assembly, and every
# Elsa package in the deps.json, the version the feed carries, and makes
# Elsa.Workbench.csproj ship ComputedVersions/appsettings.Production.json, which
# declares the Elsa shares host-provided. The restore has to see it too: deps.json
# takes a project reference's version from the restore, not the build, so a
# publish alone would stamp the assemblies and still record the dev versions.
# The restore stage above stays free of it and keeps its cache; this one only
# rewrites the assets files from the packages that stage downloaded.
#
# Left at its default, source-build, the image is built as a source build: dev
# versions and the committed appsettings.Production.json, which leaves the Elsa
# shares undeclared, since every feed package's range excludes a dev version. That
# is what `docker build` and docker compose do. An empty value is refused: it is
# what the workflow passes when it lost the versions, and building on would ship an
# image that looks right and checks nothing.
ARG ELSA_PACKAGE_VERSIONS=source-build

# ReadyToRun publish (spec 134): framework-dependent, RID-targeted, crossgen'd.
# TieredCompilation and TieredPGO are left at their .NET 10 defaults (both already
# on) — R2R images still participate in tiered compilation, so the defaults are
# already optimal and are deliberately NOT overridden here. InvariantGlobalization
# is deliberately NOT enabled: the host does auth + culture-sensitive work, so
# dropping ICU is unsafe (see specs/134-container-readytorun-publish/research.md).
RUN RID="linux-$(echo "${TARGETARCH:-amd64}" | sed 's/amd64/x64/')" \
    && set -- \
    && case "$ELSA_PACKAGE_VERSIONS" in \
        source-build) ;; \
        "") echo "ELSA_PACKAGE_VERSIONS is empty. Pass the computed versions, or leave it unset for a source build." >&2; exit 1 ;; \
        *) mkdir -p /tmp/elsa \
            && printf '%s' "$ELSA_PACKAGE_VERSIONS" | base64 -d | gunzip > /tmp/elsa/package-versions.props \
            && set -- -p:CustomBeforeDirectoryBuildProps=/tmp/elsa/package-versions.props ;; \
    esac \
    && dotnet restore src/apps/Elsa.Workbench/Elsa.Workbench.csproj \
        -r "$RID" \
        -p:PublishReadyToRun=true \
        -p:WarningsNotAsErrors=NU1603 \
        "$@" \
    && dotnet publish src/apps/Elsa.Workbench/Elsa.Workbench.csproj \
        -c Release \
        -r "$RID" \
        --self-contained false \
        --no-restore \
        -p:PublishReadyToRun=true \
        -p:WarningsNotAsErrors=NU1603 \
        "$@" \
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
#   /app/extension-builder   Extension Builder workspaces (only when enabled with a relative Elsa:ExtensionBuilder:StoragePath)
#   *.db / *.sqlite*.db      SQLite fallback stores (only when a SQLite lane is composed)
# so the non-root runtime user needs write access to the app directory.
RUN mkdir -p /app/packages /app/.nuplane /app/.elsa && chown -R $APP_UID:$APP_UID /app

# The root endpoint returns a small Healthy JSON payload (see Program.cs).
HEALTHCHECK --interval=15s --timeout=3s --start-period=40s --retries=10 \
    CMD curl -fsS http://localhost:8080/ || exit 1

# Run as the non-root user provided by the base image.
USER $APP_UID

ENTRYPOINT ["dotnet", "Elsa.Workbench.dll"]
