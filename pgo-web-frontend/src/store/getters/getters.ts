import GetterNames from '@/store/getters/getterNames'
import { EdgeSolutionValues, GraphElementType, NodeSolutionValues, State } from '@/store/state'
import { GetterTree } from 'vuex'
import { PowerFlow, SinglePeriodSettings } from '@/pgoApi/entities/solution'
import { Session } from '@/pgoApi/entities/session'
import { Line } from '@/pgoApi/entities/Line'
import { Node } from '@/pgoApi/entities/node'
import { DimensionedNode } from '@/models/dimensionedNode'
import { DimensionedEdge } from '@/models/dimensionedEdge'
import { DimensionedNodeSolutionValues } from '@/models/dimensionedNodeSolutionValues'
import { DimensionedEdgeSolutionValues } from '@/models/dimensionedEdgeSolutionValues'

export type Getters = GetterTree<State, State> & GetterNames

const getters: Getters = {
  sessionsForCurrentNetwork: (state): Session[] | null => {
    if (!state.networks
        || !state.currentNetworkId
        || !(state.currentNetworkId in state.networks)) {
      return null
    }
    return state.networks[state.currentNetworkId as string].sessions
  },
  currentSolutionHasNextPeriod(state: State): boolean {
    if (state.currentSolution
        && state.currentSolution.period_solutions.length > state.currentPeriodIndex + 1) {
      return true
    } else return false
  },
  currentSolutionHasPreviousPeriod(state: State): boolean {
    if (state.currentSolution
        && state.currentPeriodIndex > 0) {
      return true
    } else return false
  },
  currentPeriodFlow(state: State): PowerFlow | null {
    if (state.currentSolution
        && state.currentSolution.period_solutions.length) {
      return state.currentSolution.flows[state.currentPeriodIndex]
    } else return null
  },
  currentPeriodSolution(state: State): SinglePeriodSettings | null {
    if (state.currentSolution
        && state.currentSolution.period_solutions.length) {
      return state.currentSolution.period_solutions[state.currentPeriodIndex]
    } else {
      return null
    }
  },
  currentNode(state: State): Node | null {
    if (!state.currentNetwork
        || state.currentGraphElement?.type !== GraphElementType.node
    ) {
      return null
    }
    const currentNodeId = state.currentGraphElement.id as string
    const node = state.currentNetwork.nodes.find(node => node.id === currentNodeId)
    if (!node) {
      return null
    }
    return node as unknown as Node
  },
  currentNodeDimensioned(state: State, getters: Getters): DimensionedNode | null {
    const currentNode = getters.currentNode
    if (!currentNode) {
      return null
    }
    return new DimensionedNode(currentNode as unknown as Node)
  },
  currentNodeSolutionValues(state: State, getters: Getters): NodeSolutionValues | null {
    if (!state.currentGraphElement
        || state.currentGraphElement.type !== GraphElementType.node
    ) {
      return null
    }
    const currentNodeId = state.currentGraphElement.id

    const currentPeriodFlow = getters.currentPeriodFlow
    if (!currentPeriodFlow) {
      return null
    }
    const flow = currentPeriodFlow as unknown as PowerFlow // TS thinks the value is a function due to getter magic
    const result = {} as NodeSolutionValues
    result.outgoingCurrents = flow.currents[currentNodeId]
    result.outgoingPowerValues = flow.injected_power[currentNodeId]
    result.voltage = flow.voltages[currentNodeId]
    result.outgoingCurrent = result.outgoingCurrents
      ?.map(oc => oc.current)
      ?.reduce((accumulator, next) => accumulator + next)
      ?? null
    result.outgoingActivePower = result.outgoingPowerValues
      ?.map(op => op.active)
      ?.reduce((accumulator, next) => accumulator + next)
      ?? null
    result.outgoingReactivePower = result.outgoingPowerValues
      ?.map(op => op.reactive)
      ?.reduce((accumulator, next) => accumulator + next)
      ?? null
    return result
  },
  currentNodeSolutionValuesDimensioned(state: State, getters: Getters): DimensionedNodeSolutionValues | null {
    const nodeSolutionValues = getters.currentNodeSolutionValues
    if (!nodeSolutionValues) {
      return null
    }
    return new DimensionedNodeSolutionValues(nodeSolutionValues as unknown as NodeSolutionValues)
  },
  currentEdge(state: State): Line | null {
    if (!state.currentNetwork
      || state.currentGraphElement?.type !== GraphElementType.edge
    ) {
      return null
    }
    const currentEdgeId = state.currentGraphElement.id as string
    const edge = state.currentNetwork.lines.find(edge => edge.id === currentEdgeId)
    if (!edge) {
      return null
    }
    return edge as unknown as Line
  },
  currentEdgeDimensioned(state: State, getters: Getters): DimensionedEdge | null {
    const currentEdge = getters.currentEdge
    if (!currentEdge) {
      return null
    }
    return new DimensionedEdge(currentEdge as unknown as Line)
  },
  currentEdgeSolutionValues(state: State, getters: Getters): EdgeSolutionValues | null {
    if (!state.currentGraphElement
      || !(state.currentGraphElement.type === GraphElementType.edge)) {
      return null
    }
    const currentEdgeId = state.currentGraphElement.id as string

    const currentPeriodFlow = getters.currentPeriodFlow
    if (!currentPeriodFlow) {
      return null
    }
    const flow = currentPeriodFlow as unknown as PowerFlow // TS thinks the value is a function due to getter magic
    const result = {} as EdgeSolutionValues
    result.current = Object.values(flow.currents)
      .flat()
      .find(c => c.line_id === currentEdgeId)
      ?.current ?? 0

    const powerValues = Object.values(flow.injected_power)
      .flat()
      .find(p => p.line_id === currentEdgeId)

    result.activePower = powerValues?.active ?? 0
    result.reactivePower = powerValues?.reactive ?? 0
    result.thermalLoss = powerValues?.thermal_loss ?? 0

    return result
  },
  currentEdgeSwitchOpen(state: State, getters: Getters): boolean | null {
    if (!state.currentGraphElement
      || !(state.currentGraphElement.type === GraphElementType.edge)
    ) {
      return null
    }

    const currentPeriodSolution = getters.currentPeriodSolution
    if (!currentPeriodSolution) {
      return null
    }
    const currentEdgeId = state.currentGraphElement.id as string

    const solution = currentPeriodSolution as unknown as SinglePeriodSettings
    return solution.switch_settings
      .find(ss => ss.line_id === currentEdgeId)
      ?.open
      ?? null
  },
  currentEdgeSolutionValuesDimensioned(state: State, getters: Getters): DimensionedEdgeSolutionValues | null {
    const edgeSolutionValues = getters.currentEdgeSolutionValues
    if (!edgeSolutionValues) {
      return null
    }
    return new DimensionedEdgeSolutionValues(edgeSolutionValues as unknown as EdgeSolutionValues)
  },
}

export default getters
