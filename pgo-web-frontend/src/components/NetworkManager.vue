<template>
  <span class="navbar-label is-size-6 has-text-weight-light has-text-white">Network</span>
  <div class="navbar-dropdown dropdown" v-clicked-outside="closeIfOpen" @click="toggle" :class="{'is-active': isOpen}">

    <div class="dropdown-trigger">
      <button class="button">
        <span class="">{{ currentNetworkId ?? 'Select a network' }}</span>
        <i class="mdi mdi-menu-down" aria-hidden="true"></i>
      </button>
    </div>

    <div class="dropdown-menu" id="" role="menu" @clicked="closeIfOpen">
      <div class="dropdown-content">
        <div
          v-for="(network, id) of networks"
          :key="id"
          class="row-wrapper"
          :class="{'active': isCurrentNetwork(id)}"
        >
          <a class="app-dropdown-link no-wrap"
             role="button"
             href="#"
             @click="setCurrentNetwork(id)"
          >
            <i class="mdi mdi-check-bold app-dropdown-icon blue" v-if="isCurrentNetwork(id)"></i>
            <span class="">
            {{ network.name }}
          </span>
          </a>
          <div class="action-btns-wrapper no-wrap">
            <a
              @click="deleteNetwork(id)"
              href="#"
              class="app-dropdown-action-btn"
              role="button"
            >
              <i class="mdi mdi-24px mdi-delete-outline" aria-hidden="true"></i>
            </a>
          </div>
        </div>

        <hr class="dropdown-divider" v-if="networks && Object.keys(networks).length">

        <a href="#" class="dropdown-item" @click="createNetwork">
          <i class="mdi mdi-file-plus-outline app-dropdown-icon" aria-hidden="true"></i>
          <span class="is-size-6">
            Add a new network
          </span>
        </a>

        <hr class="dropdown-divider">

        <a href="#" class="dropdown-item" @click="addDemoNetwork">
          <span class="is-size-6">
            <i class="mdi mdi-auto-fix" aria-hidden="true"></i>
            Add the demo network
          </span>
        </a>

      </div>
    </div>

  </div>
</template>
<script lang="ts">
import { defineComponent } from 'vue'
import { mapState } from 'vuex'
import { State } from '@/store/state'
import { Modal } from '@/utils/alert'
import ActionNames from '@/store/actions/actionNames'
import PgoApi from '@/pgoApi/pgoApi'
import { ModalType } from '@/utils/modal/modal'

const client = new PgoApi()

interface ComponentData {
  isOpen: boolean
}

export default defineComponent({
  name: 'NetworkManager',
  components: {
  },
  data() {
    return {
      isOpen: false,
    }
  },
  computed: {
    ...mapState({
      networks: (state) => (state as State).networks,
      currentNetworkId: (state) => (state as State).currentNetworkId,
    }),
  },
  methods: {
    setCurrentNetwork(id: string) {
      this.$store.dispatch(ActionNames.SET_CURRENT_NETWORK_ID, id)
    },
    isCurrentNetwork(id: string) {
      return this.$store.state.currentNetworkId === id
    },
    deleteNetwork(id: string): void {
      this.$store.dispatch(ActionNames.DELETE_NETWORK, { id })
    },
    async showNetworkAnalysis(networkId: string) {
      const analysis = await client.getNetworkAnalysis(networkId)
      await Modal.showNetworkAnalysis(analysis)
    },
    toggle(event: MouseEvent) {
      this.isOpen = !this.isOpen
    },
    closeIfOpen() {
      if (this.isOpen) {
        this.isOpen = false
      }
    },
    createNetwork() {
      this.$store.dispatch(ActionNames.SHOW_MODAL, ModalType.CreateNetwork)
    },
    addDemoNetwork() {
      this.$store.dispatch(ActionNames.ADD_DEMO_DATA, undefined)
    },
  },
})
</script>

<style scoped lang="scss">
@import "src/style/components/dropdown";
</style>
