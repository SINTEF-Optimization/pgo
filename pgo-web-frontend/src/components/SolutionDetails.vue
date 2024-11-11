<template>
  <div class="solution-details box">
    <h3 class="title is-4">Solution values</h3>
    <div class="solution-values-container columns" v-if="currentSolutionInfo">
      <div class="column is-flex is-flex-direction-column no-wrap">
        <span class="solution-value-label">
          Objective value
        </span>
        <span class="solution-value-label">
          Feasible
        </span>
        <span class="solution-value-label">
          Optimal
        </span>
      </div>
      <div class="column is-flex is-flex-direction-column no-wrap">
        <span class="solution-value">
          {{ formatNumber(currentSolutionInfo?.objective_value)}}
        </span>
        <span class="solution-value">
          {{yesNo(currentSolutionInfo.is_feasible)}}
          <span v-if="currentSolutionInfo?.violations?.length">
            <a href="#" @click="showViolationsModal">
              <i class="mdi mdi-information-outline"></i>
              <span class="ml-1">
                {{ currentSolutionInfo.violations.length }}
                {{ currentSolutionInfo.violations.length === 1 ? "issue" : "issues" }}
              </span>
            </a>
          </span>
        </span>
        <span class="solution-value">
          {{yesNo(currentSolutionInfo.is_optimal)}}
        </span>
      </div>
    </div>
    <div class="solution-values-container columns" v-if="currentSolutionInfo">
      <div class="column is-flex is-flex-direction-column no-wrap">
        <span class="heading">Objective component</span>

          <span
          class="solution-value-label"
          v-for="(item, index) in currentSolutionInfo.objective_components"
          :key="index"
        >
          {{ item.name }}
        </span>
      </div>
      <div class="column is-flex is-flex-direction-column no-wrap">
        <span class="heading">Value</span>

        <span
        class="solution-value"
        v-for="(item, index) in currentSolutionInfo.objective_components"
        :key="index"
      >
          {{ formatNumber(item.value) }}
        </span>
      </div>
      <div class="column is-flex is-flex-direction-column no-wrap">
        <span class="heading">Weight</span>
        <span
          class="solution-value"
          v-for="(item, index) in currentSolutionInfo.objective_components"
          :key="index"
        >
          {{ formatNumber(item.weight) }}
        </span>
      </div>
    </div>
  </div>
</template>

<script lang="ts">
import ActionNames from '@/store/actions/actionNames'
import { defineComponent } from 'vue'
import { SolutionInfo } from '@/pgoApi/entities/solutionInfo'
import { formatNumber, yesNo } from '@/utils/stringFilters'
import { ModalType } from '@/utils/modal/modal'
import { DimensionedQuantity } from '@/models/dimensionedQuantity'

export default defineComponent({
  name: 'SolutionDetails',
  setup() {
    return {
      yesNo,
      formatNumber,
    }
  },
  computed: {
    currentSolutionInfo(): SolutionInfo | undefined {
      return this.$store.state.currentSolutionInfo
    },
  },
  methods: {
    async showViolationsModal() {
      this.$store.dispatch(ActionNames.SHOW_MODAL, ModalType.ConstraintViolations)
    },
    formatValue(value: number) {
      if (!value) {
        return ''
      }
      const result = (new DimensionedQuantity(value)).getString()
      return result
    },
  },
})
</script>
<style scoepd lang="scss">
</style>
