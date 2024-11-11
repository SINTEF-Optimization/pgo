<template>
    <Navbar />
    <div class="main-container">
      <div class="columns">
        <div class="column">
          <div v-if="shouldShowBestSolutionNotification" class="go-to-best-solution-notification box">
            <p>
              <span class="icon">
                <i class="mdi mdi-information-outline"></i>
              </span>
              Select the <a href="#" class="" @click="goToBestSolution()">{{ bestSolutionId }} solution</a>
              to view the optimization progress
            </p>
          </div>
          <ControlPanel v-if="$store.state.currentNetworkId" />

          <SolutionDetails v-if="$store.state.currentSolutionInfo" />
          <SolutionHistoryVisualizer v-if="shouldShowOptimizationProgress" />
        </div>
        <div class="column">
          <NetworkGraphVisualizer v-if="$store.state.currentNetworkId" />
        </div>
      </div>
      <CreateNetworkModal />
      <CreateSessionModal />
      <CreateSolutionModal />
      <Modal />
    </div>
</template>

<script lang="ts">
import { bestSolutionId } from '@/utils/constants'
import ActionNames from '@/store/actions/actionNames'
import NetworkGraphVisualizer from '@/components/NetworkGraphVisualizer.vue'
import SolutionHistoryVisualizer from '@/components/SolutionHistoryVisualizer.vue'
import Navbar from '@/components/Navbar.vue'
import SolutionDetails from '@/components/SolutionDetails.vue'

import { defineComponent } from 'vue'
import ControlPanel from '@/components/ControlPanel.vue'
import CreateNetworkModal from '@/components/CreateNetworkModal.vue'
import CreateSessionModal from '@/components/CreateSessionModal.vue'
import CreateSolutionModal from '@/components/CreateSolutionModal.vue'
import Modal from '@/components/ui/Modal.vue'

export default defineComponent({
  name: 'Home',
  components: {
    NetworkGraphVisualizer,
    SolutionDetails,
    SolutionHistoryVisualizer,
    Navbar,
    ControlPanel,
    CreateNetworkModal,
    CreateSessionModal,
    CreateSolutionModal,
    Modal,
  },
  setup() {
    return {
      bestSolutionId,
    }
  },
  async mounted() {
    await this.$store.dispatch(ActionNames.FETCH_SERVER_STATUS, undefined)
    await this.$store.dispatch(ActionNames.__AUTO_SELECT_NETWORK, undefined)
  },
  computed: {
    shouldShowOptimizationProgress() {
      return this.$store.state.currentSolutionInfo
        && this.$store.state.currentSolutionId === bestSolutionId
    },
    shouldShowBestSolutionNotification() {
      return this.$store.state.currentSession?.optimizationIsRunning
        && this.$store.state.currentSolutionId !== undefined
        && this.$store.state.currentSolutionId !== bestSolutionId
    },
  },
  methods: {
    goToBestSolution() {
      this.$store.dispatch(ActionNames.SET_CURRENT_SOLUTION_ID, bestSolutionId)
    },
  },
})
</script>

<style lang="scss">
  .main-container {
    margin-top: $box-padding;
    width: $content-width-medium;
  }

  .go-to-best-solution-notification {
    background-color: $app-notification-color;
  }

  @media (min-width: $screen-size-medium) {
    .main-container {
      width: $content-width-large;
    }
  }
</style>
