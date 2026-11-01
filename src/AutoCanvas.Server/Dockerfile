# The AutoCanvas server with its web UI. Build from the repository root:
#   docker build -f src/AutoCanvas.Server/Dockerfile -t autocanvas-server .
# Serves the UI and the HTTP API on 5080 and runners on 5081; keeps its data (the SQLite database by
# default) in /var/lib/autocanvas. See docs/technical/operations.md for the settings.

# The web UI (ADR-0018), built into the server's wwwroot.
FROM node:24-alpine AS web
WORKDIR /src
COPY build/allowed-licenses.json build/
COPY openapi/ openapi/
COPY src/web/package.json src/web/package-lock.json src/web/.npmrc src/web/
RUN cd src/web && npm ci --no-audit --no-fund
COPY src/web/ src/web/
RUN cd src/web && npm run build

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY global.json nuget.config Directory.Build.props Directory.Packages.props .editorconfig ./
COPY proto/ proto/
COPY src/AutoCanvas.Core/ src/AutoCanvas.Core/
COPY src/AutoCanvas.Reports/ src/AutoCanvas.Reports/
COPY src/AutoCanvas.Persistence/ src/AutoCanvas.Persistence/
COPY src/AutoCanvas.Persistence.Postgres/ src/AutoCanvas.Persistence.Postgres/
COPY src/AutoCanvas.Persistence.Sqlite/ src/AutoCanvas.Persistence.Sqlite/
COPY src/AutoCanvas.Protocol/ src/AutoCanvas.Protocol/
COPY src/AutoCanvas.Server.Application/ src/AutoCanvas.Server.Application/
COPY src/AutoCanvas.Server/ src/AutoCanvas.Server/
COPY --from=web /src/src/AutoCanvas.Server/wwwroot/ src/AutoCanvas.Server/wwwroot/
RUN dotnet restore src/AutoCanvas.Server/AutoCanvas.Server.csproj --locked-mode
RUN dotnet publish src/AutoCanvas.Server/AutoCanvas.Server.csproj --configuration Release --no-restore --output /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0
ENV AUTOCANVAS_URLS=http://+:5080 \
    AUTOCANVAS_RUNNER_URLS=http://+:5081 \
    AUTOCANVAS_Server__DataDirectory=/var/lib/autocanvas
RUN mkdir -p /var/lib/autocanvas && chown app:app /var/lib/autocanvas
VOLUME /var/lib/autocanvas
EXPOSE 5080 5081
WORKDIR /app
COPY --from=build /app .
USER app
ENTRYPOINT ["dotnet", "AutoCanvas.Server.dll"]
