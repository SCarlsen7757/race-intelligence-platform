# The dashboard — a TanStack Start app on Node, its own service on its own origin.
#
# Build from the REPOSITORY ROOT, not from this directory:
#   docker build -f src/RaceIntelligence.Dashboard/Dockerfile \
#       --build-arg HUB_URL=https://race-api.example.com #       --build-arg READ_URL=https://race-read.example.com .
#
# ---------------------------------------------------------------------------------------------
# HUB_URL AND READ_URL ARE BUILD ARGUMENTS, NOT RUNTIME VARIABLES. Setting either in compose's `environment:`
# does nothing.
#
# vite.config.ts resolves it at config time and `define`s it into the client bundle, because the
# browser has no environment to read and the live routes are client-only — there is no server render
# to ship the value down with. Two consequences worth stating plainly:
#
#   * Build without it and hubUrlBuild.ts falls back to http://localhost:5044. Every visitor's
#     browser then tries to open a socket to their OWN machine, which fails in a way that looks like
#     the hub is down.
#   * It must be the PUBLIC https origin, not an internal one like http://web:8080. hubUrl.ts
#     derives the socket scheme from the hub origin rather than from the page, so an http:// value
#     produces a ws:// URL that browsers block as mixed content on an https page.
#
# Repointing the dashboard at a different hub is therefore a rebuild, not a restart.
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

ARG HUB_URL
ENV HUB_URL=${HUB_URL}

# The read API's origin, and it has to be declared here for the same reason HUB_URL does: Vite reads
# it at config time and bakes it into the client bundle, so a value that only reaches compose is a
# value the build never sees. Leaving it out does not fail — the bundle quietly falls back to
# localhost:5049 and the history view breaks only in a browser, which is the most expensive place to
# find out.
ARG READ_URL
ENV READ_URL=${READ_URL}

# `npm run build` runs `tsc --noEmit` first, so a type error fails the image build rather than
# shipping. Vite bakes HUB_URL and READ_URL in here.
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

# vite.config.ts reads PORT and falls back to 3000, which is what docs/development.md and the hub's
# development Live:AllowedOrigins both assume.
ENV PORT=3000
EXPOSE 3000

# The node image ships this unprivileged user; the app only ever reads its own bundle.
USER node

# Node directly rather than `npm start`, so signals reach the process rather than being swallowed by
# an npm wrapper — that is what makes `docker compose stop` prompt instead of hitting the 10-second
# kill timeout. It is the same command `npm start` runs.
CMD ["node", "server.mjs"]
