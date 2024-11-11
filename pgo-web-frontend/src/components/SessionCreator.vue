<template>
  <div class="session-creator">
    <h3>Create a session</h3>
    <span></span>
    <div class="session-creator__file-upload-container">
      <FileUploader
        @file-uploaded="handleForecastFileUploaded"
        button-text="Upload"
        accept-extensions=".json"
      />
      <span class="upload-description">
        {{
          forecastFile
            ? forecastFile.name
            : 'Upload a demand forecast'
        }}
      </span>
    </div>
    <div>
      <FileUploader
        @file-uploaded="handleStartConfigurationFileUploaded"
        button-text="Upload"
        accept-extensions=".json"
      />
      <span class="upload-description">
        {{
          startConfigurationFile
          ? startConfigurationFile.name
          : 'Upload a starting configuration'
        }}
      </span>
    </div>
    <button class="btn" v-if="canSubmit()" @click="createSession">+ Add session</button>
    <button class="btn" v-else disabled="disabled">+ Add session</button>
  </div>
</template>

<script lang="ts">
import { defineComponent } from 'vue'
import FileUploader from '@/components/ui/FileUploader.vue'
import ActionNames from '@/store/actions/actionNames'

interface ComponentData {
  forecastFile: File | undefined
  startConfigurationFile: File | undefined
}

export default defineComponent({
  name: 'SessionCreator',
  components: {
    FileUploader,
  },
  data(): ComponentData {
    return {
      forecastFile: undefined,
      startConfigurationFile: undefined,
    }
  },
  methods: {
    handleForecastFileUploaded(file: File): void {
      this.$data.forecastFile = file
    },
    handleStartConfigurationFileUploaded(file: File): void {
      this.$data.startConfigurationFile = file
    },
    createSession(): void {
      const { currentNetworkId } = this.$store.state
      this.$store.dispatch(
        ActionNames.CREATE_SESSION, {
          networkId: currentNetworkId as string,
          forecastFile: this.forecastFile as File,
          startConfigurationFile: this.startConfigurationFile as File,
        })
    },
    canSubmit(): boolean {
      const currentNetworkIsSet = this.$store.state.currentNetworkId !== undefined
      const forecastFileIsSet = this.forecastFile !== undefined

      return currentNetworkIsSet
             && forecastFileIsSet
    },
  },
})
</script>
