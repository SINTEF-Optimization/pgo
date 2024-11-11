<template>
  <component
    v-bind:is="currentNodeTypeComponent"
  />
</template>

<script lang="ts">
import { defineComponent } from 'vue'
import { NodeType } from '@/pgoApi/entities/node'
import ProviderNodeDetails from '@/components/graphElementDetails/ProviderNodeDetails.vue'
import ConsumerNodeDetails from '@/components/graphElementDetails/ConsumerNodeDetails.vue'
import TransitionNodeDetails from '@/components/graphElementDetails/TransitionNodeDetails.vue'

export default defineComponent({
  name: 'CurrentNodeDetails',
  props: {
    id: String,
  },
  computed: {
    currentNodeTypeComponent() {
      if (!this.$store.state.currentGraphElement) {
        return null
      }
      switch (this.$store.getters.currentNode?.type) {
        case NodeType.provider:
          return ProviderNodeDetails
        case NodeType.consumer:
          return ConsumerNodeDetails
        case NodeType.transition:
          return TransitionNodeDetails
        default:
          return null
      }
    },
  },
})
</script>

<style scoped lang="scss">

</style>
