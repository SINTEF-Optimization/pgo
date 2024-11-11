<template>
  <div class="modal " :class="{'is-active': isActive}" v-if="isActive">
    <div class="modal-background" @click="reset"></div>
    <div class="modal-content">
      <div class="content box is-flex is-flex-direction-column">

        <h4 class="title is-4">Create a network</h4>

        <div class="field">
          <label class="label">Network file</label>
          <div class="control">
            <FileUploader
              @file-uploaded="handleNetworkFileUploaded"
              accept-extensions=".json"
            />
          </div>
        </div>

        <div class="field">
          <label class="label">Network name</label>
          <div class="control">
            <input type="text" class="input" v-model="networkName">
          </div>
        </div>

        <div class="actions">
          <button
            class="button is-primary"
            :class="{'is-loading': uploading}"
            @click="createNetwork"
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
  networkName: string
  networkFile: File | undefined,
  uploading: boolean,
}

function getInitialData(): ComponentData {
  return {
    isActive: false,
    networkName: "",
    networkFile: undefined,
    uploading: false,
  }
}

export default defineComponent({
  name: 'CreateNetworkModal',
  components: {
    FileUploader,
  },
  data: getInitialData,
  mounted() {
    this.$store.subscribeAction((action: ActionPayload) => {
      if (action.type === ActionNames.SHOW_MODAL
          && action.payload === ModalType.CreateNetwork) {
        this.initiate()
      }
    })
  },
  methods: {
    initiate() {
      this.isActive = true
    },
    handleNetworkFileUploaded(file: File) {
      this.networkFile = file
      this.networkName = file.name.replace(/\.json/, '')
    },
    async createNetwork() {
      if (!this.canSubmit()) {
        return
      }
      this.uploading = true
      try {
        await this.$store.dispatch(ActionNames.CREATE_NETWORK, {
          networkDescriptionFile: this.networkFile as File,
          name: this.networkName,
        })
      } finally {
        this.uploading = false
      }
      this.reset()
    },
    canSubmit() {
      return this.networkName && this.networkFile
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
