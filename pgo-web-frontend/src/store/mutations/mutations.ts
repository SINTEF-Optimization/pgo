import MutationNames from '@/store/mutations/mutationNames'
import { MutationTree } from 'vuex'
import { State } from '@/store/state'
import { MutationSignatures } from '@/store/mutations/mutationSignatures'

const mutations: MutationTree<State> & MutationSignatures = {
  [MutationNames.SET_NETWORKS](state, payload) {
    state.networks = payload
  },
  [MutationNames.SET_CURRENT_NETWORK](state, payload) {
    state.currentNetwork = payload
  },
  [MutationNames.SET_CURRENT_NETWORK_ID](state, id) {
    state.currentNetworkId = id
  },
  [MutationNames.SET_CURRENT_SESSION](state, payload) {
    state.currentSession = payload
  },
  [MutationNames.SET_CURRENT_SESSION_DEMANDS](state, payload) {
    state.currentSessionDemands = payload
  },
  [MutationNames.SET_CURRENT_SOLUTION_ID](state, id) {
    state.currentSolutionId = id
  },
  [MutationNames.SET_CURRENT_SOLUTION](state, payload) {
    state.currentSolution = payload
  },
  [MutationNames.SET_CURRENT_SOLUTION_INFO](state, payload) {
    state.currentSolutionInfo = payload
  },
  [MutationNames.SET_CURRENT_PERIOD](state, payload) {
    state.currentPeriodIndex = payload
  },
  [MutationNames.SET_SERVER_STATUS](state, payload) {
    state.serverStatus = payload
  },
  [MutationNames.SET_CURRENT_GRAPH_ELEMENT](state, payload) {
    state.currentGraphElement = payload
  },
  [MutationNames.SET_GRAPH_RENDERING_STATE](state, payload) {
    state.graphIsRendering = payload
  },
}

export default mutations
