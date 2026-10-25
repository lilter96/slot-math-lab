// eslint-disable-next-line @typescript-eslint/no-empty-object-type
export interface GameRegistry {}

export type GameId = keyof GameRegistry & string
export type GameState<G extends GameId> = GameRegistry[G]['state']
export type GameResult<G extends GameId> = GameRegistry[G]['result']
export type GameActions<G extends GameId> = GameRegistry[G]['actions']
export type ActionType<G extends GameId> = keyof GameActions<G> & string
export type ActionPayload<G extends GameId, A extends ActionType<G>> = GameActions<G>[A]
export type EmptyPayload = Record<string, void>
