<template>
    <component
      v-if="$store.state.currentGraphElement"
      v-bind:is="currentElementDetailsComponent"
    />
    <GraphElementPanel v-else >
        <span class="tip-text">Click on elements in the network to view their properties here</span>
    </GraphElementPanel>
</template>

<script lang="ts">
import CurrentNodeDetails from '@/components/graphElementDetails/CurrentNodeDetails.vue'
import CurrentEdgeDetails from '@/components/graphElementDetails/CurrentEdgeDetails.vue'
import GraphElementPanel from '@/components/ui/graphElementPanel/GraphElementPanel.vue'
import { defineComponent } from 'vue'
import { CurrentGraphElement, GraphElementType } from '@/store/state'

export default defineComponent({
  name: 'CurrentGraphElementDetails',
  components: {
    GraphElementPanel,
  },
  computed: {
    currentElementDetailsComponent() {
      if (!this.$store.state.currentGraphElement) {
        return null
      }
      const currentElement = this.$store.state.currentGraphElement as CurrentGraphElement
      switch (currentElement.type) {
        case GraphElementType.node:
          return CurrentNodeDetails
        case GraphElementType.edge:
          return CurrentEdgeDetails
        default:
          return null
      }
    },
  },
})
</script>

<style scoped lang="scss">
  .graph-element-details {
    width: $minimap-width;
  }

  .tip-text {
    font-size: 14px;
    color: $text-middle-gray;
  }
</style>
