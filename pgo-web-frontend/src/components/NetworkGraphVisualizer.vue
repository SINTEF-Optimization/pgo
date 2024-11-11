<template>
    <div class="graph-visualizer-container">
      <div class="columns">
        <div class="column">
          <div class="graph-box box">
            <div class="graph-controls buttons has-addons is-flex p-1" v-if="renderOnChanges">
              <button class="graph-control-button " @click="resetGraph">
                <span class="icon" >
                  <i class="mdi mdi-24px mdi-fit-to-page"></i>
                </span>
              </button>
              <button class="graph-control-button" @click="rebuildGraph">
                <span class="icon" >
                  <i class="mdi mdi-24px mdi-refresh"></i>
                </span>
              </button>
              <button
                @click="toggleForce"
                class="graph-control-button"
                :class="{'force-active': forceIsActive}"
              >
                <span>
                  Auto layout
                </span>
              </button>
              <button class="graph-control-button" v-if="!shouldAutoRender" @click="renderOnChanges = false; clearCurrentNetwork()">
                <span class="icon" >
                  <i class="mdi mdi-24px mdi-close"></i>
                </span>
              </button>
            </div>
            <div
              class="render-prompt has-text-grey is-flex is-flex-direction-column is-align-content-center"
              v-if="!shouldAutoRender && !renderOnChanges"
            >
              <span >
                This network might be too large for the visualizer.
              </span>
              <span >
                You can show it anyway, but the UI will become unresponsive while rendering.
              </span>
              <span >
              <button class="button mt-2" @click="renderOnChanges = true; renderCurrentNetwork()">

                <span>Render</span>
              </button>
              </span>

            </div>
            <div class="rendering-overlay" v-if="graphIsRendering">
              <div class="rendering-overlay-text-wrapper">
                <span class="">
                  <i class="mdi mdi-reload" />
                  <span class="ml-1">Rendering …</span>
                </span>
              </div>
            </div>
            <div ref="main-canvas-container" class="main-canvas-container"></div>
            <div class="overlay-notification" v-if="shouldShowNotification">
              <span>
                The latest solution will be shown when the optimization is stopped
              </span>
            </div>
          </div>
          <PeriodDetails v-if="currentSession" />
        </div>

        <div class="column">
          <div class="network-graph-visualizer__tool-area-container">
            <div ref="minimap-canvas-container" class="minimap-canvas-container graph-box box"></div>
            <CurrentGraphElementDetails />
          </div>
        </div>
      </div>

    </div>

</template>

<script lang="ts">
import ActionNames from '@/store/actions/actionNames'
import MutationNames from '@/store/mutations/mutationNames'
import CurrentGraphElementDetails from '@/components/graphElementDetails/CurrentGraphElementDetails.vue'
import PeriodDetails from '@/components/PeriodDetails.vue'
import { defineComponent } from 'vue'
import { PowerGrid } from '@/pgoApi/entities/powerGrid'
import { Session } from '@/pgoApi/entities/session'
import { SinglePeriodSettings } from '@/pgoApi/entities/solution'
import { ClickEdgeEvent, ClickNodeEvent } from '@/utils/graphRenderer/graphEvents'
import { GraphRenderer } from '@/utils/graphRenderer/graphRenderer'
import { SigmaV1GraphRenderer } from '@/utils/graphRenderer/sigmaV1GraphRenderer'
import { GraphElementType } from '@/store/state'
import { bestSolutionId } from '@/utils/constants'

const renderer: GraphRenderer = new SigmaV1GraphRenderer()

interface ComponentData {
  forceIsActive: boolean
  resizeHandler: any
  // Prompt before rendering when the graph has more than this many nodes
  autoRenderThreshold: number
  renderOnChanges: boolean
}

export default defineComponent({
  name: 'NetworkGraphVisualizer',
  components: {
    CurrentGraphElementDetails,
    PeriodDetails,
  },
  data(): ComponentData {
    return {
      forceIsActive: false,
      resizeHandler: undefined,
      autoRenderThreshold: 5000,
      renderOnChanges: false,
    }
  },
  mounted() {
    this.resizeHandler = () => {
      const viewportWidth = document.documentElement.offsetWidth
      const viewportWidthCutoff = 1920
      if (viewportWidth < viewportWidthCutoff) {
        this.mainCanvasContainer.style.width = "600px"
        this.mainCanvasContainer.style.height = "600px"
      } else {
        this.mainCanvasContainer.style.width = "700px"
        this.mainCanvasContainer.style.height = "700px"
      }
    }
    this.resizeHandler()
    window.addEventListener('resize', this.resizeHandler)

    renderer.setMainContainer(this.mainCanvasContainer)
    renderer.setMinimapContainer(this.minimapCanvasContainer)
    this.bindEvents()
  },
  unmounted() {
    window.removeEventListener('resize', this.resizeHandler)
  },
  computed: {
    mainCanvasContainer(): HTMLDivElement {
      return this.$refs['main-canvas-container'] as HTMLDivElement
    },
    minimapCanvasContainer(): HTMLDivElement {
      return this.$refs['minimap-canvas-container'] as HTMLDivElement
    },
    currentNetwork(): PowerGrid | undefined {
      return this.$store.state.currentNetwork
    },
    currentSession(): Session | undefined {
      return this.$store.state.currentSession
    },
    currentPeriodIndex(): number {
      return this.$store.state.currentPeriodIndex
    },
    currentPeriodSolution(): SinglePeriodSettings | null {
      return this.$store.getters.currentPeriodSolution
    },
    shouldShowNotification() {
      return this.$store.state.currentSession?.optimizationIsRunning
          && this.$store.state.currentSolutionId === bestSolutionId
    },
    graphIsRendering: {
      get() {
        return this.$store.state.graphIsRendering
      },
      set(value) {
        this.$store.dispatch(ActionNames.SET_GRAPH_RENDERING_STATE, value)
      },
    },
    shouldAutoRender(): boolean {
      return this.currentNetwork?.nodes !== undefined
        && this.currentNetwork.nodes.length < this.autoRenderThreshold
    },
  },
  watch: {
    async currentNetwork(newNetwork) {
      if (this.shouldAutoRender) {
        this.renderOnChanges = true
        await this.renderCurrentNetwork()
      } else {
        this.renderOnChanges = false
        await this.clearCurrentNetwork()
      }
    },
    currentSession() {
      //
    },
    currentPeriodSolution(newSolution): void {
      if (newSolution !== undefined && this.renderOnChanges) {
        this.applySwitchStates()
      }
    },
    currentPeriodIndex(): void {
      if (this.renderOnChanges) {
        this.applySwitchStates()
      }
    },
  },
  methods: {
    async renderCurrentNetwork() {
      if (this.currentNetwork !== undefined) {
        this.graphIsRendering = true
        await this.domUpdates() // Wait for the rendering overlay to show
        renderer.setNetwork(this.currentNetwork as PowerGrid)
        renderer.render()
        this.graphIsRendering = false
      }
    },
    clearCurrentNetwork() {
      renderer.clear()
    },
    applySwitchStates() {
      if (!this.currentPeriodSolution) {
        return
      }
      renderer.applySwitchStates(this.currentPeriodSolution.switch_settings)
    },
    toggleForce() {
      renderer.useForceLayout(!this.forceIsActive)
      this.blurActiveButton()
      this.forceIsActive = !this.forceIsActive
    },
    resetGraph() {
      this.blurActiveButton()
      renderer.resetMainCamera()
    },
    renderGraph() {
      this.blurActiveButton()
      renderer.resetMainCamera()
    },
    blurActiveButton() {
      const button = document.activeElement as HTMLButtonElement
      button.blur() // Remove focus
    },
    async rebuildGraph() {
      this.blurActiveButton()
      this.forceIsActive = false
      this.graphIsRendering = true
      await this.domUpdates() // Wait for the rendering overlay to show
      renderer.render()
      this.graphIsRendering = false
      this.applySwitchStates()
    },
    onClickNode(event: ClickNodeEvent) {
      // renderer.highlightNode(event.id)
      this.$store.commit(MutationNames.SET_CURRENT_GRAPH_ELEMENT, { type: GraphElementType.node, id: event.id })
    },
    onClickEdge(event: ClickEdgeEvent) {
      // renderer.highlightNode(event.id)
      this.$store.commit(MutationNames.SET_CURRENT_GRAPH_ELEMENT, { type: GraphElementType.edge, id: event.id })
      if (event.ctrlKeyPressed) {
        const currentState = this.$store.getters.currentEdgeSwitchOpen
        if (currentState !== null) {
          this.$store.dispatch(ActionNames.SET_SWITCH_STATE, {
            edgeId: event.id,
            isOpen: !currentState,
          })
        }
      }
    },
    onClickBackground() {
      // renderer.unHighlightNode()
      // this.$store.commit(MutationNames.SET_CURRENT_NODE_ID, undefined)
    },
    bindEvents() {
      renderer.events.onClickNode((event) => {
        this.onClickNode(event)
      })
      renderer.events.onClickEdge((event) => {
        this.onClickEdge(event)
      })
      renderer.events.onClickBackground(() => {
        this.onClickBackground()
      })
    },
    /*
      Returns a promise that is resolved once all pending DOM updates are complete.
      Useful when we're about to block the main thread with expensive computation
     */
    domUpdates(): Promise<void> {
      // Note that the requestAnimationFrame callback is executed before rendering,
      // so we need to call it twice to do something *after* rendering
      return new Promise(resolve => requestAnimationFrame(() => {
        requestAnimationFrame(() => resolve())
      }))
    },
  },
})
</script>

<style scoped lang="scss">
  .force-active {
    background-color: $info !important;

    &:hover {
      background-color: #1d81c4 !important;
    }
  }

  .overlay-notification {
    @extend .notification;

    position: absolute;
    z-index: 1;
    bottom: 0px;
    left: 0px;

    margin: $app-notification-margin;
    width: calc(100% - 2 * #{$app-notification-margin});

    background-color: $app-notification-color;
    color: #4f4f4f;
  }

  .graph-box {
    position: relative;
    padding: 0 !important;
  }

  .graph-controls {
    position: absolute;
    top: 0;
    right: 0;
  }

  .render-prompt {
    position: absolute;
    //top: 50%;
    //transform: translateY(-50%);
    //left: 2rem;
    //right: 2rem;
    padding: 1.25rem;
  }

  .rendering-overlay {
    position: absolute;
    top: 0;
    right: 0;
    bottom: 0;
    left: 0;
    backdrop-filter: blur(4px);
    z-index: 1;
  }

  .rendering-overlay-text-wrapper {
    width: max-content;
    padding: .5em;
    color: #171717;
    background: rgb(255, 255, 255);
    border-bottom-right-radius: 4px;
  }

  .main-canvas-container {
    height: $main-graph-height;
    width: $main-graph-width;
  }

  .minimap-canvas-container {
    height: $minimap-height;
    width: $minimap-width;
  }

  .graph-control-button {
    @extend .button;
    @extend .is-small;
    @extend .has-text-white;

    position: relative;

    background-color: rgba(0, 60, 101, 0.50);
    z-index: 10;

    &:hover {
      background-color: rgba(0, 60, 101, 0.70);
    }
  }
</style>
