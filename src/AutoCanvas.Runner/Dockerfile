# The AutoCanvas runner. Build from the repository root:
#   docker build -f src/AutoCanvas.Runner/Dockerfile -t autocanvas-runner .
# It starts job containers through the Docker socket it is given; see docs/user/runners.md.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY global.json nuget.config Directory.Build.props Directory.Packages.props .editorconfig ./
COPY proto/ proto/
COPY src/AutoCanvas.Core/ src/AutoCanvas.Core/
COPY src/AutoCanvas.Protocol/ src/AutoCanvas.Protocol/
COPY src/AutoCanvas.Runner/ src/AutoCanvas.Runner/
RUN dotnet restore src/AutoCanvas.Runner/AutoCanvas.Runner.csproj --locked-mode
RUN dotnet publish src/AutoCanvas.Runner/AutoCanvas.Runner.csproj --configuration Release --no-restore --output /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0
ENV AUTOCANVAS_RUNNER__DATADIRECTORY=/var/lib/autocanvas-runner
RUN mkdir -p /var/lib/autocanvas-runner && chown app:app /var/lib/autocanvas-runner
VOLUME /var/lib/autocanvas-runner
WORKDIR /app
COPY --from=build /app .
USER app
ENTRYPOINT ["dotnet", "AutoCanvas.Runner.dll"]
