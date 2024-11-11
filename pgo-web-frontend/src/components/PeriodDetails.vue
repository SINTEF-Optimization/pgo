<template>
  <div class="box" >
    <div class="network-object-details">
      <button
        v-if="currentSolution"
        class="button"
        :disabled="!$store.getters.currentSolutionHasPreviousPeriod"
        @click="setPeriod(currentPeriodIndex-1)"
      >
        Previous
      </button>

      <div class="is-flex is-flex-direction-column is-align-items-center" v-if="currentSolution">
        <span class="current-period-detail">{{ currentPeriodSolution ? currentPeriodSolution.period : '' }}</span>
        <span>{{getCurrentPeriodDateInterval()}}</span>
        <span>{{getCurrentPeriodIntervalText()}}</span>
      </div>

      <button
        v-if="currentSolution"
        class="button"
        :disabled="!$store.getters.currentSolutionHasNextPeriod"
        @click="setPeriod(currentPeriodIndex+1)"
      >
        Next
      </button>
    </div>
  </div>
</template>

<script lang="ts">
import MutationNames from '@/store/mutations/mutationNames'
import { defineComponent } from 'vue'
import { SinglePeriodSettings, Solution } from '@/pgoApi/entities/solution'
import { format } from 'date-fns'

export default defineComponent({
  name: 'PeriodDetails',
  computed: {
    currentSolution(): Solution | undefined {
      return this.$store.state.currentSolution
    },
    currentPeriodIndex(): number {
      return this.$store.state.currentPeriodIndex
    },
    currentPeriodSolution(): SinglePeriodSettings | null {
      return this.$store.getters.currentPeriodSolution
    },
  },
  methods: {
    setPeriod(period: number): void {
      this.$store.commit(MutationNames.SET_CURRENT_PERIOD, period)
    },
    getCurrentPeriodIntervalText(): string {
      if (!this.currentSolution) {
        return ''
      }
      return `Period ${this.currentPeriodIndex + 1} of ${this.currentSolution.period_solutions.length}`
    },
    getCurrentPeriodDateInterval(): string {
      if (!this.currentSolution) {
        return ''
      }
      const { currentSolutionInfo } = this.$store.state
      if (!currentSolutionInfo) {
        return `Period ${this.currentPeriodIndex + 1} of ${this.currentSolution.period_solutions.length}}`
      }
      const { start_time, end_time } = currentSolutionInfo.period_information[this.currentPeriodIndex].period
      const fromDate = format(new Date(start_time), "yyyy-MM-dd hh:mm")
      const toDate = format(new Date(end_time), "yyyy-MM-dd hh:mm")
      return `From ${fromDate} to ${toDate}`
    },
  },
})
</script>
<style scoped lang="scss">
  .network-object-details {
    display: flex;
    flex-direction: row;
    justify-content: space-between;
    align-items: center;
  }

  .current-period-detail {
    display: block;
  }
</style>
