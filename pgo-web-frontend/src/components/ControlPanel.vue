<template>
  <div class="control-panel box">
    <button
      class="button is-primary"
      v-if="hasCurrentSession && !currentSession.optimizationIsRunning"
      @click="startCurrentSession"
    >
      <span class="icon">
        <i class="mdi mdi-24px mdi-play"></i>
      </span>
      <span>Start optimization</span>
    </button>
    <button
      class="button is-warning"
      v-if="hasCurrentSession && currentSession.optimizationIsRunning"
      @click="stopCurrentSession"
    >
      <span class="icon">
        <i class="mdi mdi-24px mdi-stop"></i>
      </span>
      <span>Stop optimization</span>
    </button>

    <button
      class="button"
      @click="showCurrentNetworkAnalysis"
    >
      <span class="icon">
        <i class="mdi mdi-24px mdi-information-outline"></i>
      </span>
      <span>View network analysis</span>
    </button>

    <button
      class="button"
      @click="echoState"
      v-if="inDebugEnvironment"
    >
      <span class="icon">
        <i class="mdi mdi-24px mdi-bug-outline"></i>
      </span>
      <span>Echo State</span>
    </button>

  </div>

</template>

<script lang="ts">
import environment from '@/utils/environment'
import ActionNames from '@/store/actions/actionNames'
import PgoApi from '@/pgoApi/pgoApi'

import { defineComponent } from 'vue'
import { Session } from '@/pgoApi/entities/session'
import { HubConnection, HubConnectionBuilder, LogLevel } from '@microsoft/signalr'
import { SolutionInfo } from '@/pgoApi/entities/solutionInfo'
import { Modal } from '@/utils/alert'
import { bestSolutionId } from '@/utils/constants'

const client = new PgoApi()

interface ComponentData {
  realtimeConnection: HubConnection | undefined
}

export default defineComponent({
  name: 'ControlPanel',
  components: {
  },
  data(): ComponentData {
    return {
      realtimeConnection: undefined,
    }
  },
  async mounted() {
    if (this.currentSession?.optimizationIsRunning) {
      await this.establishRealtimeConnection()
    }
  },
  async dismounted() {
    await this.closeRealtimeConnection()
  },
  computed: {
    hasCurrentSession(): boolean {
      return this.$store.state.currentSession !== undefined
    },
    currentSession(): Session | undefined {
      return this.$store.state.currentSession
    },
    inDebugEnvironment(): boolean {
      return environment.isDevelopment
    },
  },
  watch: {
    async currentSession(newSession: Session | undefined) {
      await this.closeRealtimeConnection()
      if (newSession?.optimizationIsRunning) {
        await this.establishRealtimeConnection()
      }
    },
  },
  methods: {
    async startCurrentSession() {
      const session = this.$store.state.currentSession as Session
      await this.establishRealtimeConnection()
      await this.$store.dispatch(ActionNames.START_SESSION, session)
    },
    async establishRealtimeConnection() {
      if (!this.currentSession) {
        return
      }
      const connection = new HubConnectionBuilder()
        .withUrl('/solutionStatusHub')
        .configureLogging(environment.isDevelopment ? LogLevel.Information : LogLevel.Error)
        .withAutomaticReconnect()
        .build()

      connection.on('newSolutionStatus', async (solutionInfoJson: string) => {
        if (this.$store.state.currentSolutionId === bestSolutionId) {
          const solutionInfo = JSON.parse(solutionInfoJson) as SolutionInfo
          await this.$store.dispatch(ActionNames.ON_NEW_SOLUTION_INFO, solutionInfo)
        }
      })

      await connection.start()
      await connection.invoke('AddToGroup', this.currentSession.id)
      this.realtimeConnection = connection
    },
    async closeRealtimeConnection() {
      if (this.realtimeConnection) {
        await this.realtimeConnection.stop()
        this.realtimeConnection = undefined
      }
    },
    stopCurrentSession() {
      const session = this.$store.state.currentSession as Session
      this.$store.dispatch(ActionNames.STOP_SESSION, session)
    },
    async showCurrentNetworkAnalysis() {
      const networkId = this.$store.state.currentNetworkId as string
      const analysis = await client.getNetworkAnalysis(networkId)
      await Modal.showNetworkAnalysis(analysis)
    },
    echoState() {
      const state = {
        ...this.$store.state,
      }
      console.dir(state)
    },
  },
})
</script>

<style scoped lang="scss">
.control-panel {
  >*:not(:first-child) {
    &:not(:last-child) {
      margin-left: 0.25em;
    }
  }
}
</style>
