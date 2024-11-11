<template>
  <div class="name-value-pair space-below">
    <span class="name" :class="{indented: indented}">{{ name }}</span>
    <span class="value">{{ value }}</span>
  </div>
</template>

<script lang="ts">
import { defineComponent } from 'vue'

export default defineComponent({
  name: "NameValuePair",
  props: {
    name: String,
    value: [Number, String],
    indented: Boolean,
  },
  computed: {
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
  padding-left: 0.75em;
}
</style>
