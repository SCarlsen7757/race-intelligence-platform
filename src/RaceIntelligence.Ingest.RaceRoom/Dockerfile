# The RaceRoom ingest API, plus the migration bundle that prepares its database.
#
# Build from the REPOSITORY ROOT, not from this directory:
#   docker build -f src/RaceIntelligence.Ingest.RaceRoom/Dockerfile --target app .
#
# Two targets come out of this file. `app` is the service; `migrate` is a one-shot image holding a
# self-contained EF Core migration bundle. They share the restore layer because they share every
# input up to it, and splitting them into two Dockerfiles would mean restoring the same graph twice.
#
# Migrations are a separate image rather than something the API does at startup because that is what
# the code already decided: RaceIntelligence.Ingest.RaceRoom/Program.cs applies them only in
# Development. Keeping that split means a failed migration is a container that exited non-zero with
# its own logs, not an API in a restart loop, and it stays correct if the API is ever scaled past one
# replica.

# global.json pins the SDK to 10.0.302 with rollForward: latestFeature, so this must be a .NET 10 SDK.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS restore
WORKDIR /src

# The build props, the central package versions and the SDK pin come first and alone. This repo uses
# central package management, so a restore without Directory.Packages.props fails claiming every
# PackageReference is missing a version. Copying these before the sources keeps the restore layer
# cached across ordinary code edits.
COPY global.json Directory.Build.props Directory.Packages.props ./
COPY src/ ./src/

RUN dotnet restore src/RaceIntelligence.Ingest.RaceRoom/RaceIntelligence.Ingest.RaceRoom.csproj

# ---- The migration bundle -------------------------------------------------------------------
#
# `migrations bundle` produces a standalone executable carrying the migrations and just enough EF
# Core to apply them. That is the reason for the extra stage: the alternative, shipping the SDK and
# `dotnet ef` to the server so it can run `database update`, means a multi-hundred-megabyte image
# and a toolchain in the deployed environment. The bundle needs neither.
FROM restore AS bundle-build
# Pinned to the EF Core version in Directory.Packages.props. An unpinned `dotnet tool install` would
# silently drift to whatever is newest and generate a bundle against a different EF than the one the
# migrations were written with.
RUN dotnet tool install --global dotnet-ef --version 10.0.11
ENV PATH="$PATH:/root/.dotnet/tools"

# A second restore, this one RID-specific. The shared restore layer above is RID-agnostic, and a
# self-contained bundle needs assets for a concrete runtime: without this the bundle fails with
# NETSDK1047, "assets file doesn't have a target for net10.0/linux-x64", which `dotnet ef` then
# reports as the much less helpful "Unable to retrieve project metadata".
RUN dotnet restore src/RaceIntelligence.Persistence.RaceRoom/RaceIntelligence.Persistence.RaceRoom.csproj \
        --runtime linux-x64

# --self-contained so the runtime image needs no .NET at all.
#
# RaceRoomDbContextFactory (an IDesignTimeDbContextFactory) is why no --startup-project is needed:
# the persistence project is a class library with no Program.cs, and the factory is what lets the
# tooling construct a context without one. Its connection string is a design-time placeholder only;
# the real one is passed to the bundle at run time with --connection.
RUN dotnet ef migrations bundle \
        --project src/RaceIntelligence.Persistence.RaceRoom/RaceIntelligence.Persistence.RaceRoom.csproj \
        --configuration Release \
        --self-contained \
        --runtime linux-x64 \
        --output /app/efbundle \
        --verbose

FROM mcr.microsoft.com/dotnet/runtime-deps:10.0 AS migrate

# Npgsql probes for GSSAPI/Kerberos while opening a connection. Without this library the probe fails
# and prints a bare "Error: libgssapi_krb5.so.2: cannot open shared object file" before carrying on
# and migrating perfectly well. Harmless, but it is the first thing a deploy prints and it reads
# exactly like the migration failed. Installing the library costs a few hundred kilobytes and buys a
# log in which an error line actually means an error.
RUN apt-get update \
    && apt-get install -y --no-install-recommends libgssapi-krb5-2 \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /app
COPY --from=bundle-build /app/efbundle ./efbundle
# No ENTRYPOINT with a baked connection string — compose supplies it, so the secret stays out of the
# image layers. See compose.test.yml for the invocation.
ENTRYPOINT ["/app/efbundle"]

# ---- The API --------------------------------------------------------------------------------
FROM restore AS app-build
RUN dotnet publish src/RaceIntelligence.Ingest.RaceRoom/RaceIntelligence.Ingest.RaceRoom.csproj \
        --configuration Release \
        --no-restore \
        --output /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS app

# The same Npgsql GSSAPI probe as the migrate stage above, and this image lacks the library for the
# same reason — aspnet is runtime-deps plus the .NET runtime, nothing more. Here the error surfaces
# on the first database connection, which is to say the first telemetry of a session: the worst
# possible moment to be reading a false alarm in a log.
RUN apt-get update \
    && apt-get install -y --no-install-recommends libgssapi-krb5-2 \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /app
COPY --from=app-build /app/publish ./

# Set explicitly rather than relying on the image default. Nothing in this repo configures Kestrel
# or sets ASPNETCORE_URLS, so leaving it implicit would make the port a property of whichever base
# image tag happened to be pulled.
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

# Not root. The API needs no write access to its own filesystem — it writes to PostgreSQL.
USER $APP_UID

ENTRYPOINT ["dotnet", "RaceIntelligence.Ingest.RaceRoom.dll"]
