<template>
  <div clas="file-uploader">
    <div class="file" :class="{'has-name': file !== undefined}">
      <label class="file-label">
        <input
          class="file-input"
          type="file"
          @input="handleInput"
          :accept="acceptExtensions"
        >
        <span class="file-cta">
          <span class="file-icon">
            <i class="mdi mdi-upload"></i>
          </span>
          <span class="file-label">
            Choose a file…
          </span>
        </span>
        <span class="file-name" v-if="file !== undefined">
          {{ file.name }}
        </span>
      </label>
    </div>
    <slot></slot>
  </div>
</template>

<script lang="ts">
import { defineComponent } from 'vue'

interface ComponentData {
  file: File | undefined
}

export default defineComponent({
  name: 'FileUploader',
  emits: ['file-uploaded'],
  props: {
    // Comma-separated list, eg ".txt,.json"
    acceptExtensions: String,
  },
  data(): ComponentData {
    return {
      file: undefined,
    }
  },
  methods: {
    handleInput(event: InputEvent): void {
      const input = event.target as HTMLInputElement
      if (input.files && input.files.length) {
        const file = input.files[0]
        this.$emit('file-uploaded', file)
        this.file = file
      }
    },
  },
})
</script>

<style scoped lang="scss">

</style>
