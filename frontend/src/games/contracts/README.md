# Contracts of the ported game client

`src/games/client` is the slot game client of tgslots, kept as it was written. It imports the packages of its
original workspace by name (`@tgslots/...`); in this repository those names resolve to the modules of this
directory (see `paths` in `tsconfig.games.json` and `resolve.alias` in `vite.config.ts`).

Each module holds only what the client and the game server in `src/games/server` share:

- the types of the requests, responses, results and states (copied from the original packages), and
- the constants the client shows (bet sizes, symbol names, paylines, prices), read from the `model.json`
  the game's graph is built from.

There is no game math here. Rounds are played by the game graphs through `/api/play/round`.
