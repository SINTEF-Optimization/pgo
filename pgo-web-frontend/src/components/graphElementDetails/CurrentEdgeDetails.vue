<template>
  <GraphElementPanel
  >
    <template #header>
      <div class="card-header flex is-justify-content-space-between is-align-items-center">
        <h6 class="card-header-title is-size-6">{{currentEdge.id}}</h6>
        <div class="pr-1" v-if="$store.state.currentSolution && currentEdge.switchable">
          <button class="button is-white" @click="toggleSwitch" :disabled="!$store.state.currentSolution">
            <span class="icon">
              <i class="mdi mdi-electric-switch" v-if="this.currentEdgeSwitchOpen === true"></i>
              <i class="mdi mdi-electric-switch-closed" v-if="this.currentEdgeSwitchOpen === false"></i>
            </span>
          </button>
        </div>
      </div>
    </template>

    <template #properties>
      <NameValuePair
        name="Type"
        value="Line"
      />
      <NameValuePair
        name="ID"
        :value="currentEdge.id"
      />
      <NameValuePair
        name="Switchable"
        :value="yesNo(currentEdge.switchable)"
      />
      <NameValuePair
        name="Breaker"
        :value="yesNo(currentEdge.breaker)"
      />
      <NameValuePair
        name="Resistance"
        :value="currentEdge.r.getString()"
        unit="&#8486;"
      />
      <NameValuePair
        name="Reactance"
        :value="currentEdge.x.getString()"
        unit="&#8486;"
      />
      <NameValuePair
        name="Max current"
        :value="currentEdge.imax.getString()"
        unit="A"
      />
      <NameValuePair
        name="Max voltage"
        :value="currentEdge.vmax.getString()"
        unit="kV"
      />
      <NameValuePair
        name="Switching cost"
        :value="currentEdge.switching_cost"
      />
      <NameValuePair
        name="Fault frequency"
        :value="currentEdge.fault_frequency"
      />
      <NameValuePair
        name="Sectioning time"
        :value="duration(currentEdge.sectioning_time)"
      />
      <NameValuePair
        name="Repair time"
        :value="duration(currentEdge.repair_time)"
      />
    </template>
    <template #solutionValues>
      <NameValuePair
        name="Switch state"
        :value="switchState"
      />
      <NameValuePair
        name="Current"
        :value="solutionValues?.current.getString() ?? '-'"
        unit="kA"
      />
      <NameValuePair
        name="Active power"
        :value="solutionValues?.activePower.getString() ?? '-'"
        unit="MW"
      />
      <NameValuePair
        name="Reactive power"
        :value="solutionValues?.reactivePower.getString() ?? '-'"
        unit="MVAr"
      />
      <NameValuePair
        name="Thermal loss"
        :value="solutionValues?.thermalLoss.getString() ?? '-'"
        unit="MVAr"
      />
    </template>
  </GraphElementPanel>
</template>

<script lang="ts">
import GraphElementPanel from '@/components/ui/graphElementPanel/GraphElementPanel.vue'
import NameValuePair from '@/components/ui/graphElementPanel/NameValuePair.vue'
import { defineComponent } from 'vue'
import { duration, yesNo } from '@/utils/stringFilters'
import { DimensionedEdge } from '@/models/dimensionedEdge'
import { DimensionedEdgeSolutionValues } from '@/models/dimensionedEdgeSolutionValues'
import { SinglePeriodSettings } from '@/pgoApi/entities/solution'
import ActionNames from '@/store/actions/actionNames'

export default defineComponent({
  name: 'CurrentEdgeDetails.',
  setup() {
    return {
      yesNo,
      duration,
    }
  },
  components: {
    GraphElementPanel,
    NameValuePair,
  },
  computed: {
    currentEdge(): DimensionedEdge | null {
      return this.$store.getters.currentEdgeDimensioned
    },
    solutionValues(): DimensionedEdgeSolutionValues | null {
      return this.$store.getters.currentEdgeSolutionValuesDimensioned
    },
    currentEdgeSwitchOpen(): boolean | null {
      return this.$store.getters.currentEdgeSwitchOpen
    },
    switchState() {
      if (!this.currentEdge?.switchable) {
        return "N/A"
      }
      switch (this.currentEdgeSwitchOpen) {
        case true:
          return "Open"
        case false:
          return "Closed"
        default:
          return "Unknown"
      }
    },
  },
  methods: {
    toggleSwitch() {
      if (this.currentEdgeSwitchOpen === null) {
        throw new Error("Can not toggle this - it may not be a switch")
      }

      this.$store.dispatch(ActionNames.SET_SWITCH_STATE, {
        edgeId: this.currentEdge!.id,
        isOpen: !this.currentEdgeSwitchOpen,
      })
    },
  },
})
</script>

<style scoped lang="scss">
.name, .value {
  font-size: 14px;
}

.name {
  display: inline-block;
  width: 8.5em;
  color: $text-middle-gray;
}

.space-below {
  @extend .mb-1;
}

.title {
  color: #646464;
}
</style>
