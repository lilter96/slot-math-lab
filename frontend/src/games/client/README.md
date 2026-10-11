# The slot game client

This directory is the game client of tgslots (`apps/web-client/src`), moved here as it was written: the lobby,
the engine and the five games (Ancient Dragon, Woodland Whisper, Le Militare, X7 Club, Nine Lives). Its page is
`frontend/games.html`, its images and sounds are in `frontend/public/assets`.

The client is not edited to fit this repository. It differs from the original in these places only:

- `engine/dispatcher.ts` and `engine/revisioned-api.ts` call `serveGame` of `../server` where they called `fetch`.
  The requests and the responses are the same; the rounds are played by the game graphs of this repository.
- `app/game-rules.ts` and `games/ancient-dragon/runtime.ts` import the game tables as `@tgslots/<game>/config`
  where they imported a `config.json` by a path into the original workspace.

One defect of the original is fixed: `games/nine-lives/style.css` wrote a media query as `and(max-height:950px)`,
which no browser reads as a query, so the layout for a wide and low window never applied.

The `@tgslots/...` packages the client imports are the modules of `../contracts`. The client is compiled with
`tsconfig.games.json`, which keeps the compiler options it was written for.
