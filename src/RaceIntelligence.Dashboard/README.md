# Race Intelligence dashboard

The race engineer's view: the session list, the timing tower, and a **pit wall** the engineer
arranges themselves — a grid of widgets showing the selected car at the collector's full poll rate.

A [TanStack Start](https://tanstack.com/start) app on Node, served on **its own origin**. It is not
served by the hub — the browser opens its WebSocket straight at
`RaceIntelligence.Web`, which is both the more modular arrangement and the lower-latency one: a
proxy through this process would add a hop and force its event loop to re-emit every focus frame
sixty times a second.

```powershell
npm install
npm run dev        # http://localhost:3000
```

| Command             | Purpose                                                          |
| ------------------- | ---------------------------------------------------------------- |
| `npm run dev`       | Vite dev server with hot reload                                  |
| `npm run build`     | Typecheck, then build the client and server bundles into `dist/` |
| `npm start`         | Run the built server                                             |
| `npm run typecheck` | Types only                                                       |
| `npm run lint`      | ESLint, then a Prettier formatting check                         |
| `npm run format`    | Rewrite files to Prettier's formatting                           |
| `npm test`          | Vitest                                                           |

## Where the hub is

`HUB_URL`. In a deployment `server.mjs` reads it at run time and injects it into the document, so
one image works anywhere — see `app/shared/config/runtimeConfig.ts`. Under `npm run dev` there is no
`server.mjs`, so Vite's config-time value is used instead; it defaults to `http://localhost:5044`,
the hub's `http` launch profile, so two terminals and no configuration is enough to develop
against it.

The hub must list this app's origin in `Live:AllowedOrigins`, or the WebSocket upgrade is rejected.

## Layout

```
app/
  routes/      the URL surface: / and /rooms/$roomId
  features/    rooms, tower, laps, focus (the widget bodies), wall (the grid)
  shared/      live (store, socket, contracts), view (the saved wall), format, ui
  sims/        the widget catalogue, chosen by capability rather than by game
```

The URL names the room and nothing else. Which car is selected and how the wall is arranged are not
in the path — the wall is a document, and it is shared by exporting it rather than by copying a
link. That is a deliberate trade: "watch this car with me" used to be a URL and is now a file.

## The two page states

**Nothing selected** — the timing tower fills the screen. That is the whole interface in that state,
and it is meant to be good at being that rather than a wall with a hole in it.

**A car selected** — the tower moves to a column on the left and the wall appears beside it. Click
the same car again to go back.

## Everything on the wall is a widget

There are no fixed per-driver panels. Car metrics, the pedal bars, the assist indicators, every
chart — all of them are catalogue entries the user adds, moves, resizes and removes, and the tower
is the only thing on screen nobody places.

A widget draws **whichever car is selected**. It is never bound to a driver, which is what lets a
saved wall reference no driver at all and therefore open in any session. Chart series are keyed by
driver internally so that a later multi-car overlay is additive rather than a rewrite.

### Adding one

Two things, both in `app/sims/`:

1. A component. If it is a time series, it is a `LiveChart` spec — never a second uPlot lifecycle.
2. A catalogue entry in `registerSimPanels`, declaring `requires` (capability flags — **not** a game),
   `scope`, `defaultSize`, `minSize`, and `channels` if its lines can be toggled individually.

`registerDefaultWall` names what a brand-new wall starts with, so nobody meets an empty screen.

### Which rendering path

Three, and the choice is about how fast the data moves, not about taste:

| The data                        | How it draws                       | Example                        |
| ------------------------------- | ---------------------------------- | ------------------------------ |
| a series at frame rate          | `LiveChart`                        | `InputsTrace`, the tyre traces |
| at frame rate, but not a series | render once, write from a rAF loop | `TyreHeatmap`, `LiveReadout`   |
| once a lap or slower            | plain React                        | `LapTrend`, `FuelPanel`        |

Reaching for `LiveChart` for the third case would be machinery for a shape the data does not have;
reaching for plain React for the first two is the mistake the next section exists to prevent.

## The saved wall

Persisted to `localStorage` under `pitwall:view:<gameKey>` — **per simulator, not per room**, so two
sessions of the same sim open the same wall. The widgets available at all are decided by the
simulator's capabilities, and an engineer's layout belongs to the car they are engineering rather
than to one race.

The same document exports as a JSON file and imports back, which is how a wall moves between
machines or people. Import answers three cases differently, and the differences are the point:

- an **unknown widget id** is dropped and named — it is from a build this one does not have;
- a **known widget the session cannot feed** is kept, placed, and left to state its reason, because
  quietly rearranging someone's layout is worse than an empty tile, and it comes back when a
  collector that reports the channel joins;
- a **`gameKey` mismatch** is offered rather than refused, since most widgets gate on capability
  rather than on simulator.

Malformed input is refused and leaves the current wall untouched.

The view file carries a `version`. That is not a contradiction of the project's
no-backward-compatibility rule (see the root `CLAUDE.md`): that rule is about the wire, where every
process is built from the same commit. A view file lives on a disk and is the one artefact here that
can genuinely arrive from the past.

## The rates

**The focus stream never goes through React state.** The socket writes into `LiveStore`'s plain
fields and `Float32Array` ring buffers; widgets read them from `requestAnimationFrame` loops. A
`setState` per focus frame would mean a render cycle sixty times a second, which drops frames on a
laptop well before it does on a desktop.

`store.test.ts` pins that rule directly, and it is the one to keep passing.

Three channels arrive, and which one a value travels on is decided by how fast it actually changes:

| Channel     | Rate                            | Carries                                                                                |
| ----------- | ------------------------------- | -------------------------------------------------------------------------------------- |
| focus frame | collector poll rate             | pedals, steering, speed, gear, RPM, fuel, brake pressure, assists                      |
| stint frame | ~1 Hz, typed                    | tyre pressure, wear, tread temperatures and their operating window                     |
| extras      | ~1 Hz, the connector's own JSON | brake temperature, tyre grip and load, engine health, energy, flags, DRS, push-to-pass |

Tyre channels are on their own frame because they are read over a stint: on the fast frame,
fifty-nine of every sixty samples were serialised, sent, decoded and then dropped by the client's
own decimation. Brake pressure is on the fast frame for the opposite reason — a braking event lasts
about a second, and at 1 Hz that is one or two samples of the thing being asked about.

`LiveSelfFrameSizeTests` guards the fast frame's size, because a field added there is multiplied by
sixty and again by the audience.

Extras cross the wire **raw**: nothing upstream translates a simulator's sentinels, so RaceRoom's
`-1` means "not available" and emphatically not zero. `reportedNumber` is the single place that rule
lives. The typed channels translate at the connector instead, so a null there is already an absence.
