import PgoApi from '@/pgoApi/pgoApi'
import ActionNames from '@/store/actions/actionNames'
import MutationNames from '@/store/mutations/mutationNames'
import { ActionTree } from 'vuex'
import { NetworkCollectionItem, NetworkDict, State } from '@/store/state'
import { ActionSignatures } from '@/store/actions/actionSignatures'
import { ServerStatus } from '@/pgoApi/entities/serverStatus'
import { logger } from '@/main'
import { Modal } from '@/utils/alert'
import { Session } from '@/pgoApi/entities/session'
import { bestSolutionId } from '@/utils/constants'

const client = new PgoApi()

const actions: ActionTree<State, State> & ActionSignatures = {
  async [ActionNames.CREATE_NETWORK]({ dispatch }, { networkDescriptionFile, name }) {
    logger.action(ActionNames.CREATE_NETWORK)
    try {
      await client.createNetwork({ id: name, networkDescriptionFile })
    }
    catch (error) {
      await Modal.showError("Could not upload the network", error.message)
      throw error
    }

    await dispatch(ActionNames.__ON_NETWORK_CREATED, { id: name })
  },
  async [ActionNames.DELETE_NETWORK]({ dispatch }, { id }) {
    logger.action(ActionNames.DELETE_NETWORK, id)
    try {
      await client.deleteNetwork(id)
    } catch (error) {
      await Modal.showError("Error while deleting the network", error.message)
      throw error
    }
    await dispatch(ActionNames.__RESET_CURRENT_NETWORK)
    await dispatch(ActionNames.FETCH_SERVER_STATUS)
  },
  async [ActionNames.__ON_NETWORK_CREATED]({ dispatch, state }, { id }) {
    logger.action(ActionNames.__ON_NETWORK_CREATED, id)
    await dispatch(ActionNames.FETCH_SERVER_STATUS)
    await dispatch(ActionNames.SET_CURRENT_NETWORK_ID, id)
  },
  async [ActionNames.SET_CURRENT_NETWORK_ID]({ commit, dispatch }, id) {
    logger.action(ActionNames.SET_CURRENT_NETWORK_ID, id)
    commit(MutationNames.SET_CURRENT_NETWORK_ID, id)
    await dispatch(ActionNames.__ON_CURRENT_NETWORK_ID_CHANGED)
  },
  async [ActionNames.__ON_CURRENT_NETWORK_ID_CHANGED]({ dispatch, commit, state }) {
    logger.action(ActionNames.__ON_CURRENT_NETWORK_ID_CHANGED)
    await dispatch(ActionNames.__RESET_CURRENT_SESSION)
    commit(MutationNames.SET_CURRENT_GRAPH_ELEMENT, undefined)
    if (state.currentNetworkId) {
      await dispatch(ActionNames.__LOAD_CURRENT_NETWORK)
      await dispatch(ActionNames.__AUTO_SELECT_SESSION)
    }
  },
  async [ActionNames.__LOAD_CURRENT_NETWORK]({ commit, state }) {
    logger.action(ActionNames.__LOAD_CURRENT_NETWORK)
    const id = state.currentNetworkId
    if (!id) {
      throw new Error('Attempted to load current network, but no network is selected')
    }
    const network = await client.getNetwork(id)
    commit(MutationNames.SET_CURRENT_NETWORK, network)
  },
  async [ActionNames.__RESET_CURRENT_NETWORK]({ commit, dispatch }) {
    logger.action(ActionNames.__RESET_CURRENT_NETWORK)
    commit(MutationNames.SET_CURRENT_NETWORK_ID, undefined)
    commit(MutationNames.SET_CURRENT_NETWORK, undefined)
    await dispatch(ActionNames.__ON_CURRENT_NETWORK_ID_CHANGED)
  },
  async [ActionNames.__AUTO_SELECT_NETWORK]({ state, dispatch }) {
    logger.action(ActionNames.__AUTO_SELECT_NETWORK)
    if (state.networks
        && Object.keys(state.networks).length === 1) {
      const networkId = Object.keys(state.networks)[0]
      await dispatch(ActionNames.SET_CURRENT_NETWORK_ID, networkId)
    }
  },
  async [ActionNames.FETCH_SERVER_STATUS]({ commit, dispatch }) {
    logger.action(ActionNames.FETCH_SERVER_STATUS)
    const serverStatus = await client.getServerStatus()
    commit(MutationNames.SET_SERVER_STATUS, serverStatus)
    await dispatch(ActionNames.__ON_SERVER_STATUS_FETCHED)
  },
  async [ActionNames.__ON_SERVER_STATUS_FETCHED]({ dispatch, commit, state }) {
    logger.action(ActionNames.__ON_SERVER_STATUS_FETCHED)
    await dispatch(ActionNames.__UPDATE_NETWORK_LIST_FROM_SERVER_STATUS)
    await dispatch(ActionNames.__UPDATE_CURRENT_SESSION_FROM_SERVER_STATUS)
  },
  async [ActionNames.__UPDATE_NETWORK_LIST_FROM_SERVER_STATUS]({ commit, state }) {
    logger.action(ActionNames.__UPDATE_NETWORK_LIST_FROM_SERVER_STATUS)
    if (!state.serverStatus) {
      throw new Error('Attempted to access the server status, but it wasn\'t found in state')
    }
    const serverStatus = state.serverStatus as ServerStatus

    const requests = serverStatus.networks.map(networkId => {
      return {
        id: networkId,
        name: networkId,
        sessions: serverStatus.sessions
          .filter(session => session.networkId === networkId),
      } as NetworkCollectionItem
    })

    const networks = await Promise.all(requests)
    const networkDict: NetworkDict = {}
    networks.forEach(network => {
      networkDict[network.id] = network
    })
    commit(MutationNames.SET_NETWORKS, networkDict)
  },
  async [ActionNames.__UPDATE_CURRENT_SESSION_FROM_SERVER_STATUS]({ commit, state }) {
    logger.action(ActionNames.__UPDATE_CURRENT_SESSION_FROM_SERVER_STATUS)
    if (!state.networks
        || !state.currentNetworkId
        || !state.currentSession) {
      return
    }
    const {currentNetworkId, currentSession} = state
    const refreshedSession = state.networks[currentNetworkId].sessions
      .find(s => s.id === currentSession.id)
    commit(MutationNames.SET_CURRENT_SESSION, refreshedSession)
  },
  async [ActionNames.CREATE_SESSION]({ state, dispatch }, payload) {
    logger.action(ActionNames.CREATE_SESSION, payload)
    try {
      await client.createSession(payload)
    } catch (error) {
      await Modal.showError("Error while creating the scenario", error.message)
      throw error
    }
    await dispatch(ActionNames.FETCH_SERVER_STATUS)
    await dispatch(ActionNames.__ON_SESSION_CREATED, { id: payload.id })
  },
  async [ActionNames.SET_CURRENT_SESSION]({ commit, dispatch }, session) {
    logger.action(ActionNames.SET_CURRENT_SESSION, session)
    commit(MutationNames.SET_CURRENT_SESSION, session)
    await dispatch(ActionNames.__ON_CURRENT_SESSION_CHANGED)
  },
  async [ActionNames.__ON_SESSION_CREATED]({ state, dispatch }, { id }) {
    logger.action(ActionNames.__ON_SESSION_CREATED, { id })
    if (state.networks && state.currentNetworkId) {
      const session = state.networks[state.currentNetworkId].sessions
        .find(s => s.id === id)
      if (session) {
        await dispatch(ActionNames.SET_CURRENT_SESSION, session)
      }
    }
  },
  async [ActionNames.__RESET_CURRENT_SESSION]({ commit, dispatch }) {
    logger.action(ActionNames.__RESET_CURRENT_SESSION)
    commit(MutationNames.SET_CURRENT_SESSION, undefined)
    commit(MutationNames.SET_CURRENT_SESSION_DEMANDS, undefined)
    await dispatch(ActionNames.__RESET_CURRENT_SOLUTION)
  },
  async [ActionNames.__AUTO_SELECT_SESSION]({ getters, dispatch }) {
    logger.action(ActionNames.__AUTO_SELECT_SESSION)
    const sessions = getters.sessionsForCurrentNetwork as Session[] | null
    if (sessions
        && sessions.length === 1) {
      await dispatch(ActionNames.SET_CURRENT_SESSION, sessions[0])
    }
  },
  async [ActionNames.__ON_CURRENT_SESSION_CHANGED]({ dispatch, commit, state }) {
    logger.action(ActionNames.__ON_CURRENT_SESSION_CHANGED)
    commit(MutationNames.SET_CURRENT_SESSION_DEMANDS, undefined)
    await dispatch(ActionNames.__RESET_CURRENT_PERIOD)
    await dispatch(ActionNames.__RESET_CURRENT_SOLUTION)
    await dispatch(ActionNames.__AUTO_SELECT_SOLUTION)
    await dispatch(ActionNames.__LOAD_CURRENT_SESSION_DEMANDS)
  },
  async [ActionNames.__LOAD_CURRENT_SESSION_DEMANDS]({ commit, state }) {
    logger.action(ActionNames.__LOAD_CURRENT_SESSION_DEMANDS)
    const { currentSession } = state
    if (!currentSession) {
      return
    }
    const demands = await client.getDemands(currentSession.id)
    commit(MutationNames.SET_CURRENT_SESSION_DEMANDS, demands)
  },
  async [ActionNames.START_SESSION]({ dispatch }, { id }) {
    logger.action(ActionNames.START_SESSION, id)
    await client.startSession(id)
    await dispatch(ActionNames.FETCH_SERVER_STATUS)
  },
  async [ActionNames.STOP_SESSION]({ dispatch, state }, { id }) {
    logger.action(ActionNames.STOP_SESSION, id)
    await client.stopSession(id)
    await dispatch(ActionNames.FETCH_SERVER_STATUS)
    if (state.currentSolutionId === bestSolutionId) {
      await dispatch(ActionNames.__LOAD_CURRENT_SOLUTION)
    }
  },
  async [ActionNames.DELETE_SESSION]({ dispatch }, { id }) {
    logger.action(ActionNames.DELETE_SESSION, id)
    try {
      await client.deleteSession(id)
    } catch (error) {
      await Modal.showError("Error while deleting the scenario", error.message)
      return
    }
    await dispatch(ActionNames.FETCH_SERVER_STATUS)
    await dispatch(ActionNames.SET_CURRENT_SESSION, undefined)
  },
  async [ActionNames.SET_CURRENT_SOLUTION_ID]({ commit, dispatch }, id) {
    logger.action(ActionNames.SET_CURRENT_SOLUTION_ID, id)
    commit(MutationNames.SET_CURRENT_SOLUTION_ID, id)
    await dispatch(ActionNames.__ON_CURRENT_SOLUTION_CHANGED)
  },
  async [ActionNames.CREATE_SOLUTION]({ dispatch }, { sessionId, solutionId, solutionFile }) {
    logger.action(ActionNames.CREATE_SOLUTION, { sessionId, solutionId, solutionFile })
    try {
      const solutionJson = await solutionFile.text()
      const solution = JSON.parse(solutionJson)
      await client.createSolution(sessionId, solutionId, solution)
      await dispatch(ActionNames.SET_CURRENT_SOLUTION_ID, solutionId)
    } catch (error) {
      await Modal.showError("Error while creating the solution", error.message)
      throw error
    }
    await dispatch(ActionNames.__ON_SOLUTION_CREATED)
  },
  async [ActionNames.__ON_SOLUTION_CREATED]({ dispatch }) {
    logger.action(ActionNames.__ON_SOLUTION_CREATED)
    dispatch(ActionNames.FETCH_SERVER_STATUS)
  },
  async [ActionNames.DELETE_SOLUTION]({ dispatch, state }, { id }) {
    logger.action(ActionNames.DELETE_SOLUTION)
    try {
      const sessionId = state.currentSession?.id as string
      await client.deleteSolution(sessionId, id)
    } catch (error) {
      await Modal.showError("Error while deleting the solution", error.message)
      return
    }
    await dispatch(ActionNames.SET_CURRENT_SOLUTION_ID, undefined)
    await dispatch(ActionNames.FETCH_SERVER_STATUS)
  },
  async [ActionNames.__RESET_CURRENT_SOLUTION]({ commit, dispatch }) {
    logger.action(ActionNames.__RESET_CURRENT_SOLUTION)
    commit(MutationNames.SET_CURRENT_SOLUTION_ID, undefined)
    commit(MutationNames.SET_CURRENT_SOLUTION, undefined)
    commit(MutationNames.SET_CURRENT_SOLUTION_INFO, undefined)
    await dispatch(ActionNames.__ON_CURRENT_SOLUTION_CHANGED)
  },
  async [ActionNames.__LOAD_CURRENT_SOLUTION]({ commit, state }) {
    logger.action(ActionNames.__LOAD_CURRENT_SOLUTION)
    if (!state.currentSession) {
      throw new Error('Attempted to load a solution, but no current session is set')
    }
    if (!state.currentSolutionId) {
      throw new Error('Attempted to load a solution, but no current solution id is set')
    }
    const sessionId = state.currentSession.id
    const solutionId = state.currentSolutionId
    const solutionRequest = client.getSolution(sessionId, solutionId)
    const solutionInfoRequest = client.getSolutionInfo(sessionId, solutionId)
    const [solution, solutionInfo] = await Promise.all([solutionRequest, solutionInfoRequest])
    commit(MutationNames.SET_CURRENT_SOLUTION, solution)
    commit(MutationNames.SET_CURRENT_SOLUTION_INFO, solutionInfo)
  },
  async [ActionNames.__ON_CURRENT_SOLUTION_CHANGED]({ dispatch, state }) {
    logger.action(ActionNames.__ON_CURRENT_SOLUTION_CHANGED)
    if (state.currentSolutionId) {
      await dispatch(ActionNames.__LOAD_CURRENT_SOLUTION)
    }
  },
  async [ActionNames.__AUTO_SELECT_SOLUTION]({ dispatch, state }) {
    logger.action(ActionNames.__AUTO_SELECT_SOLUTION)
    if (state.currentSession
        && state.currentSession.solutionIds.length === 1) {
      await dispatch(ActionNames.SET_CURRENT_SOLUTION_ID, state.currentSession.solutionIds[0])
    }
  },
  async [ActionNames.ON_NEW_SOLUTION_INFO]({ commit, dispatch, state }, solutionInfo) {
    logger.action(ActionNames.ON_NEW_SOLUTION_INFO, solutionInfo)
    commit(MutationNames.SET_CURRENT_SOLUTION_INFO, solutionInfo)
  },
  async [ActionNames.__RESET_CURRENT_PERIOD]({ commit }) {
    logger.action(ActionNames.__RESET_CURRENT_PERIOD)
    commit(MutationNames.SET_CURRENT_PERIOD, 0)
  },
  async [ActionNames.SHOW_MODAL]({ commit }, modalType) {
    logger.action(ActionNames.SHOW_MODAL, modalType)
    // Do nothing (the modals can listen for this action)
  },
  async [ActionNames.ADD_DEMO_DATA]({ dispatch, state }) {
    logger.action(ActionNames.ADD_DEMO_DATA)
    const demoNetworkName = "Demo network"
    const demoScenarioName = "Demo scenario"

    const demoNetwork = (await import("@/demoData/demoNetwork.json")).default
    const networkBlob = new Blob([JSON.stringify(demoNetwork)], { type: 'application/json' })
    const networkFile = new File([networkBlob], "demo_network.json")
    await dispatch(ActionNames.CREATE_NETWORK, { networkDescriptionFile: networkFile, name: demoNetworkName })

    const demoForecast = (await import("@/demoData/demoForecast.json")).default
    const forecastBlob = new Blob([JSON.stringify(demoForecast)], { type: 'application/json' })
    const forecastFile = new File([forecastBlob], "demo_forecast.json")
    await dispatch(ActionNames.CREATE_SESSION, {
      id: demoScenarioName,
      networkId: demoNetworkName,
      forecastFile: forecastFile,
    })
  },
  async [ActionNames.SET_GRAPH_RENDERING_STATE]({ commit }, value) {
    logger.action(ActionNames.SET_GRAPH_RENDERING_STATE, value)
    commit(MutationNames.SET_GRAPH_RENDERING_STATE, value)
  },
  async [ActionNames.SET_SWITCH_STATE]({ dispatch, commit, state }, { edgeId, isOpen }) {
    logger.action(ActionNames.SET_SWITCH_STATE, edgeId, isOpen)

    if (state.currentSolutionId === undefined) {
      throw new Error("currentSolutionId is undefined")
    }
    if (state.currentSolution === undefined) {
      throw new Error("currentSolution is undefined")
    }
    if (state.currentSession === undefined) {
      throw new Error("currentSession is undefined")
    }

    const switchSetting = state.currentSolution
      .period_solutions[state.currentPeriodIndex]
      .switch_settings
      .find(s => s.line_id === edgeId)!

    switchSetting.open = isOpen

    let solutionId = state.currentSolutionId
    if (solutionId === 'best') {
      // If we're on the 'best' solution, we will create a special solution to hold our changes
      solutionId = 'best (edited)'
      // But if it already exists, we can reuse it
      const solutionExists = state.currentSession.solutionIds.some(sId => sId === solutionId)
      if (solutionExists) {
        await client.updateSolution(state.currentSession.id, solutionId, state.currentSolution)
      } else {
        await client.createSolution(state.currentSession.id, solutionId, state.currentSolution)
        await dispatch(ActionNames.FETCH_SERVER_STATUS)
      }
    } else {
      await client.updateSolution(state.currentSession.id, solutionId, state.currentSolution)
      await dispatch(ActionNames.SET_CURRENT_SOLUTION_ID, state.currentSolutionId)
    }
    await dispatch(ActionNames.SET_CURRENT_SOLUTION_ID, solutionId)
  },
}
export default actions
