import sigma from 'sigma'
import 'sigma/plugins/sigma.renderers.customEdgeShapes/sigma.canvas.edges.dashed'
import 'sigma/plugins/sigma.renderers.customEdgeShapes/sigma.canvas.edgehovers.dashed'
import '../sigma/forceAtlas2/supervisor'
import '../sigma/forceAtlas2/worker'
import { PowerGrid } from '@/pgoApi/entities/powerGrid'
import {
  edgeRenderingProperties,
  getEdgeRenderingProperties,
  getNodeRenderingProperties,
} from '@/utils/graphRenderer/renderingProperties'
import { SwitchState } from '@/pgoApi/entities/solution'
import { GraphEventEmitter } from '@/utils/graphRenderer/graphEvents'
import { GraphRenderer } from '@/utils/graphRenderer/graphRenderer'

// Sigma v1 does not have up-to-date typings
/* eslint-disable @typescript-eslint/no-explicit-any */
/* eslint-disable new-cap */
export class SigmaV1GraphRenderer implements GraphRenderer {
  private _initialMainCameraRatio = 1.05
  private _initialMinimapCameraRatio = 1.05
  private _mainRenderer: any
  private _minimapRenderer: any
  private _mainContainer: HTMLElement | undefined
  private _minimapContainer: HTMLElement | undefined
  private _minimapCamera: any
  private _mainCamera: any
  private _network: PowerGrid | undefined
  private _highlightedNodeId: string | undefined = undefined

  public events = new GraphEventEmitter()

  public setMainContainer(container: HTMLElement) {
    this._mainContainer = container
  }

  public setMinimapContainer(container: HTMLElement) {
    this._minimapContainer = container
  }

  public setNetwork(network: PowerGrid) {
    this._network = network
  }

  public render() {
    if (!this._network) {
      throw new Error("The network to render must be set")
    }
    if (!this._mainContainer) {
      throw new Error("No DOM element is set as a container for rendering the main graph")
    }
    if (!this._minimapContainer) {
      throw new Error("No DOM element is set as a container for rendering the minimap")
    }

    this.clear()
    this.createRenderer()
    this.addMinimap()
    this.buildGraph()
    this.bindMainWindowEvents()
    this.bindMinimapEvents()
    this._mainRenderer.refresh()
  }

  public applySwitchStates(switchStates: SwitchState[]) {
    if (!this._mainRenderer) {
      throw new Error("The graph must be rendered before switch states can be applied")
    }
    const graph = this._mainRenderer.graph
    switchStates.forEach(switchState => {
      const renderingOptions = switchState.open
        ? edgeRenderingProperties.switchOpen
        : edgeRenderingProperties.switchClosed

      const node = graph.edges(switchState.line_id)
      Object.assign(node, renderingOptions)
    })
    this._mainRenderer.refresh({ skipIndexation: true })
  }

  public clear() {
    if (this._mainRenderer) {
      this._mainRenderer.graph.clear()
      this._mainRenderer.kill()
      this._mainRenderer = undefined
    }
  }

  public resetMainCamera() {
    if (this._mainRenderer) {
      const camera = this._mainRenderer.cameras[0]
      camera.x = 0.5
      camera.y = 0.5
      camera.ratio = this._initialMainCameraRatio
      this._mainRenderer.refresh()
    }
  }

  public highlightNode() {
    throw new Error("Not implemented")
  }

  public unHighlightNode() {
    throw new Error("Not implemented")
  }

  public useForceLayout(useForce: boolean): void {
    const mainRenderer = this._mainRenderer as any
    if (useForce) {
      mainRenderer.startForceAtlas2({useWorker: true})
    } else {
      mainRenderer.stopForceAtlas2()
    }
  }

  private buildGraph() {
    const graph = {
      nodes: [] as any,
      edges: [] as any,
    } as sigma.GraphData
    const network = this._network as PowerGrid

    network.nodes.forEach(node => {
      graph.nodes.push({
        id: node.id,
        label: node.id,
        x: node.coordinates
          ? node.coordinates[0]
          : Math.random(),
        y: node.coordinates
          ? node.coordinates[1]
          : Math.random(),
        ...getNodeRenderingProperties(node),
      })
    })

    network.lines.forEach(edge => {
      graph.edges.push({
        id: edge.id,
        source: edge.source,
        target: edge.target,
        ...getEdgeRenderingProperties(edge),
      })
    })

    this._mainRenderer.graph.read(graph)
  }

  private createRenderer() {
    this._mainRenderer = new sigma.sigma({
      graph: { nodes: [], edges: [] },
      renderer: {
        container: this._mainContainer,
        type: 'canvas',
      } as sigma.RendererSettings,
      settings: {
        doubleClickEnabled: false,
        skipIndexation: true,
        minEdgeSize: 0.5,
        maxEdgeSize: 4,
        enableEdgeHovering: true,
        edgeHoverColor: 'edge',
        defaultEdgeHoverColor: '#000',
        edgeHoverSizeRatio: 1,
        edgeHoverExtremities: true,
      } as any as sigma.Settings,
    })
    this._mainCamera = this._mainRenderer.camera
    this._mainCamera.ratio = this._initialMainCameraRatio
  }

  private addMinimap() {
    this._minimapCamera = this._mainRenderer.addCamera()
    this._minimapRenderer = this._mainRenderer.addRenderer({
      container: this._minimapContainer,
      type: 'canvas',
      camera: this._minimapCamera,
      settings: {
        enableHovering: false,
        enableEdgeHovering: false,
        drawEdges: true,
        drawLabels: false,
        mouseEnabled: true,
        draggingEnabled: false,
        touchEnabled: false,
      },
    })
    this._minimapCamera.ratio = this._initialMinimapCameraRatio
  }

  private bindMainWindowEvents() {
    this._mainRenderer.bind('clickStage', (event: any) => {
      const { x, y, clientX, clientY } = event.data.captor
      this.events.clickBackground({ x, y, clientX, clientY })
    })

    this._mainRenderer.bind('clickNode', (event: any) => {
      const { id } = event.data.node
      const { x, y, clientX, clientY } = event.data.captor
      this.events.clickNode({ id, x, y, clientX, clientY })
    })

    this._mainRenderer.bind('overNode', (event: any) => {
      const { id } = event.data.node
      const { x, y, clientX, clientY } = event.data.captor
      this.events.startHoverNode({ id, x, y, clientX, clientY })
    })

    this._mainRenderer.bind('outNode', (event: any) => {
      const { id } = event.data.node
      const { x, y, clientX, clientY } = event.data.captor
      this.events.endHoverNode({ id, x, y, clientX, clientY })
    })

    this._mainRenderer.bind('clickEdge', (event: any) => {
      const { id, source, target } = event.data.edge
      const { x, y, clientX, clientY, ctrlKey } = event.data.captor
      this.events.clickEdge({ id, source, target, x, y, clientX, clientY, ctrlKeyPressed: ctrlKey })
    })

    this._mainRenderer.bind('overEdge', (event: any) => {
      const { id, source, target } = event.data.edge
      const { x, y, clientX, clientY } = event.data.captor
      this.events.startHoverEdge({ id, source, target, x, y, clientX, clientY })
    })

    this._mainRenderer.bind('outEdge', (event: any) => {
      const { id, source, target } = event.data.edge
      const { x, y, clientX, clientY } = event.data.captor
      this.events.endHoverEdge({ id, source, target, x, y, clientX, clientY })
    })
  }

  private bindMinimapEvents() {
    // The built-in captors call stopPropagation on all events
    // So we must remove them, not just set { enableMouse: false }
    const { captors } = this._minimapRenderer as any
    captors.forEach((captor: any) => {
      captor.kill()
    })

    const container = this._minimapContainer as HTMLElement
    container.addEventListener('click', (event: MouseEvent) => {
      this.setMainCameraPosition(event.offsetX, event.offsetY)
    })
  }

  private setMainCameraPosition(x: number, y: number) {
    if (!this._mainRenderer
        || !this._minimapRenderer
        || !this._mainContainer
        || !this._minimapContainer
    ) {
      return
    }

    // The Sigma coordinate system has 0, 0 in the center, so we must convert from screen coordinates
    const sigmaX = x - this._minimapContainer.offsetWidth / 2
    const sigmaY = y - this._minimapContainer.offsetHeight / 2

    // Scale up to the main map size
    const scalingFactorX = this._mainContainer.offsetWidth / this._minimapContainer.offsetWidth
    const scalingFactorY = this._mainContainer.offsetHeight / this._minimapContainer.offsetHeight

    this._mainCamera.x = sigmaX * scalingFactorX
    this._mainCamera.y = sigmaY * scalingFactorY
    this._mainRenderer.render()
  }
}
