import { Container } from 'pixi.js'

export interface Scene {
  readonly background: Container
  readonly reels: Container
  readonly features: Container
  readonly hud: Container
  readonly overlays: Container
  clearGameLayers(): void
}

export class PixiScene implements Scene {
  readonly background: Container
  readonly reels: Container
  readonly features: Container
  readonly hud: Container
  readonly overlays: Container

  constructor(stage: Container) {
    this.background = new Container()
    this.reels = new Container()
    this.features = new Container()
    this.hud = new Container()
    this.overlays = new Container()

    stage.addChild(this.background)
    stage.addChild(this.reels)
    stage.addChild(this.features)
    stage.addChild(this.hud)
    stage.addChild(this.overlays)
  }

  clearGameLayers(): void {
    this.background.removeChildren()
    this.reels.removeChildren()
    this.features.removeChildren()
  }
}
