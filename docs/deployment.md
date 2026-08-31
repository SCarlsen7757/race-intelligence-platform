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
   | $R3E shared memory            ingest-api   :5443  --- cloudflared ---> race-ingest.<domain>
collector  ------- LAN ---------->  live hub    :5044  --- cloudflared ---> race-api.<domain>
   (this house)                     read-api    :5049  --- cloudflared ---> race-read.<domain>
                                   dashboard    :3000  --- cloudflared ---> race-web.<domain>

a collector on someone else's network takes the tunnel instead of the LAN arrow.
```

Three things about this are deliberate and worth reading before changing anything.

**The collector is not containerised and never will be.** It reads a Windows named shared-memory
block that exists only while RaceRoom is running. It belongs on the gaming PC.

**The ingest API is on the tunnel, and it took two things to get there.** Its key check is one key
per collector now, compared in constant time, behind a rate limiter. The two blockers that remained
after that are both already answered in the code: TLS is terminated by the tunnel, and the batch
body is capped at 8 MB by `TelemetryEndpoints.cs`, enforced on `Content-Length` and again by
lowering the request-body limit for a chunked body, so an oversized POST is refused with a 413
before anything is buffered or decoded. What it still does not have is an origin allowlist — its
client is a console collector that sends no `Origin` header, so there is nothing to check, which
means the key and the limiter are its whole guard. The live hub was always the service built for
exposure — constant-time key comparison, an origin allowlist, and a WebSocket keep-alive that
survives proxy idle timeouts — and the read API is published for the opposite reason: it holds no
key at all, writes nothing, applies no migrations, and serves only GETs behind its own allowlist.

**A collector in this house still uses the LAN.** Lower latency and one less thing to depend on
mid-race. That is an optimisation available to one install, not a property of the topology — a
collector anywhere else reaches the same two services through the tunnel.

**Four hostnames, and only three of them are origins a browser uses.** The browser loads the page
from the dashboard, opens its WebSocket straight at the hub, and fetches stored sessions from the
read API. Routing the sockets through Node would force its event loop to re-emit every focus frame
sixty times a second; keeping history off the hub is what lets the hub go on holding no database
credentials. The fourth, `race-ingest.*`, is never opened by a browser at all — a collector is
configured with it directly.

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

Create the tunnel in the Zero Trust dashboard, put its token in `.env`, and give it four public
hostnames:

| Hostname | Service |
|---|---|
| `race-web.<domain>` | `http://dashboard:3000` |
| `race-api.<domain>` | `http://web:8080` |
| `race-read.<domain>` | `http://read-api:8080` |
| `race-ingest.<domain>` | `http://ingest-api:8080` |

The service address is the compose service name and its container port, not the LAN-published one —
cloudflared resolves it on the compose network. So `ingest-api:8080`, not `<server-ip>:5443`.

There is deliberately no Access policy in front of `race-ingest.<domain>`. A collector is a console
application with no browser to complete an Access login, and the collector key is the guard. Putting
one there is an optional hardening step a deployment may choose; nothing in the application knows or
cares which tunnel provider is in front of it.

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

On the same LAN as the server:

```powershell
$env:Collector__Ingest__BaseUrl = "http://<server-lan-ip>:5443/"
$env:Collector__Ingest__ApiKey  = "<INGEST_API_KEY from the server .env>"
$env:Collector__Live__BaseUrl   = "http://<server-lan-ip>:5044/"
$env:Collector__Live__ApiKey    = "<LIVE_API_KEY from the server .env>"

C:\race-collector\RaceIntelligence.Collector.exe --live
```

### A collector on another network

Same executable, public addresses instead of LAN ones:

```powershell
$env:Collector__Ingest__BaseUrl = "https://race-ingest.<domain>/"
$env:Collector__Ingest__ApiKey  = "<this driver's own key>"
$env:Collector__Live__BaseUrl   = "https://race-api.<domain>/"
$env:Collector__Live__ApiKey    = "<LIVE_API_KEY from the server .env>"

C:\race-collector\RaceIntelligence.Collector.exe --live
```

Three things differ, and each has bitten somebody:

- **Each driver gets their own ingest key.** Add it on the server as another
  `Ingest__ApiKeys__<label>` line beside the existing one and restart `ingest-api`. Deleting that
  line later revokes that driver and leaves everyone else uploading. The hub key is still shared —
  it is one key on one publishing socket, not a per-driver credential.
- **`Collector__Live__BaseUrl` must be a real, reachable address.** The socket scheme is derived
  from it, so `https://` gives `wss://` and `http://` silently gives `ws://`. Unlike the ingest URL
  it cannot be a service-discovery name: the collector dials the hub with a `ClientWebSocket`,
  which never passes through the `HttpClient` that resolves those names.
- **`https://`, not `http://`.** The ingest key is sent on every request, so its confidentiality is
  the transport's. Over plaintext across the internet it is worth nothing.

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
