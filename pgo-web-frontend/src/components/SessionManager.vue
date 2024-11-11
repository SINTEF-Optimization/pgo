<template>
  <div class="session-manager is-flex is-align-items-center" v-if="currentNetworkId">
    <span class="navbar-label is-size-6 has-text-weight-light has-text-white">Scenario</span>

    <div
      class="navbar-dropdown dropdown"
      @click="toggle"
      :class="{'is-active': isOpen}"
      v-clicked-outside="closeIfOpen"
    >

      <div class="dropdown-trigger" >
        <button class="button">
          <span class="" >{{ currentSession ? currentSession.id : 'Select a scenario  ' }}</span>
          <i class="mdi mdi-menu-down" aria-hidden="true"></i>
        </button>
      </div>

      <div class="dropdown-menu" id="" role="menu" @clicked="closeIfOpen">
        <div class="dropdown-content">
          <div
            v-for="(session, index) in sessionsForCurrentNetwork"
            :key="index"
            class="row-wrapper"
            :class="{'active': isActive(session)}"
          >
            <a class="app-dropdown-link no-wrap"
               role="button"
               href="#"
               @click="setCurrentSession(session)"
            >
              <i class="mdi mdi-check-bold app-dropdown-icon blue" v-if="isActive(session)"></i>
              <span class="">
                {{ session.id }}
              </span>
            </a>
            <div class="action-btns-wrapper no-wrap">
              <a
                @click="deleteSession(session)"
                href="#"
                class="app-dropdown-action-btn"
                role="button"
              >
                <i class="mdi mdi-24px mdi-delete-outline" aria-hidden="true"></i>
              </a>
            </div>
          </div>

          <hr class="dropdown-divider" v-if="sessionsForCurrentNetwork?.length">

          <a href="#" class="dropdown-item" @click="createSession">
            <i class="mdi mdi-file-plus-outline app-dropdown-icon" aria-hidden="true"></i>
            <span class="is-size-6">
              Add a new scenario
            </span>
          </a>

        </div>
      </div>
    </div>
  </div>
</template>

<script lang="ts">
import ActionNames from '@/store/actions/actionNames'

import { defineComponent } from 'vue'
import { Session } from '@/pgoApi/entities/session'
import { ModalType } from '@/utils/modal/modal'

interface ComponentData {
  isOpen: boolean
}

export default defineComponent({
  name: 'SessionManager',
  components: {
  },
  data(): ComponentData {
    return {
      isOpen: false,
    }
  },
  computed: {
    currentSession(): Session | undefined {
      return this.$store.state.currentSession
    },
    currentNetworkId(): string | undefined {
      return this.$store.state.currentNetworkId
    },
    sessionsForCurrentNetwork() {
      return this.$store.getters.sessionsForCurrentNetwork
    },
  },
  methods: {
    isActive(session: Session): boolean {
      const currentSession = this.$store.state.currentSession
      if (!currentSession) return false
      return session.id === currentSession.id
    },
    setCurrentSession(session: Session) {
      this.$store.dispatch(ActionNames.SET_CURRENT_SESSION, session)
    },
    deleteSession(session: Session) {
      this.$store.dispatch(ActionNames.DELETE_SESSION, session)
    },
    toggle() {
      this.isOpen = !this.isOpen
    },
    closeIfOpen() {
      if (this.isOpen) {
        this.isOpen = false
      }
    },
    createSession() {
      this.$store.dispatch(ActionNames.SHOW_MODAL, ModalType.CreateSession)
    },
  },
})
</script>

<style scoped lang="scss">
@import "src/style/components/dropdown";
</style>
