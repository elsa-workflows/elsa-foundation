# syntax=docker/dockerfile:1

ARG ELSA_VERSION=specify-release
ARG PROFILE_BUILDER_IMAGE=elsaworkflows/elsa-foundation-host:builder-${ELSA_VERSION}
ARG FOUNDATION_IMAGE=elsaworkflows/elsa-foundation-host:${ELSA_VERSION}

FROM ${PROFILE_BUILDER_IMAGE} AS profile-builder
WORKDIR /profile
COPY packages.txt ./
COPY settings/ ./settings/

FROM profile-builder AS resolve-locks
RUN elsa-profile assemble --allow-add-ons --appsettings-directory settings --output /out

FROM scratch AS locks
COPY --from=resolve-locks /out/packages.lock.json /out/nuplane.lock.json /

FROM profile-builder AS prepare
COPY packages.lock.json nuplane.lock.json ./
RUN elsa-profile assemble --allow-add-ons --appsettings-directory settings --locked --output /out

FROM ${FOUNDATION_IMAGE}
ARG FOUNDATION_IMAGE
LABEL org.opencontainers.image.base.name="${FOUNDATION_IMAGE}"
RUN --mount=type=bind,from=prepare,source=/out/foundation-base.sha256,target=/tmp/foundation-base.sha256 \
    sha256sum -c /tmp/foundation-base.sha256 && mkdir -p /app/data
COPY --from=prepare /out/app/ /app/
COPY --chown=$APP_UID:$APP_UID --from=prepare /out/app/appsettings.json /app/appsettings.json
COPY --chown=$APP_UID:$APP_UID shells.json /app/shells.json
COPY shells.Production.json /app/
COPY settings/appsettings.Development.json /app/
