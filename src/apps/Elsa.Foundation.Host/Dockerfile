# syntax=docker/dockerfile:1.26
# =============================================================================
# Elsa.Foundation.Host container image (minimal Nuplane + CShells host).
#
# Build context MUST be the repository root, because restore relies on repo-root
# configuration (Directory.Build.props, Directory.Packages.props, NuGet.config).
# Every feature still arrives at runtime as a .nupkg through the Nuplane
# directory feed at /app/packages. The host has one compile-time infrastructure
# reference for shared endpoint metadata, plus Line A, the ten cross-cutting
# contract projects every host shares with every feature (ADR 0067, #2126), plus
# the cluster membership it composes on its own container, whose contracts
# (Elsa.Cluster.Core, Elsa.Persistence.Schema) it shares too (#2143) — none of
# which brings a feature implementation or EF Core into this host — so its
# project and their sources must also be present in the Docker build context.
# Example:
#
#   docker build -f src/apps/Elsa.Foundation.Host/Dockerfile \
#                -t elsa-foundation-host:local-<sha> .
#
# Runtime configuration is supplied via environment variables (standard .NET
# double-underscore layering, e.g. Elsa__ModuleManagement__ApiKey) and an
# optional mounted shells.json at /app/shells.json.
# =============================================================================

# ---- Build stage ------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Repo-root build configuration first, so the restore layer caches well.
# Directory.Packages.props carries central (and transitively pinned) versions;
# NuGet.config carries the feed list + packageSourceMapping, so restore is fully
# self-contained and needs no user-level NuGet config. *.props and *.targets rather
# than names, because Directory.Build.props imports sibling root files and restore
# ignores a missing import -- see the same step in the Workbench Dockerfile.
COPY *.props *.targets NuGet.config ./

# Restore inputs: the host, its shared endpoint-metadata project, Line A, its cluster membership, and the computed-production-settings
# swap Elsa.Foundation.Host.csproj imports from src/apps/ (shared with Elsa.Workbench.csproj, #2126) -- MSBuild
# treats a missing <Import> as an error, not a no-op the way it does for the root files above, so this one has
# to be copied before restore too, or restore fails outright with MSB4019 rather than silently.
# Each project comes with its committed NuGet lock file, and both restores below are locked (#2122): they
# restore exactly the versions those files name, and fail when one no longer matches its project or names a
# version the feed no longer has. A project whose lock file is missing would restore unlocked without a word,
# which is why HostEndpointMetadataTests requires each one here. See docs/reference/nuget-lock-files.md.
COPY src/apps/ComputedProductionSettings.targets src/apps/
COPY src/apps/Elsa.Foundation.Host/Elsa.Foundation.Host.csproj src/apps/Elsa.Foundation.Host/packages.lock.json src/apps/Elsa.Foundation.Host/
COPY src/essentials/Api/AspNetCore/Elsa.Api.AspNetCore.csproj src/essentials/Api/AspNetCore/packages.lock.json src/essentials/Api/AspNetCore/
COPY src/essentials/Attention/Core/Elsa.Attention.Core.csproj src/essentials/Attention/Core/packages.lock.json src/essentials/Attention/Core/
COPY src/essentials/Caching/Core/Elsa.Caching.Core.csproj src/essentials/Caching/Core/packages.lock.json src/essentials/Caching/Core/
COPY src/essentials/Cluster/Core/Elsa.Cluster.Core.csproj src/essentials/Cluster/Core/packages.lock.json src/essentials/Cluster/Core/
COPY src/essentials/Cluster/InProcess/Elsa.Cluster.InProcess.csproj src/essentials/Cluster/InProcess/packages.lock.json src/essentials/Cluster/InProcess/
COPY src/essentials/Cluster/Readability/Elsa.Cluster.Readability.csproj src/essentials/Cluster/Readability/packages.lock.json src/essentials/Cluster/Readability/
COPY src/essentials/Events/Core/Elsa.Events.Core.csproj src/essentials/Events/Core/packages.lock.json src/essentials/Events/Core/
COPY src/essentials/Expressions/Core/Elsa.Expressions.Core.csproj src/essentials/Expressions/Core/packages.lock.json src/essentials/Expressions/Core/
COPY src/essentials/Locking/Core/Elsa.Locking.Core.csproj src/essentials/Locking/Core/packages.lock.json src/essentials/Locking/Core/
COPY src/essentials/Mediator/Core/Elsa.Mediator.Core.csproj src/essentials/Mediator/Core/packages.lock.json src/essentials/Mediator/Core/
COPY src/essentials/Persistence/Schema/Elsa.Persistence.Schema.csproj src/essentials/Persistence/Schema/packages.lock.json src/essentials/Persistence/Schema/
COPY src/essentials/Pipelines/Core/Elsa.Pipelines.Core.csproj src/essentials/Pipelines/Core/packages.lock.json src/essentials/Pipelines/Core/
COPY src/essentials/Primitives/Primitives/Elsa.Primitives.csproj src/essentials/Primitives/Primitives/packages.lock.json src/essentials/Primitives/Primitives/
COPY src/essentials/Serialization/Core/Elsa.Serialization.Core.csproj src/essentials/Serialization/Core/packages.lock.json src/essentials/Serialization/Core/
COPY src/essentials/Tasks/Core/Elsa.Tasks.Core.csproj src/essentials/Tasks/Core/packages.lock.json src/essentials/Tasks/Core/
# NU1603: tolerate a restore-only warning when a transitive preview dependency
# resolves to a newer available version; does not relax real code warnings.
RUN dotnet restore src/apps/Elsa.Foundation.Host/Elsa.Foundation.Host.csproj \
    --locked-mode \
    -p:WarningsNotAsErrors=NU1603

# Now the host and shared infrastructure sources.
COPY src/apps/Elsa.Foundation.Host/ src/apps/Elsa.Foundation.Host/
COPY src/essentials/Api/AspNetCore/ src/essentials/Api/AspNetCore/
COPY src/essentials/Attention/Core/ src/essentials/Attention/Core/
COPY src/essentials/Caching/Core/ src/essentials/Caching/Core/
COPY src/essentials/Cluster/Core/ src/essentials/Cluster/Core/
COPY src/essentials/Cluster/InProcess/ src/essentials/Cluster/InProcess/
COPY src/essentials/Cluster/Readability/ src/essentials/Cluster/Readability/
COPY src/essentials/Events/Core/ src/essentials/Events/Core/
COPY src/essentials/Expressions/Core/ src/essentials/Expressions/Core/
COPY src/essentials/Locking/Core/ src/essentials/Locking/Core/
COPY src/essentials/Mediator/Core/ src/essentials/Mediator/Core/
COPY src/essentials/Persistence/Schema/ src/essentials/Persistence/Schema/
COPY src/essentials/Pipelines/Core/ src/essentials/Pipelines/Core/
COPY src/essentials/Primitives/Primitives/ src/essentials/Primitives/Primitives/
COPY src/essentials/Serialization/Core/ src/essentials/Serialization/Core/
COPY src/essentials/Tasks/Core/ src/essentials/Tasks/Core/

# Package versions (ADR 0067, spec 150, #2084, #2126). The Docker Images workflow passes
# ELSA_PACKAGE_VERSIONS: the pack-properties file the version calculator writes for this
# commit, gzipped and base64-encoded onto one line, because the builder takes build args
# one per line. Both commands below read it through -p:CustomBeforeDirectoryBuildProps,
# which gives every Elsa assembly, and every Elsa package in the deps.json, the version
# the feed carries, and makes Elsa.Foundation.Host.csproj ship
# ComputedVersions/appsettings.Production.json, which declares the Line A shares
# and the host-composed ones host-provided. The restore has to see it too: deps.json takes a project reference's
# version from the restore, not the build, so a publish alone would stamp the assemblies
# and still record the dev versions. The restore above stays free of it and keeps its
# cache; this one only rewrites the assets file from the packages that stage downloaded.
#
# Left at its default, source-build, the image is built as a source build: dev versions
# and the committed appsettings.Production.json, which leaves the Elsa shares
# undeclared, since every feed package's range excludes a dev version. That is what
# `docker build` does directly. An empty value is refused: it is what the workflow passes
# when it lost the versions, and building on would ship an image that looks right and
# checks nothing.
ARG ELSA_PACKAGE_VERSIONS=source-build

RUN set -- \
    && case "$ELSA_PACKAGE_VERSIONS" in \
        source-build) ;; \
        "") echo "ELSA_PACKAGE_VERSIONS is empty. Pass the computed versions, or leave it unset for a source build." >&2; exit 1 ;; \
        *) mkdir -p /tmp/elsa \
            && printf '%s' "$ELSA_PACKAGE_VERSIONS" | base64 -d | gunzip > /tmp/elsa/package-versions.props \
            && set -- -p:CustomBeforeDirectoryBuildProps=/tmp/elsa/package-versions.props ;; \
    esac \
    && dotnet restore src/apps/Elsa.Foundation.Host/Elsa.Foundation.Host.csproj \
        --locked-mode \
        -p:WarningsNotAsErrors=NU1603 \
        "$@" \
    && dotnet publish src/apps/Elsa.Foundation.Host/Elsa.Foundation.Host.csproj \
        -c Release \
        --no-restore \
        -p:WarningsNotAsErrors=NU1603 \
        "$@" \
        -o /app/publish

# ---- Runtime stage ----------------------------------------------------------
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app

# curl gives the container a self-contained HTTP healthcheck (aspnet base ships none).
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*

# Kestrel listens on 8080 (aspnet base default).
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

COPY --from=build /app/publish ./

# Runtime write locations under the content root:
#   /app/packages   Nuplane directory feed (watched; mount a volume here to drop .nupkg files)
#   /app/.nuplane   Nuplane reconciliation state
# The non-root runtime user needs write access to them.
RUN mkdir -p /app/packages /app/.nuplane && chown -R $APP_UID:$APP_UID /app

# Liveness probe (process up + Kestrel serving). Deliberately /health/live, NOT
# /health/ready: readiness reports 503 while a shell is (re)activating, which is a
# normal transient state during a feed reconcile — it must not mark the container
# unhealthy.
HEALTHCHECK --interval=15s --timeout=3s --start-period=20s --retries=10 \
    CMD curl -fsS http://localhost:8080/health/live || exit 1

USER $APP_UID

ENTRYPOINT ["dotnet", "Elsa.Foundation.Host.dll"]
