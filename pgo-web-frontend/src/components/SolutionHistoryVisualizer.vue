<template>
  <div class="solution-value-visualizer box">
    <h4 class="title is-4">Optimization progress</h4>
    <canvas ref="chart-canvas" class="mt-2"></canvas>
    <p class="is-size-7 mt-2">* The component weight has been applied</p>
    <div v-if="shouldShowNotification" class="blur-overlay">
      <div class="chart-notification">
        <span>
          The optimizer has not yet found a feasible solution
        </span>
      </div>
    </div>
  </div>
</template>

<script lang="ts">
import ActionNames from '@/store/actions/actionNames'
import { defineComponent } from 'vue'
import { Chart, ChartConfiguration, ChartTooltipItem } from 'chart.js'
import { ActionPayload } from 'vuex'
import { format } from 'date-fns'
import { SolutionInfo } from '@/pgoApi/entities/solutionInfo'

interface ComponentData {
  chart: Chart | undefined
  nextValue: SolutionInfo | undefined
  lastValue: SolutionInfo | undefined
  lastValueWasPlaceholder: boolean
  tickDelayMs: number
  nextTickTimeoutRef: NodeJS.Timeout | undefined
  lastMouseMoveEvent: MouseEvent | undefined
}

export default defineComponent({
  name: 'SolutionHistoryVisualizer',
  data(): ComponentData {
    return {
      chart: undefined,
      nextValue: undefined,
      lastValue: undefined,
      lastValueWasPlaceholder: false,
      tickDelayMs: 1000,
      nextTickTimeoutRef: undefined,
      lastMouseMoveEvent: undefined,
    }
  },
  mounted() {
    this.$store.subscribeAction((action: ActionPayload) => {
      switch (action.type) {
        case ActionNames.START_SESSION:
          this.startEventLoop()
          break
        case ActionNames.STOP_SESSION:
          this.stopEventLoop()
          break
      }
    })

    // Due to a bug in chart.js, the hover tooltip disappears when the chart updates,
    // which is all the time. So we keep track of the last mouse move event and re-emit
    // it after every chart update.
    const handleMouseMove = (event: Event) => {
      this.lastMouseMoveEvent = event as MouseEvent
    }
    const handleMouseLeave = () => {
      this.lastMouseMoveEvent = undefined
    }
    this.chartCanvas.addEventListener('mousemove', handleMouseMove)
    this.chartCanvas.addEventListener('mouseleave', handleMouseLeave)

    // Start the event loop if appropriate
    this.nextValue = this.currentSolutionInfo
    if (this.currentSession) {
      this.createChart()
      if (this.currentSession.optimizationIsRunning) {
        this.startEventLoop()
      }
    }
  },
  computed: {
    chartCanvas(): HTMLCanvasElement {
      return this.$refs['chart-canvas'] as HTMLCanvasElement
    },
    currentSession() {
      return this.$store.state.currentSession
    },
    currentSolutionId() {
      return this.$store.state.currentSolutionId
    },
    currentSolutionInfo() {
      return this.$store.state.currentSolutionInfo
    },
    shouldShowNotification() {
      if (!this.currentSolutionInfo) {
        return false
      }
      const currentSolutionInfo = this.currentSolutionInfo as SolutionInfo
      return !currentSolutionInfo.is_feasible
    },
  },
  watch: {
    currentSession(newSession, oldSession) {
      if (this.chart
          && (!newSession
              || newSession.id !== oldSession.id)) {
        this.destroyChart()
      }
    },
    currentSolutionInfo(newSolutionInfo, previousSolutionInfo) {
      this.nextValue = newSolutionInfo

      if (newSolutionInfo?.is_feasible
          && !previousSolutionInfo?.is_feasible
      ) {
        this.stopEventLoop()
        this.destroyChart()
        this.startEventLoop()
      }
    },
  },
  methods: {
    startEventLoop() {
      if (!this.nextTickTimeoutRef) {
        this.doLoop()
      }
    },
    stopEventLoop() {
      if (this.nextTickTimeoutRef) {
        clearTimeout(this.nextTickTimeoutRef)
        this.nextTickTimeoutRef = undefined
      }
    },
    destroyChart() {
      if (this.chart) {
        this.chart.destroy()
        this.chart = undefined
      }
    },
    doLoop() {
      if (!this.currentSolutionInfo) {
        this.nextTick()
        return
      }
      // If needed, create the chart
      if (!this.chart) {
        this.createChart()
      }
      // Add the next point
      if (this.nextValue) {
        this.pushValue(this.nextValue)
        this.lastValue = this.nextValue
        this.nextValue = undefined
        this.lastValueWasPlaceholder = false
      } else if (this.lastValue) {
        this.pushValue(this.lastValue)
        this.lastValueWasPlaceholder = true
      }

      ;(this.chart as Chart).update()
      // Re-emit the last mousemove event to avoid any active tooltip disappearing after the update
      if (this.lastMouseMoveEvent) {
        this.chartCanvas.dispatchEvent(this.lastMouseMoveEvent)
      }
      this.nextTick()
    },
    nextTick() {
      this.nextTickTimeoutRef = setTimeout(this.doLoop, this.tickDelayMs)
    },
    pushValue(value: SolutionInfo) {
      const chart = this.chart as any
      // Remove the previous value if it's a placeholder value (and it's not the only value)
      if (chart.data.datasets[0].data.length > 1
          && this.lastValueWasPlaceholder
      ) {
        for (let i = 0; i < chart.data.datasets.length; i++) {
          chart.data.datasets[i].data.pop()
        }
      }
      // Add the new value
      const date = new Date()
      chart.data.datasets[0].data.push({
        t: date,
        y: value.objective_value,
      })
      for (let i = 1; i < chart.data.datasets.length; i++) {
        const component = value.objective_components[i - 1]
        chart.data.datasets[i].data.push({
          t: date,
          y: component.value * component.weight,
        })
      }
    },
    createChart() {
      const solutionInfo = this.$store.state.currentSolutionInfo
      if (!solutionInfo) {
        throw new Error("Could not create the chart because there's no currentSolutionInfo")
      }
      const defaultDatasetProperties = {
        lineTension: 0,
        pointRadius: 0,
      }
      const colorGenerator = this.getColorGenerator()
      const ctx = this.chartCanvas.getContext('2d') as CanvasRenderingContext2D
      this.chart = new Chart(ctx, {
        type: 'line',
        data: {
          datasets: [
            {
              ...defaultDatasetProperties,
              label: 'Objective value',
              ...colorGenerator.next().value,
              data: [],
            },
            ...solutionInfo.objective_components.map(oc => ({
              ...defaultDatasetProperties,
              label: oc.name + "*",
              data: [],
              ...colorGenerator.next().value,
            })),
          ],
        },
        options: {
          legend: {
            align: "center",
            position: "bottom",
            labels: {
              boxWidth: 30,
              fontFamily: "'Fira sans', sans-serif",
            },
          },
          tooltips: {
            mode: 'nearest',
            intersect: false,
            callbacks: {
              title: function(tooltipItems: ChartTooltipItem[]) {
                if (tooltipItems.length) {
                  const dateStr = tooltipItems[0].label
                  return format(new Date(dateStr as string), "MMMM d yyyy HH:mm:ss")
                }
              },
            },
          },
          animation: { duration: 1 },
          scales: {
            yAxes: [{
              gridLines: {
                display: false,
              },
            }],
            xAxes: [{
              type: 'time',
              distribution: 'linear',
              time: {
                displayFormats: {
                  second: 'HH:mm:ss',
                },
                unit: 'second',
              },
              gridLines: {
                display: false,
              },
              ticks: {
                source: 'data',
                maxRotation: 0,
                callback: function(value, index, values) {
                  // Only show the first and last tick
                  if (index === 0 || index === values.length - 1) {
                    return value
                  }
                },
              },
            }],
          },
        },
      } as ChartConfiguration)
    },
    getColorGenerator() {
      return (function * () {
        const settings = [
          {
            backgroundColor: 'rgba(0, 0, 0, 0)',
            borderColor: 'rgb(255, 99, 132)',
          },
          {
            backgroundColor: 'rgba(0, 0, 0, 0)',
            borderColor: 'rgb(98,229,50)',
          },
          {
            backgroundColor: 'rgba(0, 0, 0, 0)',
            borderColor: 'rgb(254, 242, 0)',
          },
          {
            backgroundColor: 'rgba(0, 0, 0, 0)',
            borderColor: 'rgb(255,112,2)',
          },
          {
            backgroundColor: 'rgba(0, 0, 0, 0)',
            borderColor: 'rgb(2,102,255)',
          },
          {
            backgroundColor: 'rgba(0, 0, 0, 0)',
            borderColor: 'rgb(255,26,26)',
          },
        ]
        let index = 0
        while (true) {
          yield settings[index % settings.length]
          index++
        }
      })()
    },
  },
})
</script>

<style lang="scss" scoped>
  .solution-value-visualizer {
    min-width: 460px;
    position: relative;
  }

  //.chart-notification {
  //  position: absolute;
  //  z-index: 1;
  //  width: 100%;
  //  height: 100%;
  //  bottom: 0;
  //  left: 0;
  //
  //  //position: absolute;
  //  //z-index: 1;
  //
  //  //width: 100%;
  //  //top: 15%;
  //  //left: 0;
  //}

  .blur-overlay {
    position: absolute;
    bottom: 0;
    left: 0;
    backdrop-filter: blur(2px);
    height: calc(100% - 15%);
    width: 100%;
  }

  .chart-notification {
    @extend .notification;
    position: absolute;
    bottom: 0;
    left: 0;

    $app-notification-margin: 0.25rem;
    margin: $app-notification-margin;
    background-color: $app-notification-color;
    color: #4f4f4f;

    width: calc(100% - 2 * #{$app-notification-margin});
  }
</style>
