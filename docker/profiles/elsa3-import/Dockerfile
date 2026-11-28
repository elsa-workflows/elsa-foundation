# syntax=docker/dockerfile:1

ARG ELSA_VERSION=specify-release
ARG PROFILE_BUILDER_IMAGE=elsaworkflows/elsa-foundation-host:builder-${ELSA_VERSION}
ARG FOUNDATION_IMAGE=elsaworkflows/elsa-foundation-host:${ELSA_VERSION}

FROM ${PROFILE_BUILDER_IMAGE} AS profile-builder
WORKDIR /profile
COPY packages.txt ./

FROM profile-builder AS resolve-locks
RUN elsa-profile assemble --output /out

FROM scratch AS locks
COPY --from=resolve-locks /out/packages.lock.json /out/nuplane.lock.json /

FROM profile-builder AS prepare
COPY packages.lock.json nuplane.lock.json ./
RUN elsa-profile assemble --locked --output /out

FROM ${FOUNDATION_IMAGE}
ARG FOUNDATION_IMAGE
LABEL org.opencontainers.image.base.name="${FOUNDATION_IMAGE}"
RUN --mount=type=bind,from=prepare,source=/out/foundation-base.sha256,target=/tmp/foundation-base.sha256 \
    sha256sum -c /tmp/foundation-base.sha256 && mkdir -p /app/data
ENV ConnectionStrings__Elsa="Data Source=/app/data/elsa.db"
COPY --from=prepare /out/app/ /app/
COPY shells.json /app/shells.json
