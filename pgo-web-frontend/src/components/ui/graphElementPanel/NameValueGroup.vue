<template>
  <div class="name-value-group">
    <NameValuePair
      :name="heading"
    />
    <slot></slot>
  </div>
</template>

<script lang="ts">
import NameValuePair from '@/components/ui/graphElementPanel/NameValuePair.vue'
import { defineComponent } from 'vue'

export default defineComponent({
  name: "NameValueGroup",
  components: {
    NameValuePair,
  },
  props: {
    heading: String,
  },
})
</script>

<style scoped lang="scss">
.name, .value {
  font-size: 14px;
}
.name {
  display: inline-block;
  width: 8em;
  color: $text-middle-gray;
}
.space-below {
  @extend .mb-1;
}

.indented {
  padding-left: 0.5em;
}
</style>
