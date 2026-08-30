# The live hub — the WebSocket publish/view service the dashboard and collector both talk to.
#
# Build from the REPOSITORY ROOT, not from this directory:
#   docker build -f src/RaceIntelligence.Web/Dockerfile .
#
# No migration stage here, unlike the ingest API: the hub holds no database credentials by design.
# Everything it serves is in memory and arrives over the publishing socket, so there is no schema
# for it to own.

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Central package management: a restore without Directory.Packages.props fails claiming every
# PackageReference is missing a version.
COPY global.json Directory.Build.props Directory.Packages.props ./
# channels/ is source, not data: the RaceRoom channel manifest is an AdditionalFiles item that
# a Roslyn generator reads at compile time, so a build without it fails with CS2001 rather than
# producing a smaller image. It lives outside src/ because it describes a simulator rather than
# a project — see CLAUDE.md.
COPY channels/ ./channels/
COPY src/ ./src/

RUN dotnet restore src/RaceIntelligence.Web/RaceIntelligence.Web.csproj
RUN dotnet publish src/RaceIntelligence.Web/RaceIntelligence.Web.csproj \
        --configuration Release \
        --no-restore \
        --output /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS app
WORKDIR /app
COPY --from=build /app/publish ./

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

USER $APP_UID

# Live:ApiKey and Live:AllowedOrigins are [Required] and validated with ValidateOnStart, so this
# container refuses to boot without Live__ApiKey and Live__AllowedOrigins__0 in its environment.
# That is deliberate — a hub that accepted every origin because a setting was forgotten would defeat
# the setting's entire purpose. See compose.test.yml.
ENTRYPOINT ["dotnet", "RaceIntelligence.Web.dll"]
