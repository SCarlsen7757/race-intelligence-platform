# Deploying the platform

This describes the first real deployment: the backend in containers on a home-server VM, the
collector on the gaming PC, and the dashboard published through a Cloudflare tunnel.

`docs/development.md` covers running everything on one machine for development. This is the other
half — what changes when the pieces are on different machines.

The topology and the reasoning behind it are recorded in
[ADR 0003](decisions/0003-deployment-topology.md). This file is the instructions.

---

## The shape of it

```
Gaming PC (Windows)                Home server VM (Linux, Docker)         Anywhere
-------------------                ------------------------------         --------
RaceRoom                           postgres                               a browser
   | $R3E shared memory            ingest-api   :5443  (not published)        |
collector  ------- LAN ---------->  live hub    :5044  --- cloudflared ---> race-api.<domain>
                                   dashboard    :3000  --- cloudflared ---> race-web.<domain>
```

Three things about this are deliberate and worth reading before changing anything.

**The collector is not containerised and never will be.** It reads a Windows named shared-memory
block that exists only while RaceRoom is running. It belongs on the gaming PC.

**The ingest API is not on the tunnel.** Its key check is no longer the reason — that is now one
key per collector, compared in constant time, behind a rate limiter — but publishing it is a
separate decision about TLS termination and the 8 MB batch body as a DoS surface, and ADR 0003 has
not been amended to make it. The live hub is the service built for exposure — constant-time key comparison, an
origin allowlist, and a WebSocket keep-alive that exists to survive proxy idle timeouts. The read
API is published too, for the opposite reason: it holds no key at all, writes nothing, applies no
migrations, and serves only GETs behind its own origin allowlist. The collector reaches the ingest
API and the hub over the LAN: there is no reason to send live frames out through the tunnel and back.

**The dashboard, the hub and the read API are three origins, hence three hostnames.** The browser
loads the page from the dashboard, opens its WebSocket straight at the hub, and fetches stored
sessions from the read API. Routing the sockets through Node would force its event loop to re-emit
every focus frame sixty times a second; keeping history off the hub is what lets the hub go on
holding no database credentials.

---

## Server: first deploy

```bash
git clone <this repo> race-intelligence
cd race-intelligence
cp .env.example .env
$EDITOR .env          # every value is required; see the comments in the file
docker compose -f compose.test.yml up --build -d
```

Compose brings things up in the right order on its own: PostgreSQL becomes healthy, the `migrate`
container applies the schema and exits, and only then do the ingest and read APIs start. The read
API waits on the migration step even though it never migrates: it does not own the schema, so
starting before one exists would fail on its first query rather than at startup.

### Three settings that are easy to get wrong

**`HUB_ORIGIN` and `READ_ORIGIN` are baked into the dashboard image at build time.** Both are passed
as Docker build arguments, and `vite.config.ts` compiles them into the client bundle — the browser
has no environment to read, and the routes are client-only, so there is no server render to ship the
values down with. Two consequences:

- Changing either means `docker compose -f compose.test.yml build dashboard`, **not** a restart.
- `HUB_ORIGIN` must be the public `https://` origin of the **hub**. `app/shared/live/hubUrl.ts`
  derives the socket scheme from this value rather than from the page, so an `http://` origin
  produces a `ws://` URL that browsers block as mixed content on an https page.

If the dashboard loads but never connects, `HUB_ORIGIN` is almost always why. If it connects but the
History page fails to load, look at `READ_ORIGIN` instead — the two are independent, and one being
wrong leaves the other working, which is what makes the symptom confusing. Check what the page
actually requests in DevTools before looking anywhere else.

**`DASHBOARD_ORIGIN` is compared against a browser's `Origin` header**, so it too must be the public
address, not an internal one. It is used twice — the hub's `Live:AllowedOrigins` and the read API's
`Read:AllowedOrigins` — and both services refuse to start if it is missing rather than defaulting to
allowing every origin.

### Cloudflare tunnel

Create the tunnel in the Zero Trust dashboard, put its token in `.env`, and give it three public
hostnames:

| Hostname | Service |
|---|---|
| `race-web.<domain>` | `http://dashboard:3000` |
| `race-api.<domain>` | `http://web:8080` |
| `race-read.<domain>` | `http://read-api:8080` |

Do not add a rule for the ingest API.

WebSockets need no special configuration. The dashboard uses the raw browser `WebSocket` API rather
than SignalR, which cloudflared proxies natively, and the hub's 15-second keep-alive already exists
to cover idle timeouts.

### Updating

```bash
git pull
docker compose -f compose.test.yml build --pull
docker compose -f compose.test.yml up -d
```

Rebuilding runs the migration container again; it is a no-op when there is nothing to apply. If a
change moved the hub's public hostname, rebuild the dashboard image explicitly.

**`--pull` matters.** `global.json` names an exact SDK, and the Dockerfiles build on the floating
`mcr.microsoft.com/dotnet/sdk:10.0` tag. A base image cached before that tag moved forward will fail
the restore with `Requested SDK version: 10.0.400 ... Installed SDKs: 10.0.302` — which reads as a
repository problem and is a stale layer.

---

## Gaming PC: the collector

Publish it once:

```powershell
dotnet publish src/RaceIntelligence.Collector -c Release -o C:\race-collector
```

Then run it with the server's addresses. Use the **published exe**, not `dotnet run`:
`Properties/launchSettings.json` forces `DOTNET_ENVIRONMENT=Development`, which would load the
committed `dev-local-only-key` values instead of the server's real ones.

```powershell
$env:Collector__Ingest__BaseUrl = "http://<server-lan-ip>:5443/"
$env:Collector__Ingest__ApiKey  = "<INGEST_API_KEY from the server .env>"
$env:Collector__Live__BaseUrl   = "http://<server-lan-ip>:5044/"
$env:Collector__Live__ApiKey    = "<LIVE_API_KEY from the server .env>"

C:\race-collector\RaceIntelligence.Collector.exe --live
```

Both base URLs **must end in a trailing slash**. They are `[ServiceUrl]`-validated and the collector
refuses to start without it, because the relative request paths would otherwise replace the last
segment rather than append to it.

`--live` is required: live publishing is off in the shipped defaults, so without it the collector
archives but the dashboard stays empty.

### What it does when things go wrong

Worth knowing before a race rather than during one.

| | Behaviour |
|---|---|
| **RaceRoom not running** | Logs it and polls every 2 seconds. Starting the game is picked up within about 2 seconds. Not an error. |
| **Ingest API down** | Retries with backoff and a circuit breaker. On give-up it logs an error and drops that batch — telemetry is lost, never silently. The collector keeps running and the dashboard is unaffected. |
| **Live hub down** | Reconnects with 1s to 30s backoff, logging "Archiving is unaffected." |
| **Buffer full** | Bounded at 20 000 samples (about 5.5 minutes at 60 Hz). The default `Wait` applies back-pressure to the collect loop; `DropWrite` drops with a warning and a counter instead. |
| **The collector itself crashes** | It prints a stack trace and exits. There is no supervision and no auto-restart, deliberately: a wedged process retrying during a race is worse than a stopped one you debug afterwards. |

**It cannot steal focus from the game.** It is a console worker with no UI framework of any kind — no
window, dialog or message box exists anywhere in it. Start it before RaceRoom, or minimized, so its
console window never appears on top.

**Logs.** It writes to the console and to `logs/collector-<date>.log` next to the exe, rolling daily
and keeping 14 files. The file matters here: a collector started before a race and left alone would
otherwise lose every warning to a console window nobody is watching.

---

## Checking a deployment

```bash
# On the server. Empty until a collector connects, which is the correct answer.
curl http://<server>:5044/api/v1/live/rooms
# -> {"type":"roomList","rooms":[]}

docker compose -f compose.test.yml ps -a     # migrate should show Exited (0)
docker compose -f compose.test.yml logs -f web
```

Then start the collector with RaceRoom closed. The hub should log the publisher connecting by name,
and the room should appear in the dashboard — all before the game is launched. That separates
"deployment works" from "telemetry works", which are much harder to debug together.

Worth rehearsing once, before trusting it with a race: stop the ingest API mid-session and confirm
the dashboard carries on, then restart the hub and confirm both the collector and the browser
reconnect by themselves.
