# syntax=docker/dockerfile:1
# check=skip=InvalidDefaultArgInFrom

ARG FOUNDATION_IMAGE
FROM ${FOUNDATION_IMAGE} AS foundation

FROM mcr.microsoft.com/dotnet/sdk:10.0
ARG FOUNDATION_IMAGE
LABEL io.elsaworkflows.profile.foundation-image="${FOUNDATION_IMAGE}"

RUN apt-get update \
    && apt-get install -y --no-install-recommends python3 \
    && rm -rf /var/lib/apt/lists/*

ENV DOTNET_NOLOGO=1 DOTNET_CLI_TELEMETRY_OPTOUT=1
COPY global.json NuGet.config /opt/elsa-profile/
COPY docker/profiles/prepare.py /opt/elsa-profile/elsa-profile
COPY docker/profiles/packs/ /opt/elsa-profile/packs/
COPY --from=versions /package-versions.props /opt/elsa-profile/
COPY --from=foundation /app/Elsa.Foundation.Host.deps.json /app/Elsa.Foundation.Host.dll \
    /app/appsettings.json /app/appsettings.Production.json /opt/elsa-profile/foundation/
RUN chmod 0755 /opt/elsa-profile/elsa-profile \
    && ln -s /opt/elsa-profile/elsa-profile /usr/local/bin/elsa-profile

WORKDIR /profile
ENTRYPOINT ["elsa-profile"]
