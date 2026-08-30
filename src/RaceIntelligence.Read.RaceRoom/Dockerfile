# The RaceRoom read API: the exposed, read-only half of RaceRoom's storage.
#
# Build from the REPOSITORY ROOT, not from this directory:
#   docker build -f src/RaceIntelligence.Read.RaceRoom/Dockerfile .
#
# One target, unlike the ingest Dockerfile's two. There is no migration bundle here because this
# service does not own RaceRoom's schema and must not create it — the ingest side owns the
# migrations (ADR 0001), and compose sequences this service after that bundle has run.

# global.json pins the SDK to 10.0.302 with rollForward: latestFeature, so this must be a .NET 10 SDK.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Central package management: a restore without Directory.Packages.props fails claiming every
# PackageReference is missing a version. These come first and alone so the restore layer survives
# ordinary code edits.
COPY global.json Directory.Build.props Directory.Packages.props ./
COPY src/ ./src/

RUN dotnet restore src/RaceIntelligence.Read.RaceRoom/RaceIntelligence.Read.RaceRoom.csproj

RUN dotnet publish src/RaceIntelligence.Read.RaceRoom/RaceIntelligence.Read.RaceRoom.csproj \
        --configuration Release \
        --no-restore \
        --output /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS app

# Npgsql probes for GSSAPI/Kerberos while opening a connection, and the aspnet image does not carry
# the library. Without it the first database connection prints a bare
# "libgssapi_krb5.so.2: cannot open shared object file" and then works perfectly well — an error
# line that means nothing, printed at the moment someone is looking for one that does.
RUN apt-get update \
    && apt-get install -y --no-install-recommends libgssapi-krb5-2 \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /app
COPY --from=build /app/publish ./

# Set explicitly rather than relying on the image default. Nothing in this repo configures Kestrel
# or sets ASPNETCORE_URLS, so leaving it implicit would make the port a property of whichever base
# image tag happened to be pulled.
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

# Not root. This service writes nothing at all — not to its filesystem and not to the database.
USER $APP_UID

ENTRYPOINT ["dotnet", "RaceIntelligence.Read.RaceRoom.dll"]
