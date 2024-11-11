<template>
  <div class="solution-manager is-flex is-align-items-center" v-if="shouldBeVisible">
    <span class="navbar-label is-size-6 has-text-weight-light has-text-white">Solution</span>
    <div class="navbar-dropdown dropdown" v-clicked-outside="closeIfOpen" @click="toggle" :class="{'is-active': isOpen}">

      <div class="dropdown-trigger">
        <button class="button">
          <span class="" >{{ currentSolutionId ?? 'Select a solution' }}</span>
          <i class="mdi mdi-menu-down" aria-hidden="true"></i>
        </button>
      </div>

      <div class="dropdown-menu" id="" role="menu">
        <div class="dropdown-content">

          <div
            v-for="(solutionId, index) in solutionIds"
            :key="index"
            class="row-wrapper"
            :class="{'active': isCurrentSolution(solutionId)}"
          >
            <a class="app-dropdown-link no-wrap"
               role="button"
               href="#"
               @click="setCurrentSolutionId(solutionId)"
            >
              <i class="mdi mdi-check-bold app-dropdown-icon blue" v-if="isCurrentSolution(solutionId)"></i>
              <span class="">
                {{ solutionId }}
              </span>
            </a>
            <div class="action-btns-wrapper no-wrap">
              <a
                v-if="solutionIsDeletable(solutionId)"
                @click="deleteSolution(solutionId)"
                href="#"
                class="app-dropdown-action-btn"
                role="button"
              >
                <i class="mdi mdi-24px mdi-delete-outline" aria-hidden="true"></i>
              </a>
            </div>
          </div>

          <hr class="dropdown-divider" v-if="solutionIds">

          <a href="#" class="dropdown-item" @click="createSolution">
          <i class="mdi mdi-file-plus-outline app-dropdown-icon" aria-hidden="true"></i>
          <span class="is-size-6">
            Add a new solution
          </span>
          </a>

        </div>
      </div>

    </div>
  </div>
</template>

<script lang="ts">
import { defineComponent } from 'vue'
import ActionNames from '@/store/actions/actionNames'
import { ModalType } from '@/utils/modal/modal'
import { bestSolutionId } from '@/utils/constants'

export default defineComponent({
  name: 'SolutionManager',
  components: {
  },
  data() {
    return {
      isOpen: false,
    }
  },
  computed: {
    solutionIds(): string[] | null {
      const currentSession = this.$store.state.currentSession
      if (!currentSession) {
        return null
      }
      return currentSession.solutionIds
    },
    shouldBeVisible(): boolean {
      return this.$store.state.currentNetworkId !== undefined
        && this.$store.state.currentSession !== undefined
    },
    currentSolutionId(): string | undefined {
      return this.$store.state.currentSolutionId
    },
  },
  methods: {
    setCurrentSolutionId(id: string): void {
      this.$store.dispatch(ActionNames.SET_CURRENT_SOLUTION_ID, id)
    },
    isCurrentSolution(id: string): boolean {
      return id === this.$store.state.currentSolutionId
    },
    toggle() {
      this.isOpen = !this.isOpen
    },
    closeIfOpen() {
      if (this.isOpen) {
        this.isOpen = false
      }
    },
    createSolution() {
      this.$store.dispatch(ActionNames.SHOW_MODAL, ModalType.CreateSolution)
    },
    deleteSolution(id: string) {
      this.$store.dispatch(ActionNames.DELETE_SOLUTION, { id })
    },
    solutionIsDeletable(solutionId: string) {
      return solutionId !== bestSolutionId
    },
  },
})
</script>
<style scoped lang="scss">
@import "src/style/components/dropdown";
</style>
