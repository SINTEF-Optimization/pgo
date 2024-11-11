<template>
  <div class="modal " :class="{'is-active': isActive}" v-if="isActive">
    <div class="modal-background" @click="reset"></div>
    <div class="modal-content">
      <div class="content box is-flex is-flex-direction-column">

        <h4 class="title is-4">Create a solution</h4>

        <div class="field">
          <label class="label">Solution file</label>
          <div class="control">
            <FileUploader
              @file-uploaded="handleSolutionFileUploaded"
              accept-extensions=".json"
            />
          </div>
        </div>

        <div class="field">
          <label class="label">Solution name</label>
          <div class="control">
            <input type="text" class="input" v-model="solutionName">
          </div>
        </div>

        <div class="actions">
          <button
            class="button is-primary"
            :class="{'is-loading': uploading}"
            @click="createSolution"
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
  solutionName: string
  solutionFile: File | undefined
  uploading: boolean
}

function getInitialData(): ComponentData {
  return {
    isActive: false,
    solutionName: "",
    solutionFile: undefined,
    uploading: false,
  }
}

export default defineComponent({
  name: 'CreateSolutionModal',
  components: {
    FileUploader,
  },
  data: getInitialData,
  mounted() {
    this.$store.subscribeAction((action: ActionPayload) => {
      if (action.type === ActionNames.SHOW_MODAL
          && action.payload === ModalType.CreateSolution) {
        this.initiate()
      }
    })
  },
  methods: {
    initiate() {
      this.isActive = true
    },
    handleSolutionFileUploaded(file: File) {
      this.solutionFile = file
      this.solutionName = file.name.replace(/\.json/, '')
    },
    async createSolution() {
      if (!this.canSubmit()) {
        return
      }
      this.uploading = true
      try {
        const { currentNetworkId } = this.$store.state
        const sessionId = this.$store.state.currentSession?.id
        await this.$store.dispatch(ActionNames.CREATE_SOLUTION, {
          sessionId: sessionId as string,
          solutionId: this.solutionName,
          solutionFile: this.solutionFile as File,
        })
      } finally {
        this.uploading = false
      }

      this.reset()
    },
    canSubmit() {
      return this.solutionName && this.solutionFile
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
