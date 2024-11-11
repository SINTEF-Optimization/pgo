<template>
  <div class="card">

    <slot name="header">
      <div class="card-header" v-if="title">
        <h6 class="card-header-title is-size-6">{{title}}</h6>
      </div>
    </slot>

    <div class="card-content">
      <slot name="default"></slot>

      <h6 class="heading" v-if="$slots.properties">Properties</h6>
      <div class="name-value-pairs-container no-wrap" v-if="$slots.properties">
          <slot name="properties"></slot>
      </div>

      <h6 class="heading mt-5" v-if="$slots.demands">Demand</h6>
      <div class="name-value-pairs-container no-wrap" v-if="$slots.demands">
          <slot name="demands"></slot>
      </div>

      <h6 class="heading mt-5" v-if="$slots.solutionValues">Solution values</h6>
      <div class="name-value-pairs-container no-wrap" v-if="$slots.solutionValues">
        <slot name="solutionValues"></slot>
      </div>

    </div>

  </div>
</template>

<script lang="ts">
import { defineComponent } from 'vue'

export default defineComponent({
  name: 'GraphElementPanel',
  props: {
    title: String,
    showProperties: {
      type: Boolean,
      default: false,
    },
    showSolutionValues: {
      type: Boolean,
      default: false,
    },
  },
})
</script>

<style scoped>

</style>
