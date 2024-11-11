<template>
  <GraphElementPanel
    :title="currentNode.id"
  >
    <template #properties>
      <NameValuePair
        name="Type"
        value="Consumer"
      />
      <NameValuePair
        name="ID"
        :value="currentNode.id"
      />
      <NameValuePair
        name="Min voltage"
        :value="currentNode.v_min.getString()"
        unit="kV"
      />
      <NameValuePair
        name="Max voltage"
        :value="currentNode.v_max.getString()"
        unit="kV"
      />
    </template>
    <template #demands v-if="demand">
      <NameValuePair
        name="Active power"
        :value="demand.p_load.getString()"
      />
      <NameValuePair
        name="Reactive power"
        :value="demand.q_load.getString()"
      />
    </template>
    <template #solutionValues v-if="solutionValues">
      <NameValuePair
        name="Voltage"
        :value="solutionValues.voltage.getString()"
      />
      <NameValuePair
        name="Current"
        :value="solutionValues.outgoingCurrent.getString()"
      />
      <NameValuePair
        name="Active power"
        :value="solutionValues.outgoingActivePower.getString()"
      />
      <NameValuePair
        name="Reactive power"
        :value="solutionValues.outgoingReactivePower.getString()"
      />
    </template>
  </GraphElementPanel>
</template>

<script lang="ts">
import GraphElementPanel from '@/components/ui/graphElementPanel/GraphElementPanel.vue'
import NameValuePair from '@/components/ui/graphElementPanel/NameValuePair.vue'
import { defineComponent } from 'vue'
import { Demand, LoadSeries } from '@/pgoApi/entities/demand'
import { DimensionedNode } from '@/models/dimensionedNode'
import { DimensionedNodeSolutionValues } from '@/models/dimensionedNodeSolutionValues'
import { DimensionedNodeDemand } from '@/models/dimensionedNodeDemand'

export default defineComponent({
  name: 'ConsumerNodeDetails',
  components: {
    GraphElementPanel,
    NameValuePair,
  },
  computed: {
    currentNode(): DimensionedNode | null {
      return this.$store.getters.currentNodeDimensioned
    },
    solutionValues(): DimensionedNodeSolutionValues | null {
      return this.$store.getters.currentNodeSolutionValuesDimensioned
    },
    demand(): DimensionedNodeDemand | null {
      if (!this.$store.state.currentSessionDemands
          || !this.currentNode
      ) {
        return null
      }
      const demands = this.$store.state.currentSessionDemands as Demand
      const currentNodeId = this.currentNode.id
      const currentPeriodIndex = this.$store.state.currentPeriodIndex

      const loadSeries = demands.loads
        .find((ls: LoadSeries) => ls.node_id === currentNodeId) as LoadSeries

      const p_load = loadSeries.p_loads[currentPeriodIndex]
      const q_load = loadSeries.q_loads[currentPeriodIndex]
      return new DimensionedNodeDemand({ p_load, q_load })
    },
  },
})
</script>

<style scoped lang="scss">
</style>
