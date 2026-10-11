import model from '../../le-militare/model.json' with { type: 'json' };

const bet = model.game_metadata.cost_to_play;
const inBets = (table: Record<string, number>) => Object.fromEntries(Object.entries(table).map(([key, credits]) => [key, credits / bet]));
const mode = ({ prices }: { prices: { standard: number; elite: number; super: number; chance: number; air_raid: number } }) => ({
  buy_costs: { standard: prices.standard / bet, elite: prices.elite / bet, super: prices.super / bet, chance: prices.chance / bet, airraid: prices.air_raid / bet },
});

/**
 * The tables the client's rules page shows, laid out as the original game's config file was and read from the
 * model the game's graph is built from. The model counts in credits of a bet of 100; the page shows bets.
 */
export default {
  game_metadata: {
    name: model.game_metadata.name,
    grid_reels: model.board.columns,
    grid_rows: model.board.rows,
    min_cluster: Math.min(...Object.values(model.cluster_paytable).flatMap(pays => Object.keys(pays).map(Number))),
    max_win_multiplier: model.max_win,
  },
  paytable: Object.fromEntries(Object.entries(model.cluster_paytable).map(([symbol, pays]) => [symbol, inBets(pays)])) as Record<string, Record<string, number>>,
  scatter_definition: { symbol: model.symbols.scatter, min_count: model.free_spins.trigger, free_spins_awarded: model.free_spins.awards },
  buy_options: { super: { start_multiplier: model.buy.super.multiplier } },
  modes: { recon: mode(model.modes.recon), assault: mode(model.modes.assault), siege: mode(model.modes.siege) },
};
