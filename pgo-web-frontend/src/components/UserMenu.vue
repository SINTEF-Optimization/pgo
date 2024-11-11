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
        <i class="mdi mdi-24px mdi-account" aria-hidden="true"></i>
        <span class=""></span>
      </a>
    </div>

    <div class="dropdown-menu" id="" role="menu">
      <div class="dropdown-content">

        <a
          href="/.auth/sign-out"
          class="dropdown-item"
        >
          <span class="is-size-6">
            <i class="mdi mdi-logout" aria-hidden="true"></i>
            Sign out
          </span>
        </a>

      </div>
    </div>

  </div>
</template>
<script lang="ts">
import { defineComponent } from 'vue'

export default defineComponent({
  name: 'UserMenu',
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
