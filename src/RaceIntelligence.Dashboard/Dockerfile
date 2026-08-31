# The dashboard — a TanStack Start app on Node, its own service on its own origin.
#
# Build from the REPOSITORY ROOT, not from this directory:
#   docker build -f src/RaceIntelligence.Dashboard/Dockerfile .
#
# ---------------------------------------------------------------------------------------------
# THIS BUILD TAKES NO DEPLOYMENT-SPECIFIC ARGUMENTS. The image is the same everywhere.
#
# HUB_URL and READ_URL are RUNTIME variables, set in compose's `environment:`. server.mjs reads them
# at startup and injects them into the document, so one image serves any deployment and repointing
# it is a restart rather than a rebuild.
#
# They used to be build arguments baked into the client bundle, which meant an image built on one
# server carried that server's hostnames and was useless to anybody else. Two things still worth
# knowing, because they moved rather than went away:
#
#   * Neither has a default. server.mjs refuses to start without both, rather than falling back to
#     localhost and serving a dashboard whose every visitor opens a socket to their OWN machine — a
#     failure that reads as "the hub is down" and mentions configuration nowhere.
#   * HUB_URL must be the PUBLIC https origin, not an internal one like http://web:8080. The socket
#     scheme is derived from the hub's origin rather than from the page, so an http:// value yields
#     a ws:// URL that browsers block as mixed content on an https page. server.mjs warns about that
#     combination at startup.
# ---------------------------------------------------------------------------------------------

# Node 24, matching the `dashboard` job in .github/workflows/ci.yml.
FROM node:24-slim AS build
WORKDIR /app

# package.json and the lockfile alone first, so the dependency install layer survives source edits.
COPY src/RaceIntelligence.Dashboard/package.json src/RaceIntelligence.Dashboard/package-lock.json ./
# `ci` rather than `install`: it installs exactly the lockfile and fails if the two disagree, which
# is what makes a container build reproducible.
RUN npm ci

COPY src/RaceIntelligence.Dashboard/ ./

# `npm run build` runs `tsc --noEmit` first, so a type error fails the image build rather than
# shipping. Nothing deployment-specific enters the image here — the two origins arrive at run time.
RUN npm run build

FROM node:24-slim AS app
WORKDIR /app
ENV NODE_ENV=production

# Production dependencies only — the built server still requires them at run time; it is a Node
# server, not a bundled-everything artifact.
COPY src/RaceIntelligence.Dashboard/package.json src/RaceIntelligence.Dashboard/package-lock.json ./
RUN npm ci --omit=dev && npm cache clean --force

COPY --from=build /app/dist ./dist
# The entry that actually listens. dist/server/server.js only exports a fetch handler, so running it
# directly exits 0 immediately — see the comment at the top of server.mjs.
COPY src/RaceIntelligence.Dashboard/server.mjs ./server.mjs

# server.mjs reads PORT and falls back to 3000, which is what docs/development.md and the hub's
# development Live:AllowedOrigins both assume. HUB_URL and READ_URL are deliberately NOT given
# defaults here: there is no value that is right for every deployment, and a wrong one that starts
# is worse than a missing one that does not.
ENV PORT=3000
EXPOSE 3000

# The node image ships this unprivileged user; the app only ever reads its own bundle.
USER node

# Node directly rather than `npm start`, so signals reach the process rather than being swallowed by
# an npm wrapper — that is what makes `docker compose stop` prompt instead of hitting the 10-second
# kill timeout. It is the same command `npm start` runs.
CMD ["node", "server.mjs"]
