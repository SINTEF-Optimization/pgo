<template>
  <div class="navbar-container is-flex is-justify-content-center is-align-items-center">
    <div class="navbar-content is-flex is-justify-content-space-between is-align-items-center">

      <div class="navbar-left-group is-flex is-flex-direction-row is-align-items-center">
        <NetworkManager />
        <SessionManager />
        <SolutionManager />
      </div>

      <div class="navbar-right-group">
        <span class="logotype is-size-4 has-text-weight-light has-text-white">Power Grid Optimizer</span>
      </div>
    </div>

    <div class="menu-tab">
      <InfoMenu />
      <UserMenu />
    </div>
  </div>

</template>

<script lang="ts">
import { defineComponent } from 'vue'
import NetworkManager from '@/components/NetworkManager.vue'
import SessionManager from '@/components/SessionManager.vue'
import SolutionManager from '@/components/SolutionManager.vue'
import UserMenu from '@/components/UserMenu.vue'
import InfoMenu from '@/components/InfoMenu.vue'

export default defineComponent({
  name: 'Navbar',
  components: {
    NetworkManager,
    SessionManager,
    SolutionManager,
    UserMenu,
    InfoMenu,
  },
  methods: {

  },
})
</script>

<style scoped lang="scss">
.navbar-container {
  width: 100%;
  background-color: $sintef-blue;
}

.navbar-content {
  width: $content-width-medium;
  height: 100%;
  padding: 1.5rem 0;
}

.logotype {
  color: white;
}

@media (min-width: $screen-size-medium) {
  .navbar-content {
    width: $content-width-large;
  }
}
</style>

<style lang="scss">
  .navbar-label {
    margin-right: $navbar-spacing;
  }

  .dropdown-item.active {
    background-color: $sintef-blue;
    a {
      color: white !important;
    }
  }

  .navbar-left-group {
    >*:not(:last-child) {
      margin-right: $navbar-spacing;
    }
  }

  .navbar-container{
    position: relative;
  }

  .menu-tab {
    position: absolute;
    top: 0;
    right: 1rem;

    padding: 0 .5rem;
    border-radius: 0 0 .25rem .25rem;

    display: flex;
    align-items: center;
    justify-content: center;

    background-color: #ec4100;

    >*:not(:last-child) {
      margin-right: 3px;
    }
  }

</style>
