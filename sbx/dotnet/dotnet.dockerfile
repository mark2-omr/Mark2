# syntax=docker/dockerfile:1
# The .NET SDK as an overlay: installed with dotnet-install.sh in a build
# stage, staged under /out, and copied onto scratch so it lands on the
# workload's base. The SDK is a portable glibc build, so the build stage
# does not have to match the workload's distribution.
FROM debian:trixie-slim AS build
ARG DOTNET_SDK_VERSION
RUN apt-get update \
 && apt-get install -y --no-install-recommends ca-certificates curl \
 && rm -rf /var/lib/apt/lists/*

# The version check runs with invariant globalization because this stage has
# no ICU; the composed sandbox gets ICU from the install hook in dotnet.yaml.
RUN set -eux; \
    curl -fsSL https://dot.net/v1/dotnet-install.sh -o /tmp/dotnet-install.sh; \
    bash /tmp/dotnet-install.sh --version "${DOTNET_SDK_VERSION}" \
      --install-dir /out/usr/local/share/dotnet --no-path; \
    rm /tmp/dotnet-install.sh; \
    test "$(DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1 DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1 \
      /out/usr/local/share/dotnet/dotnet --version)" = "${DOTNET_SDK_VERSION}"; \
    mkdir -p /out/usr/local/bin; \
    ln -s /usr/local/share/dotnet/dotnet /out/usr/local/bin/dotnet; \
    chown -R 0:0 /out

# The overlay: the SDK and the dotnet command, nothing under /home.
FROM scratch
COPY --from=build /out /

# Environment on the final stage: a mixin's ENV merges into the composed
# image and reaches the agent process. PATH is appended to the base's, so
# dotnet global tools (dotnet tool install -g) are on PATH.
ENV DOTNET_ROOT=/usr/local/share/dotnet \
    DOTNET_CLI_TELEMETRY_OPTOUT=1 \
    DOTNET_NOLOGO=1 \
    DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1 \
    NUGET_PACKAGES=/home/agent/.nuget/packages
ENV PATH="${PATH}:/home/agent/.dotnet/tools"
