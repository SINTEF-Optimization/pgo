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
import { DimensionedNode } from '@/models/dimensionedNode'
import { DimensionedNodeSolutionValues } from '@/models/dimensionedNodeSolutionValues'

export default defineComponent({
  name: 'TransitionNodeDetails',
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
  },
})
</script>

<style scoped lang="scss">
</style>
