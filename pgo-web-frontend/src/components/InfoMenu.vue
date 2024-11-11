<template>
  <div
    class="navbar-dropdown dropdown is-right"
    v-clicked-outside="closeIfOpen"
    @click="toggle"
    :class="{'is-active': isOpen}"
  >

    <div class="dropdown-trigger">
      <a
        class="account-btn has-text-white"
        role="button"
        aria-label="User menu"
      >
        <i class="mdi mdi-24px mdi-file-question" aria-hidden="true"></i>
        <span class=""></span>
      </a>
    </div>

    <div class="dropdown-menu" id="" role="menu">
      <div class="dropdown-content">

        <a
          href="https://pgo.sintef.no/demo-client-user-manual/"
          target="_blank"
          class="dropdown-item"
        >
          <span class="is-size-6">
            <i class="mdi mdi-link" aria-hidden="true"></i>
            UI help
          </span>
        </a>

        <a
          href="/swagger"
          target="_blank"
          class="dropdown-item"
        >
          <span class="is-size-6">
            <i class="mdi mdi-link" aria-hidden="true"></i>
            API documentation
          </span>
        </a>

        <a
          href="https://pgo.sintef.no"
          target="_blank"
          class="dropdown-item"
        >
          <span class="is-size-6">
            <i class="mdi mdi-link" aria-hidden="true"></i>
            Product site
          </span>
        </a>

      </div>
    </div>

  </div>
</template>
<script lang="ts">
import { defineComponent } from 'vue'

export default defineComponent({
  name: 'InfoMenu',
  components: {
  },
  data() {
    return {
      isOpen: false,
    }
  },
  computed: {
  },
  methods: {
    toggle(event: MouseEvent) {
      this.isOpen = !this.isOpen
    },
    closeIfOpen() {
      if (this.isOpen) {
        this.isOpen = false
      }
    },
  },
})
</script>

<style scoped lang="scss">

</style>
