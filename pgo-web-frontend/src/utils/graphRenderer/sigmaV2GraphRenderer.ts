import { WebGLRenderer } from 'sigma'
import { UndirectedGraph } from 'graphology'
import { PowerGrid } from '@/pgoApi/entities/powerGrid'
import {
  edgeRenderingProperties,
  getEdgeRenderingProperties,
  getNodeRenderingProperties,
} from '@/utils/graphRenderer/renderingProperties'
import { SwitchState } from '@/pgoApi/entities/solution'
import { SigmaEvent } from '@/utils/graphRenderer/sigmaEvents'
import {
  GraphEventEmitter,
  ClickBackgroundEvent,
  ClickNodeEvent,
} from '@/utils/graphRenderer/graphEvents'
import { GraphRenderer } from '@/utils/graphRenderer/graphRenderer'

export class SigmaV2GraphRenderer implements GraphRenderer {
  private _initialGraphCameraRatio = 1.1
  private _initialMinimapCameraRatio = 1.1
  private _mainRenderer: WebGLRenderer | undefined
  private _minimapRenderer: WebGLRenderer | undefined
  private _mainContainer: HTMLElement | undefined
  private _minimapContainer: HTMLElement | undefined
  private _graph: UndirectedGraph | undefined
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
      throw new Error("No DOM element is set as a container for rendering the main graph ")
    }
    if (!this._minimapContainer) {
      throw new Error("No DOM element is set as a container for rendering the minimap")
    }

    this.clear()
    this.buildGraph()
    this.renderMain()
    this.renderMinimap()
  }

  public applySwitchStates(switchStates: SwitchState[]) {
    const renderer = this._mainRenderer as WebGLRenderer
    switchStates.forEach(switchState => {
      const edge = (this._network as PowerGrid)
        .lines
        .find(l => l.id === switchState.line_id)
      if (!edge) {
        return
      }
      const renderingOptions = switchState.open
        ? edgeRenderingProperties.switchOpen
        : edgeRenderingProperties.switchClosed

      renderer.graph.mergeEdge(
        edge.source,
        edge.target,
        {
          ...renderingOptions,
        })
    })
    renderer.refresh()
  }

  public clear() {
    if (this._mainRenderer) {
      this._mainRenderer.kill()
    }
    if (this._minimapRenderer) {
      this._minimapRenderer.kill()
    }
  }

  public resetMainCamera() {
    if (this._mainRenderer) {
      const camera = this._mainRenderer.getCamera()
      camera.x = 0.5
      camera.y = 0.5
      camera.ratio = this._initialGraphCameraRatio
      this._mainRenderer.refresh()
    }
  }

  public highlightNode(id: string) {
    this._highlightedNodeId = id
    const renderer = this._mainRenderer as WebGLRenderer
    renderer.refresh()
  }

  public unHighlightNode() {
    this._highlightedNodeId = undefined
    const renderer = this._mainRenderer as WebGLRenderer
    renderer.refresh()
  }

  private renderMain() {
    const nodeReducer = (id: string, data: any) => {
      if (this._highlightedNodeId === id) return { ...data, size: 12, zIndex: 1 }
      return data
    }

    this._mainRenderer = new WebGLRenderer(this._graph, this._mainContainer, {
      renderLabels: true,
      renderEdgeLabels: true,
      nodeReducer,
    })

    // Map Sigma events to our application events
    this._mainRenderer.on(SigmaEvent.clickNode, (event) => {
      const clickNodeEvent = {
        id: event.node,
        x: event.event.x,
        y: event.event.y,
        clientX: event.event.clientX,
        clientY: event.event.clientY,
      } as ClickNodeEvent
      this.events.clickNode(clickNodeEvent)
    })
    this._mainRenderer.on(SigmaEvent.clickStage, (event) => {
      const clickBackgroundEvent = {
        x: event.event.x,
        y: event.event.y,
        clientX: event.event.clientX,
        clientY: event.event.clientY,
      } as ClickBackgroundEvent
      this.events.clickBackground(clickBackgroundEvent)
    })

    this._mainRenderer.getCamera().animatedUnzoom(this._initialGraphCameraRatio)
  }

  private renderMinimap() {
    this._minimapRenderer = new WebGLRenderer(this._graph, this._minimapContainer, {
      renderLabels: false,
      renderEdgeLabels: false,
    })

    // Remove all built-in event listeners
    this._minimapRenderer.captors.mouse.kill()

    // Add back the hover functionality
    this._minimapRenderer.elements.mouse.addEventListener(
      'mousemove',
      this._minimapRenderer.captors.mouse.handleMove,
      false
    )

    // Handle clicks on the minimap
    const container = this._minimapContainer as HTMLElement
    container.onclick = event => {
      this.setMainCameraPosition(event.offsetX, event.offsetY)
    }
    this._minimapRenderer.getCamera().animatedUnzoom(this._initialMinimapCameraRatio)
  }

  private setMainCameraPosition(x: number, y: number) {
    if (!this._mainRenderer
        || !this._minimapRenderer) {
      return
    }
    const camera = this._mainRenderer.getCamera()
    const minimapCamera = this._minimapRenderer.getCamera()
    const mapWidth = this._minimapRenderer.width
    const mapHeight = this._minimapRenderer.width

    // Each camera starts with a center position of x=0.5, y=0.5 and with a ratio of 1 regardless of the graph size
    // The mouse event captures the screen coordinates of the click, so these need to be converted to the
    // renderer's coordinate system
    const cameraPositionOffset = minimapCamera.ratio / 2
    const xMouseClickDistance = x * minimapCamera.ratio / mapWidth
    const yMouseClickDistance = y * minimapCamera.ratio / mapHeight
    camera.x = minimapCamera.x - cameraPositionOffset + xMouseClickDistance
    camera.y = minimapCamera.y + cameraPositionOffset - yMouseClickDistance

    this._mainRenderer.refresh()
  }

  private buildGraph() {
    const network = this._network as PowerGrid
    const graph = new UndirectedGraph()
    network.nodes.forEach(node => {
      graph.addNode(
        node.id,
        {
          label: node.id,
          x: node.coordinates[0],
          y: node.coordinates[1],
          ...getNodeRenderingProperties(node),
        })
    })
    network.lines.forEach(edge => {
      graph.addEdge(
        edge.source,
        edge.target,
        {
          label: edge.id,
          ...getEdgeRenderingProperties(edge),
        })
    })
    this._graph = graph
  }
}
