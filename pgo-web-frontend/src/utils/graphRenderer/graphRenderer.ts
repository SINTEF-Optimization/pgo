import { GraphEventEmitter } from '@/utils/graphRenderer/graphEvents'
import { PowerGrid } from '@/pgoApi/entities/powerGrid'
import { SwitchState } from '@/pgoApi/entities/solution'

export interface GraphRenderer {
  events: GraphEventEmitter

  setMainContainer(container: HTMLElement): void

  setMinimapContainer(container: HTMLElement): void

  setNetwork(network: PowerGrid): void

  render(): void

  applySwitchStates(switchStates: SwitchState[]): void

  clear(): void

  resetMainCamera(): void

  highlightNode(id: string): void

  unHighlightNode(): void

  useForceLayout(useForce: boolean): void
}
