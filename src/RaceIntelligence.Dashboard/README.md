# Race Intelligence dashboard

The race engineer's view: the session list, the timing tower with per-lap history, and one
driver's telemetry at the collector's full poll rate.

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

`HUB_URL`, read at build time — see `app/shared/live/hubUrlBuild.ts` for why it is baked in rather
than fetched, and what that costs. It defaults to `http://localhost:5044`, which is the hub's
`http` launch profile, so two terminals and no configuration is enough to develop against it.

The hub must list this app's origin in `Live:AllowedOrigins`, or the WebSocket upgrade is rejected.

## Layout

```
app/
  routes/      the URL surface: /, /rooms/$roomId, /rooms/$roomId/$driverKey
  features/    rooms, tower, focus, laps — what each route shows
  shared/      live (store, socket, contracts), format, ui
  sims/        per-simulator panels, chosen by capability rather than by game
```

## The two rates

The focus stream runs at the collector's full poll rate, and **that data never goes through React
state**. The socket writes into `LiveStore`'s plain fields and `Float32Array` ring buffers; the
focus panel reads them from `requestAnimationFrame` loops and paints to canvas. React state holds
only the slow-changing half — the room list, the tower, lap history, extras, errors. A `setState`
per focus frame would mean a render cycle 60 times a second, which drops frames on a laptop well
before it does on a desktop.

`store.test.ts` pins that rule directly, and it is the one to keep passing.
