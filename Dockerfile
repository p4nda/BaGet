SHELL ["/bin/bash", "-eux", "-o", "pipefail"]

ARG TARGETARCH="linux-x64"
ARG BUILD_TIMESTAMP="yyyyMMdd-HHmmss"
ARG CONTAINER_USER="baget"
ARG CONTAINER_GID=1001
ARG CONTAINER_UID=1001

# Base image
FROM quay.io/rockylinux/rockylinux:9-ubi AS base
ARG CONTAINER_USER
ARG CONTAINER_UID
ARG CONTAINER_GID

# Install .NET and other dependencies
RUN dnf install -y --setopt=install_weak_deps=false \
      ca-certificates fontconfig unzip openssl hostname vi shadow-utils policycoreutils \
      dotnet-runtime-8.0 aspnetcore-runtime-8.0 && \
    dnf clean all && \
    rm -f /usr/bin/dotnet && ln -s /usr/lib64/dotnet/dotnet /usr/bin/dotnet && \
    dotnet --list-runtimes && dotnet --info

# OS Setup
RUN echo -e "LANG=en_US.utf8\nTZ=Europe/Prague\nLC_TIME=en_GB.utf8" > /etc/locale.conf && \
    echo -e "\nexport LANG=en_US.utf8\nexport TZ=Europe/Prague\nexport LC_TIME=en_GB.UTF-8" >> ~/.bash_profile && \
    groupadd -g ${CONTAINER_GID} ${CONTAINER_USER} && \
    useradd -u ${CONTAINER_UID} -g ${CONTAINER_GID} -d /home/${CONTAINER_USER} -s /bin/bash -c 'BaGet Server' ${CONTAINER_USER}

WORKDIR "/home/${CONTAINER_USER}"
RUN mkdir conf database logs packages /app /app/cert

# Build the project
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY /src .
COPY /LICENSE \
        /CODE_OF_CONDUCT.md \
        /CONTRIBUTING.md \
        /nuget.config \
        /src/
RUN dotnet restore BaGet
RUN dotnet build BaGet -c Release -o /app

# .NET publish
FROM build AS publish
ARG TARGETARCH
RUN dotnet publish BaGet \
    -p DebugType=none \
    -p DebugSymbols=false \
    -p UseAppHost=false \
    -p GenerateDocumentationFile=false \
    -p PublishReadyToRun=true \
    -p PublishProfile=PROD \
    -a ${TARGETARCH} \
    -c Release -o /app

RUN find /app -type d -exec chmod 0550 {} + && \
    find /app -type f -exec chmod 0440 {} +

# Copy .NET app to the final image
FROM base AS final
ARG BUILD_TIMESTAMP
ARG CONTAINER_USER
ARG CONTAINER_UID

    # Using appsettings.Production.json from the mounted volume
ENV BAGET_CONF_DIR="/home/${CONTAINER_USER}/conf" \
    # Ideally log to the mounted volume
    BAGET_LOGS_DIR="/home/${CONTAINER_USER}/logs" \
    # Set longuague and timezone
    LANG='en_US.UTF-8' LC_TIME='en_GB.UTF-8' TZ='Europe/Prague' \
    # .NET Core environment variables
    DOTNET_CLI_TELEMETRY_OPTOUT=true \
    DOTNET_PRINT_TELEMETRY_MESSAGE=false \
    DOTNET_SKIP_FIRST_TIME_EXPERIENCE=true \
    DOTNET_USE_POLLING_FILE_WATCHER=true \
    DOTNET_RUNNING_IN_CONTAINER=true \
    NUGET_XMLDOC_MODE=skip \
    ASPNETCORE_ENVIRONMENT="Production" \
    DOTNET_ENVIRONMENT="Production"

LABEL org.opencontainers.image.source="https://github.com/p4nda/BaGet"

WORKDIR /home/${CONTAINER_USER}
COPY --from=publish --chown=${CONTAINER_UID}:${CONTAINER_GID} /app /app
COPY --from=build --chown=${CONTAINER_UID}:${CONTAINER_GID} \
    /src/LICENSE \
    /src/CODE_OF_CONDUCT.md \
    /src/CONTRIBUTING.md \
    /app/
    # Cleanup unnecessary packages
RUN dnf remove -y shadow-utils policycoreutils \
      langpacks-en langpacks-core-en langpacks-core-font-en dejavu-sans-fonts && \
    dnf clean all && \
    find /var/log -type f -exec truncate --size=0 {} \; && \
    rm -f /root/anaconda-ks.cfg /root/original-ks.cfg /root/anaconda-post.log && \
    rm -Rf /var/cache/* /usr/share/misc/magic && \
    # Drop permissions
    echo "${BUILD_TIMESTAMP}" > VERSION && \
    chown -R ${CONTAINER_USER}:0 /app /home/${CONTAINER_USER} && \
    chmod 0770 database logs packages && \
    chmod 0550 conf /app /app/cert && \
    # Ditch app configs, we will use config(s) from the mounted volume
    rm -f /app/appsettings.Production.json && \
    rm -f /app/appsettings.Development.json

# baget.pfx
VOLUME ["/app/cert"]
# appsettings.Production.json
VOLUME ["/home/${CONTAINER_USER}/conf"]
# Persist baget.db if not using external database
VOLUME ["/home/${CONTAINER_USER}/database"]
# NuGet packages fetched through BaGet
VOLUME ["/home/${CONTAINER_USER}/packages"]
# Preserve logs
VOLUME ["/home/${CONTAINER_USER}/logs"]

USER ${CONTAINER_UID}
ENTRYPOINT ["dotnet", "/app/BaGet.dll"]
#EXPOSE 8080
EXPOSE 8081
