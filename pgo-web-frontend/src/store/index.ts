import { createStore } from 'vuex'
import { state } from '@/store/state'
import mutations from '@/store/mutations/mutations'
import getters from '@/store/getters/getters'
import actions from '@/store/actions/actions'
import { Store } from '@/store/store'

export default createStore({
  state,
  mutations,
  actions,
  getters,
})

declare module '@vue/runtime-core' {
  interface ComponentCustomProperties {
    $store: Store
  }
}
