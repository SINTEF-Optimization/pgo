<template>
  <div class="modal " :class="{'is-active': isActive}" v-if="isActive">
    <div class="modal-background" @click="hide"></div>
    <div class="modal-content">
      <div class="contents box is-flex is-flex-direction-column">

        <h4 class="title is-4">Constraint violations</h4>

        <p v-if="violations?.length">The solution is not feasible because it fails to meet the following constraints.</p>
        <p v-else>The solution meets every constraint.</p>

        <div
          v-for="(violation, index) in violations"
          :key="index"
          class="constraint-violation-container"
        >
          <h6 class="constraint-name has-text-danger ">
            <i class="mdi mdi-close-octagon"></i> {{ violation.name }}
          </h6>
          <p class="violation-description is-size-6 has-text-weight-light" v-if="violation.description">
            {{ violation.description }}
          </p>
        </div>

        <div class="actions">
          <button class="button is-primary" @click="hide">
            <span>OK</span>
          </button>
        </div>

      </div>
    </div>
    <button class="modal-close is-large" aria-label="close" @click="hide"></button>
  </div>
</template>

<script lang="ts">
import { defineComponent } from 'vue'
import { ConstraintViolation, SolutionInfo } from '@/pgoApi/entities/solutionInfo'

interface ComponentData {
  isActive: boolean
}

function getInitialData(): ComponentData {
  return {
    isActive: true,
  }
}

export default defineComponent({
  name: 'ConstraintViolationsModal',
  components: {
  },
  data: getInitialData,
  computed: {
    violations(): ConstraintViolation[] | undefined {
      return this.$store.state.currentSolutionInfo?.violations
    },
  },
  methods: {
    reset() {
      Object.assign(this.$data, getInitialData())
    },
    hide() {
      this.isActive = false
    },
  },
})
</script>

<style scoped lang="scss">
.constraint-name {
  color: $sintef-dark-red;
}

.violation-description {
  // The icon on the line above is 1em wide
  // while 0.25em is a typical SPACE character
  margin-left: calc(1em + 0.25em);
}

.contents {
  >* {
    margin-bottom: 0.5rem;
  }
}

.actions {
  margin-top: 2rem;
}
</style>
