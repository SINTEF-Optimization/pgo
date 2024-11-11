<template>
  <GraphElementPanel
    :title="currentNode.id"
  >
    <template #properties>
      <NameValuePair
        name="Type"
        value="Provider"
      />
      <NameValuePair
        name="ID"
        :value="currentNode.id"
      />
      <NameValuePair
        name="Output voltage"
        :value="currentNode.v_gen.getString()"
      />
      <NameValueGroup heading="Active power limits">
        <NameValuePair
          name="min"
          :value="currentNode.p_gen_min.getString()"
          indented
        />
        <NameValuePair
          name="max"
          :value="currentNode.p_gen_max.getString()"
          unit="W"
          indented
        />
      </NameValueGroup>
      <NameValueGroup heading="Reactive power limits">
        <NameValuePair
          name="min"
          :value="currentNode.q_gen_min.getString()"
          unit="MW"
          indented
        />
        <NameValuePair
          name="max"
          :value="currentNode.q_gen_max.getString()"
          unit="MW"
          indented
        />
      </NameValueGroup>
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
import NameValueGroup from '@/components/ui/graphElementPanel/NameValueGroup.vue'
import { defineComponent } from 'vue'
import { duration, yesNo } from '@/utils/stringFilters'
import { DimensionedQuantity } from '@/models/dimensionedQuantity'
import { DimensionedNode } from '@/models/dimensionedNode'
import { DimensionedNodeSolutionValues } from '@/models/dimensionedNodeSolutionValues'

export default defineComponent({
  name: 'ProviderNodeDetails',
  components: {
    GraphElementPanel,
    NameValuePair,
    NameValueGroup,
  },
  setup() {
    return {
      yesNo,
      duration,
    }
  },
  computed: {
    currentNode(): DimensionedNode | null {
      return this.$store.getters.currentNodeDimensioned
    },
    solutionValues(): DimensionedNodeSolutionValues | null {
      return this.$store.getters.currentNodeSolutionValuesDimensioned
    },
  },
  methods: {
    scale(
      initialValue: number | string | null,
      baseUnit: string | undefined,
      initialScaleMultiplier: number | undefined,
    ) {
      const scaled = new DimensionedQuantity(initialValue, baseUnit, initialScaleMultiplier)
      return scaled
    },
  },
})
</script>

<style scoped lang="scss">
</style>
