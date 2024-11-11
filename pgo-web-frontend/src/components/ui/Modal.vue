<template>
    <component :is="currentModal" :key="currentModalKey"/>
</template>

<script lang="ts">
import ActionNames from '@/store/actions/actionNames'
import ConstraintViolationsModal from '@/components/ConstraintViolationsModal.vue'
import { defineComponent, shallowRef } from 'vue'
import { ActionPayload } from 'vuex'
import { ModalType } from '@/utils/modal/modal'

interface ComponentData {
  currentModal: any
  currentModalKey: symbol
}

export default defineComponent({
  name: 'Modal',
  components: {
  },
  data(): ComponentData {
    return {
      currentModal: undefined,
      currentModalKey: Symbol("Modal identity"),
    }
  },
  mounted() {
    this.$store.subscribeAction((actionPayload: ActionPayload) => {
      if (actionPayload.type === ActionNames.SHOW_MODAL
        && actionPayload.payload === ModalType.ConstraintViolations
      ) {
        this.currentModal = shallowRef(ConstraintViolationsModal)
        // Changing the key forces the component to re-mount.
        // This is done to handle same modal being invoked repeatedly
        this.currentModalKey = Symbol("New modal identity")
      }
    })
  },
  computed: {
  },
  watch: {
  },
  methods: {
  },
})
</script>

<style scoped lang="scss">
</style>
