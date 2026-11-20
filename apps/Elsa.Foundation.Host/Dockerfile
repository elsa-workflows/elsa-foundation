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
# (Elsa.Cluster.Core, Elsa.Persistence.Schema) it shares too (#2143), and the
# opt-in EF membership provider with EF Core and the four engines (#2151) —
# none of which brings a feature implementation into this host — so its
# project and their sources must also be present in the Docker build context.
# Example:
#
#   docker build -f apps/Elsa.Foundation.Host/Dockerfile \
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
# .editorconfig carries the naming analyzer's per-domain retired terms, so the image compiles under the same
# naming rules as CI.
COPY *.props *.targets .editorconfig NuGet.config ./

# Restore inputs: the host, its shared endpoint-metadata project, Line A, its cluster membership, the EF closure that membership's
# durable provider carries (Elsa.Persistence.EntityFramework plus the EF Core packages, #2151), its Data Protection key store,
# which carries the same closure (#2191), and the computed-production-settings
# swap Elsa.Foundation.Host.csproj imports from apps/ (shared with Elsa.Workbench.csproj, #2126) -- MSBuild
# treats a missing <Import> as an error, not a no-op the way it does for the root files above, so this one has
# to be copied before restore too, or restore fails outright with MSB4019 rather than silently.
# Each project comes with its committed NuGet lock file, and both restores below are locked (#2122): they
# restore exactly the versions those files name, and fail when one no longer matches its project or names a
# version the feed no longer has. A project whose lock file is missing would restore unlocked without a word,
# which is why HostEndpointMetadataTests requires each one here. See docs/reference/nuget-lock-files.md.
COPY apps/ComputedProductionSettings.targets apps/
COPY apps/Elsa.Foundation.Host/Elsa.Foundation.Host.csproj apps/Elsa.Foundation.Host/packages.lock.json apps/Elsa.Foundation.Host/
COPY essentials/src/Api/AspNetCore/Elsa.Api.AspNetCore.csproj essentials/src/Api/AspNetCore/packages.lock.json essentials/src/Api/AspNetCore/
COPY essentials/src/Attention/Core/Elsa.Attention.Core.csproj essentials/src/Attention/Core/packages.lock.json essentials/src/Attention/Core/
COPY essentials/src/Caching/Core/Elsa.Caching.Core.csproj essentials/src/Caching/Core/packages.lock.json essentials/src/Caching/Core/
COPY essentials/src/Cluster/Core/Elsa.Cluster.Core.csproj essentials/src/Cluster/Core/packages.lock.json essentials/src/Cluster/Core/
COPY essentials/src/Cluster/EntityFrameworkCore/Elsa.Cluster.EntityFrameworkCore.csproj essentials/src/Cluster/EntityFrameworkCore/packages.lock.json essentials/src/Cluster/EntityFrameworkCore/
COPY essentials/src/Cluster/InProcess/Elsa.Cluster.InProcess.csproj essentials/src/Cluster/InProcess/packages.lock.json essentials/src/Cluster/InProcess/
COPY essentials/src/Cluster/Readability/Elsa.Cluster.Readability.csproj essentials/src/Cluster/Readability/packages.lock.json essentials/src/Cluster/Readability/
COPY essentials/src/Events/Core/Elsa.Events.Core.csproj essentials/src/Events/Core/packages.lock.json essentials/src/Events/Core/
COPY essentials/src/Expressions/Core/Elsa.Expressions.Core.csproj essentials/src/Expressions/Core/packages.lock.json essentials/src/Expressions/Core/
COPY essentials/src/Foundation/DataProtection/EntityFrameworkCore/Elsa.Foundation.DataProtection.EntityFrameworkCore.csproj essentials/src/Foundation/DataProtection/EntityFrameworkCore/packages.lock.json essentials/src/Foundation/DataProtection/EntityFrameworkCore/
COPY essentials/src/Locking/Core/Elsa.Locking.Core.csproj essentials/src/Locking/Core/packages.lock.json essentials/src/Locking/Core/
COPY essentials/src/Mediator/Core/Elsa.Mediator.Core.csproj essentials/src/Mediator/Core/packages.lock.json essentials/src/Mediator/Core/
COPY essentials/src/Persistence/EntityFramework/Elsa.Persistence.EntityFramework.csproj essentials/src/Persistence/EntityFramework/packages.lock.json essentials/src/Persistence/EntityFramework/
COPY essentials/src/Persistence/Core/Elsa.Persistence.Core.csproj essentials/src/Persistence/Core/packages.lock.json essentials/src/Persistence/Core/
COPY essentials/src/Persistence/Schema/Elsa.Persistence.Schema.csproj essentials/src/Persistence/Schema/packages.lock.json essentials/src/Persistence/Schema/
COPY essentials/src/Pipelines/Core/Elsa.Pipelines.Core.csproj essentials/src/Pipelines/Core/packages.lock.json essentials/src/Pipelines/Core/
COPY essentials/src/Primitives/Primitives/Elsa.Primitives.csproj essentials/src/Primitives/Primitives/packages.lock.json essentials/src/Primitives/Primitives/
COPY essentials/src/Serialization/Core/Elsa.Serialization.Core.csproj essentials/src/Serialization/Core/packages.lock.json essentials/src/Serialization/Core/
COPY essentials/src/Tasks/Core/Elsa.Tasks.Core.csproj essentials/src/Tasks/Core/packages.lock.json essentials/src/Tasks/Core/
# The naming analyzer every production project compiles with: Directory.Build.props adds it to the host and each
# project above, so it restores with them, and a build without it fails (ELSABUILD001).
COPY essentials/build/Analyzers/Elsa.Architecture.Analyzers.csproj essentials/build/Analyzers/packages.lock.json essentials/build/Analyzers/
# NU1603: tolerate a restore-only warning when a transitive preview dependency
# resolves to a newer available version; does not relax real code warnings.
RUN dotnet restore apps/Elsa.Foundation.Host/Elsa.Foundation.Host.csproj \
    --locked-mode \
    -p:WarningsNotAsErrors=NU1603

# Now the host and shared infrastructure sources.
COPY apps/Elsa.Foundation.Host/ apps/Elsa.Foundation.Host/
COPY essentials/src/Api/AspNetCore/ essentials/src/Api/AspNetCore/
COPY essentials/src/Attention/Core/ essentials/src/Attention/Core/
COPY essentials/src/Caching/Core/ essentials/src/Caching/Core/
COPY essentials/src/Cluster/Core/ essentials/src/Cluster/Core/
COPY essentials/src/Cluster/EntityFrameworkCore/ essentials/src/Cluster/EntityFrameworkCore/
COPY essentials/src/Cluster/InProcess/ essentials/src/Cluster/InProcess/
COPY essentials/src/Cluster/Readability/ essentials/src/Cluster/Readability/
COPY essentials/src/Events/Core/ essentials/src/Events/Core/
COPY essentials/src/Expressions/Core/ essentials/src/Expressions/Core/
COPY essentials/src/Foundation/DataProtection/EntityFrameworkCore/ essentials/src/Foundation/DataProtection/EntityFrameworkCore/
COPY essentials/src/Locking/Core/ essentials/src/Locking/Core/
COPY essentials/src/Mediator/Core/ essentials/src/Mediator/Core/
COPY essentials/src/Persistence/EntityFramework/ essentials/src/Persistence/EntityFramework/
COPY essentials/src/Persistence/Core/ essentials/src/Persistence/Core/
COPY essentials/src/Persistence/Schema/ essentials/src/Persistence/Schema/
COPY essentials/src/Pipelines/Core/ essentials/src/Pipelines/Core/
COPY essentials/src/Primitives/Primitives/ essentials/src/Primitives/Primitives/
COPY essentials/src/Serialization/Core/ essentials/src/Serialization/Core/
COPY essentials/src/Tasks/Core/ essentials/src/Tasks/Core/
COPY essentials/build/Analyzers/ essentials/build/Analyzers/

# Package versions (ADR 0067, spec 150, #2084, #2126). The Docker Images workflow passes
# ELSA_PACKAGE_VERSIONS: the pack-properties file the version calculator writes for this
# commit, gzipped and base64-encoded onto one line, because the builder takes build args
# one per line. Both commands below read it through -p:CustomBeforeDirectoryBuildProps,
# which gives every Elsa assembly, and every Elsa package in the deps.json, the version
# the feed carries, and makes Elsa.Foundation.Host.csproj ship
# ComputedVersions/appsettings.Production.json, which declares the Line A shares
# and the host-composed ones (the cluster contracts, Elsa.Persistence.EntityFramework and the EF Core packages) host-provided. The restore has to see it too: deps.json takes a project reference's
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
    && dotnet restore apps/Elsa.Foundation.Host/Elsa.Foundation.Host.csproj \
        --locked-mode \
        -p:WarningsNotAsErrors=NU1603 \
        "$@" \
    && dotnet publish apps/Elsa.Foundation.Host/Elsa.Foundation.Host.csproj \
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
# The non-root runtime user needs write access to them. The Data Protection key ring is not among them: unless
# Elsa:DataProtection:EntityFrameworkCore:Enabled keeps it in the platform database, ASP.NET Core keeps it in the
# runtime user's home directory, which no volume covers, so a recreated container signs everyone out
# (docs/docker.md, "Data Protection keys").
RUN mkdir -p /app/packages /app/.nuplane && chown -R $APP_UID:$APP_UID /app

# Liveness probe (process up + Kestrel serving). Deliberately /health/live, NOT
# /health/ready: readiness reports 503 while a shell is (re)activating, which is a
# normal transient state during a feed reconcile — it must not mark the container
# unhealthy.
HEALTHCHECK --interval=15s --timeout=3s --start-period=20s --retries=10 \
    CMD curl -fsS http://localhost:8080/health/live || exit 1

USER $APP_UID

ENTRYPOINT ["dotnet", "Elsa.Foundation.Host.dll"]
