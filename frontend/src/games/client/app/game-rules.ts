import type { AssetManifest, GameManifest } from '@tgslots/shared-contracts'
import { PAYLINE_DATA as dragonLines } from '@tgslots/ancient-dragon/constants'
import { PAYLINE_DATA as forestLines } from '@tgslots/woodland-whisper/constants'
import dragon from '@tgslots/ancient-dragon/config'
import forest from '@tgslots/woodland-whisper/config'
import military from '@tgslots/le-militare/config'

interface RulesSpec {
  notes: string[]
  paytable: Record<string, Record<string, number>>
  counts: number[]
  basis: string
  paylines?: Uint8Array
  scatter?: Record<string, number>
}

export const gameRules: Record<string, RulesSpec> = {
  'ancient-dragon': {
    notes: [
      `5 reels × 3 rows. All ${dragon.game_metadata.lines} paylines are active.`,
      'Line wins count consecutive matching symbols from the leftmost reel. Only the longest paying combination on each line pays; different winning lines are added.',
      'Gold Dragon substitutes for paying symbols. An all-wild line receives the highest listed payout for its length. Yin-yang scatter pays anywhere on the grid.',
      `${dragon.feature.trigger.min_count}+ scatters award ${dragon.feature.free_spins_per_trigger} free spins. Each retrigger adds the same number. Free spins keep the triggering stake.`,
    ],
    paytable: dragon.paytable,
    counts: [3, 4, 5],
    basis: `Line values × stake per line. Stake per line = total stake ÷ ${dragon.game_metadata.lines}. Scatter values × total stake.`,
    paylines: dragonLines,
    scatter: dragon.scatter_paytable.YINYANG,
  },
  'woodland-whisper': {
    notes: [
      `5 reels × 3 rows. All ${forest.game_metadata.lines} paylines are active.`,
      'Line wins count consecutive matching symbols from the leftmost reel. Different winning lines are added. Woman is wild; Coin is scatter and pays anywhere.',
      `${forest.feature.trigger.min_count}+ Coins start a pick feature. Find two matching numbers to award that number of free spins, from ${Math.min(...forest.feature.pick_bonus.map(([value]) => value!))} to ${Math.max(...forest.feature.pick_bonus.map(([value]) => value!))}. Picks reveal a server-selected award; choosing a card does not change its probability.`,
      `Free-spin line and scatter awards are multiplied by ${forest.feature.free_spin_multiplier}. Retriggers add free spins. The triggering stake is retained.`,
      `BUY BONUS costs ${forest.feature.buy_bonus_cost_multiplier}× the total stake and guarantees a pick-feature entry.`,
      'Chest is a non-paying symbol in this game configuration. Only combinations listed below award line wins.',
    ],
    paytable: forest.paytable,
    counts: [3, 4, 5],
    basis: `Line values × stake per line. Stake per line = total stake ÷ ${forest.game_metadata.lines}. Scatter values × total stake; both are multiplied by ${forest.feature.free_spin_multiplier} during free spins.`,
    paylines: forestLines,
    scatter: forest.scatter_paytable.COIN,
  },
  'le-militare': {
    notes: [
      `${military.game_metadata.grid_reels} reels × ${military.game_metadata.grid_rows} rows. There are no paylines. ${military.game_metadata.min_cluster}+ matching symbols connected horizontally or vertically form a paying cluster; diagonals do not connect.`,
      'Wilds substitute within clusters containing a paying symbol. Mixed-symbol wild clusters are disabled. Paying clusters clear and symbols cascade into their places.',
      'An S300 turns its reel into one giant sticky WILD. It connects across all five rows, counts as ONE symbol toward a cluster, and stays locked through every cascade until the bonus ends. Ordinary interception WILDs count individually and clear when they win; their multipliers stay banked.',
      'Add cluster awards across all cascades, then multiply by the accumulated combat multiplier (at least 1). The multiplier persists between free spins and resets on the next paid round.',
      `${Object.entries(military.scatter_definition.free_spins_awarded)
        .map(
          ([count, spins], index, entries) =>
            `${count}${index === entries.length - 1 ? '+' : ''} scatters = ${spins} free spins`,
        )
        .join('; ')}. Free spins retain the triggering stake.`,
      'Recon Strike has approximately 5× the natural bonus trigger chance. Air Raid guarantees a squadron, but interceptions and wins are not guaranteed.',
      `Combat Op / Elite Op / Super Op start with exactly ${military.scatter_definition.free_spins_awarded['4']} / ${military.scatter_definition.free_spins_awarded['6']} / ${military.scatter_definition.free_spins_awarded['7']} free spins. Extra entry scatters do not increase a purchased tier; retriggers during free spins still apply.`,
      ...Object.entries(military.modes).map(
        ([mode, data]) =>
          `${mode[0]!.toUpperCase()}${mode.slice(1)} prices (× stake): Recon Strike ${data.buy_costs.chance}; Air Raid ${data.buy_costs.airraid}; Combat Op ${data.buy_costs.standard}; Elite Op ${data.buy_costs.elite}; Super Op ${data.buy_costs.super}.`,
      ),
      `Super Op starts with multiplier ${military.buy_options.super.start_multiplier}. Recon, Assault and Siege change feature distributions. The round payout cap is ${military.game_metadata.max_win_multiplier.toLocaleString('en-US')}× stake.`,
    ],
    paytable: military.paytable,
    counts: Array.from({ length: 25 }, (_, index) => index + 6),
    basis:
      'Cluster values × total stake, before combat multipliers and the round cap. A giant sticky WILD counts as one symbol, regardless of its five-row height.',
  },
}

const labels: Record<string, string> = {
  GOLDDRAGON: 'Gold Dragon',
  GREENDRAGON: 'Green Dragon',
  LOTUSFLOWER: 'Lotus',
  YINYANG: 'Yin-yang',
  ACE: 'A',
  KING: 'K',
  QUEEN: 'Q',
  JACK: 'J',
  TEN: '10',
  NINE: '9',
}
const label = (name: string) => labels[name] ?? name[0]!.toUpperCase() + name.slice(1).toLowerCase()

function icon(name: string, assets: AssetManifest): HTMLElement {
  const holder = document.createElement('span')
  holder.className = 'rules-symbol-icon'
  const source = assets.images?.[name]
  if (source) {
    const image = document.createElement('img')
    image.src = source
    image.alt = ''
    holder.append(image)
  } else {
    for (const atlas of assets.atlases ?? []) {
      const frame = atlas.frames[name]
      if (!frame) continue
      const crop = document.createElement('span')
      const scale = Math.min(40 / frame.width, 40 / frame.height)
      crop.style.cssText = `position:absolute;width:${frame.width}px;height:${frame.height}px;left:${(44 - frame.width * scale) / 2}px;top:${(44 - frame.height * scale) / 2}px;background-image:url('${assets.images?.[atlas.image]}');background-position:-${frame.x}px -${frame.y}px;transform:scale(${scale});transform-origin:top left`
      holder.append(crop)
      break
    }
  }
  return holder
}

export function appendGameRules(root: HTMLElement, manifest: GameManifest, assets: AssetManifest) {
  const spec = gameRules[manifest.gameId]
  if (!spec) return
  const list = document.createElement('ul')
  spec.notes.forEach((text) => {
    const item = document.createElement('li')
    item.textContent = text
    list.append(item)
  })
  root.append(list)
  const details = document.createElement('details')
  const summary = document.createElement('summary')
  summary.textContent = 'Paytable & symbol values'
  const basis = document.createElement('p')
  basis.textContent = spec.basis
  details.append(summary, basis)
  if (spec.counts.length > 5) {
    for (const [name, pays] of Object.entries(spec.paytable)) {
      const symbol = document.createElement('details')
      const heading = document.createElement('summary')
      heading.append(icon(name, assets), document.createTextNode(label(name)))
      const grid = document.createElement('dl')
      grid.className = 'rules-payout-grid'
      for (const count of spec.counts) {
        const pair = document.createElement('div')
        const size = document.createElement('dt')
        size.textContent = `${count} symbols`
        const pay = document.createElement('dd')
        pay.textContent = `${pays[String(count)] ?? 0}×`
        pair.append(size, pay)
        grid.append(pair)
      }
      symbol.append(heading, grid)
      details.append(symbol)
    }
  } else {
    const table = document.createElement('table')
    table.className = 'rules-paytable'
    const head = table.createTHead().insertRow()
    for (const title of ['Symbol', ...spec.counts.map(String)]) {
      const cell = document.createElement('th')
      cell.scope = 'col'
      cell.textContent = title
      head.append(cell)
    }
    const body = table.createTBody()
    for (const [name, pays] of Object.entries(spec.paytable)) {
      const row = body.insertRow()
      const title = document.createElement('th')
      title.scope = 'row'
      title.append(icon(name, assets), document.createTextNode(label(name)))
      row.append(title)
      spec.counts.forEach((count) => {
        row.insertCell().textContent = `${pays[String(count)] ?? 0}×`
      })
    }
    details.append(table)
  }
  if (spec.scatter) {
    const paragraph = document.createElement('p')
    paragraph.textContent =
      'Scatter awards: ' +
      Object.entries(spec.scatter)
        .map(([count, pay]) => `${count} = ${pay}× total stake`)
        .join(' · ')
    details.append(paragraph)
  }
  root.append(details)
  if (spec.paylines) {
    const lines = document.createElement('details')
    const title = document.createElement('summary')
    title.textContent = `All ${spec.paylines.length / manifest.grid.reels} paylines`
    const diagrams = document.createElement('div')
    diagrams.className = 'rules-paylines'
    for (let line = 0; line < spec.paylines.length / manifest.grid.reels; line++) {
      const figure = document.createElement('figure')
      const caption = document.createElement('figcaption')
      caption.textContent = `Line ${line + 1}`
      const grid = document.createElement('div')
      grid.className = 'rules-line-grid'
      const rows = Array.from(spec.paylines.slice(line * 5, line * 5 + 5))
      grid.setAttribute(
        'aria-label',
        `Rows from left to right: ${rows.map((row) => row + 1).join(', ')}`,
      )
      for (let row = 0; row < 3; row++)
        for (let reel = 0; reel < 5; reel++) {
          const cell = document.createElement('span')
          cell.className = rows[reel] === row ? 'active' : ''
          grid.append(cell)
        }
      figure.append(caption, grid)
      diagrams.append(figure)
    }
    lines.append(title, diagrams)
    root.append(lines)
  }
}
