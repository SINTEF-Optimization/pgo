<template>
  <div class="modal " :class="{'is-active': isActive}" v-if="isActive">
    <div class="modal-background" @click="reset"></div>
    <div class="modal-content">
      <div class="content box is-flex is-flex-direction-column">

        <h4 class="title is-4">Create a scenario</h4>

        <div class="field">
          <label class="label">Demand forecast</label>
          <div class="control">
            <FileUploader
              @file-uploaded="handleSessionFileUploaded"
              accept-extensions=".json"
            />
          </div>
        </div>

        <div class="field">
          <label class="label">Starting configuration (optional)</label>
          <div class="control">
            <FileUploader
              @file-uploaded="handleStartConfigurationFileUploaded"
              accept-extensions=".json"
            />
          </div>
        </div>

        <div class="field">
          <label class="label">Scenario name</label>
          <div class="control">
            <input type="text" class="input" v-model="sessionName">
          </div>
        </div>

        <div class="actions">
          <button
            class="button is-primary"
            :class="{'is-loading': uploading}"
            @click="createSession"
            :disabled="!canSubmit()"
          >
            <span>Create</span>
          </button>
          <button class="button" @click="reset">
            <span>Cancel</span>
          </button>
        </div>

      </div>
    </div>
    <button class="modal-close is-large" aria-label="close" @click="reset"></button>
  </div>
</template>

<script lang="ts">
import FileUploader from '@/components/ui/FileUploader.vue'
import ActionNames from '@/store/actions/actionNames'
import { defineComponent } from 'vue'
import { ActionPayload } from 'vuex'
import { ModalType } from '@/utils/modal/modal'

interface ComponentData {
  isActive: boolean
  sessionName: string
  forecastFile: File | undefined
  startConfigurationFile: File | undefined
  uploading: boolean

}

function getInitialData(): ComponentData {
  return {
    isActive: false,
    sessionName: "",
    forecastFile: undefined,
    startConfigurationFile: undefined,
    uploading: false,
  }
}

export default defineComponent({
  name: 'CreateSessionModal',
  components: {
    FileUploader,
  },
  data: getInitialData,
  mounted() {
    this.$store.subscribeAction((action: ActionPayload) => {
      if (action.type === ActionNames.SHOW_MODAL
          && action.payload === ModalType.CreateSession) {
        this.initiate()
      }
    })
  },
  methods: {
    initiate() {
      this.isActive = true
    },
    handleSessionFileUploaded(file: File) {
      this.forecastFile = file
      this.sessionName = file.name.replace(/\.json/, '')
    },
    handleStartConfigurationFileUploaded(file: File) {
      this.startConfigurationFile = file
    },
    async createSession() {
      if (!this.canSubmit()) {
        return
      }
      this.uploading = true
      try {
        const { currentNetworkId } = this.$store.state
        await this.$store.dispatch(ActionNames.CREATE_SESSION, {
          id: this.sessionName,
          networkId: currentNetworkId as string,
          forecastFile: this.forecastFile as File,
          startConfigurationFile: this.startConfigurationFile as File,
        })
      } finally {
        this.uploading = false
      }
      this.reset()
    },
    canSubmit() {
      return this.sessionName && this.forecastFile
    },
    reset() {
      Object.assign(this.$data, getInitialData())
    },
  },
})
</script>

<style scoped lang="scss">
.content {
  >*:not(:last-child) {
    margin-bottom: 2rem;
  }
}

.actions {
  :not(:first-child) {
    margin-left: 0.5rem;
  }
}
</style>
