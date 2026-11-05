# The AutoCanvas runner. Build from the repository root:
#   docker build -f src/AutoCanvas.Runner/Dockerfile -t autocanvas-runner #     --build-arg SOURCE_REVISION=$(git rev-parse HEAD) .
# SOURCE_REVISION puts the commit into the runner's version; the build context has no .git.
# It starts job containers through the Docker socket it is given; see docs/user/runners.md.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY global.json nuget.config Directory.Build.props Directory.Packages.props .editorconfig ./
COPY proto/ proto/
COPY src/AutoCanvas.Core/ src/AutoCanvas.Core/
COPY src/AutoCanvas.Protocol/ src/AutoCanvas.Protocol/
COPY src/AutoCanvas.Runner/ src/AutoCanvas.Runner/
RUN dotnet restore src/AutoCanvas.Runner/AutoCanvas.Runner.csproj --locked-mode
ARG SOURCE_REVISION=
RUN dotnet publish src/AutoCanvas.Runner/AutoCanvas.Runner.csproj --configuration Release --no-restore --output /app     -p:SourceRevisionId="$SOURCE_REVISION"

FROM mcr.microsoft.com/dotnet/aspnet:10.0
ARG SOURCE_REVISION=
LABEL org.opencontainers.image.source=https://github.com/Bexy315/autocanvas       org.opencontainers.image.licenses=MIT       org.opencontainers.image.revision=$SOURCE_REVISION
ENV AUTOCANVAS_RUNNER__DATADIRECTORY=/var/lib/autocanvas-runner
# The data directory, and where the bundled runner of the compose package finds its registration
# token (ADR-0020).
RUN mkdir -p /var/lib/autocanvas-runner /var/lib/autocanvas-handoff \
    && chown app:app /var/lib/autocanvas-runner /var/lib/autocanvas-handoff
VOLUME /var/lib/autocanvas-runner
WORKDIR /app
COPY --from=build /app .
USER app
ENTRYPOINT ["dotnet", "AutoCanvas.Runner.dll"]
