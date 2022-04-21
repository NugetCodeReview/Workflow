# See here for image contents: https://github.com/microsoft/vscode-dev-containers/tree/v0.231.6/containers/dotnet/.devcontainer/base.Dockerfile

ARG VARIANT="6.0-bullseye-slim"
FROM mcr.microsoft.com/vscode/devcontainers/dotnet:0-${VARIANT}

LABEL org.opencontainers.image.title ="Nuget Code Review"
LABEL org.opencontainers.image.authors="ninja@thesharp.ninja"

ARG NODE_VERSION="none"
RUN if [ "${NODE_VERSION}" != "none" ]; then su vscode -c "umask 0002 && . /usr/local/share/nvm/nvm.sh && nvm install ${NODE_VERSION} 2>&1"; fi

RUN mkdir -p /home/vscode/.aspnet/https
COPY ./https/* /home/vscode/.aspnet/https/

RUN apt-get update && export DEBIAN_FRONTEND=noninteractive && apt upgrade -y

RUN dotnet tool install --global dotnet-generate

# [Optional] Uncomment this line to install global node packages.
# RUN su vscode -c "source /usr/local/share/nvm/nvm.sh && npm install -g <your-package-here>" 2>&1