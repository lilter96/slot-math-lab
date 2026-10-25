const DETAILS: Record<
  string,
  { image: string; foreground?: string; category: string; copy: string; color: string }
> = {
  'nine-lives': {
    image: '/assets/images/nine-lives/promo.webp',
    category: 'CLUSTER CASCADES · NINE LIVES',
    copy: 'Make a deal with Death. Paw chips, climbing multipliers and nine lives.',
    color: '#ef6a44',
  },
  'x7-club': {
    image: '/assets/images/x7-club/nightclub.webp',
    foreground: '/assets/images/x7-club/wild-hero.webp',
    category: 'HOLD & SPIN + COLUMN BOOST',
    copy: 'Sticky prizes, meme energy and a rare ×7 afterparty.',
    color: '#ffb1e5',
  },
  'ancient-dragon': {
    image: '/assets/images/ancient-dragon/shrine-background.webp',
    category: 'PAYLINE ENGINE',
    copy: 'Golden dragons, 25 paylines and free spins.',
    color: '#e3ba76',
  },
  'woodland-whisper': {
    image: '/assets/images/woodland-whisper/background-16-9.png',
    category: 'PAYLINES + PICK BONUS',
    copy: 'An enchanted forest with a matching-card feature.',
    color: '#8dddc1',
  },
  'le-militare': {
    image: '/assets/images/le-militare/command-background.webp',
    category: 'CLUSTERS + COMBAT',
    copy: 'Cascades, armed reels and growing multipliers.',
    color: '#a6c3d3',
  },
}

export class GamePicker {
  constructor(
    games: ReadonlyArray<{ readonly id: string; readonly displayName: string }>,
    onSelect: (gameId: string) => void,
  ) {
    const overlay = document.createElement('section')
    overlay.className = 'game-lobby'
    overlay.setAttribute('aria-label', 'TGSlots game selection')
    const intro = document.createElement('div')
    intro.className = 'lobby-intro'
    const eyebrow = document.createElement('p')
    eyebrow.className = 'lobby-eyebrow'
    eyebrow.textContent = 'TGSlots / PERSONAL GAME PLATFORM'
    const title = document.createElement('h1')
    title.textContent = 'Choose your world.'
    const subtitle = document.createElement('p')
    subtitle.textContent = 'Independent game engines. Real mechanics. Demo credits.'
    intro.append(eyebrow, title, subtitle)
    const grid = document.createElement('div')
    grid.className = 'lobby-grid'
    for (const game of games) {
      const detail = DETAILS[game.id]
      if (!detail) continue
      const card = document.createElement('button')
      card.type = 'button'
      card.className = 'lobby-card'
      card.style.setProperty('--game-accent', detail.color)
      card.setAttribute('aria-label', `Play ${game.displayName}`)
      const image = document.createElement('img')
      image.src = detail.image
      image.alt = ''
      const content = document.createElement('div')
      content.className = 'lobby-card-content'
      const category = document.createElement('span')
      category.className = 'lobby-category'
      category.textContent = detail.category
      const name = document.createElement('h2')
      name.textContent = game.displayName
      const copy = document.createElement('p')
      copy.textContent = detail.copy
      const cta = document.createElement('span')
      cta.className = 'lobby-play'
      cta.textContent = 'Enter game →'
      content.append(category, name, copy, cta)
      card.append(image)
      if (detail.foreground) {
        const foreground = document.createElement('img')
        foreground.src = detail.foreground
        foreground.alt = ''
        foreground.className = 'lobby-card-mascot'
        card.append(foreground)
      }
      card.append(content)
      card.addEventListener('click', () => {
        history.replaceState(null, '', `${location.pathname}?game=${encodeURIComponent(game.id)}`)
        overlay.remove()
        onSelect(game.id)
      })
      grid.append(card)
    }
    const footer = document.createElement('a')
    footer.className = 'lobby-source'
    footer.href = 'https://github.com/lilter96/tgslots'
    footer.target = '_blank'
    footer.rel = 'noopener noreferrer'
    footer.textContent = 'Explore the source on GitHub ↗'
    overlay.append(intro, grid, footer)
    document.body.append(overlay)
    const loader = document.getElementById('game-loader')
    if (loader) loader.style.visibility = 'hidden'
    grid.addEventListener(
      'click',
      () => {
        if (loader) loader.style.visibility = 'visible'
      },
      { once: true },
    )
  }
}
