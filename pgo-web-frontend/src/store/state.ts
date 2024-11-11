import { PowerGrid } from '@/pgoApi/entities/powerGrid'
import { ServerStatus } from '@/pgoApi/entities/serverStatus'
import { Session } from '@/pgoApi/entities/session'
import { OutgoingCurrent, OutgoingPower, Solution } from '@/pgoApi/entities/solution'
import { SolutionInfo } from '@/pgoApi/entities/solutionInfo'
import { Demand } from '@/pgoApi/entities/demand'

export const state = {
  networks: undefined,
  currentNetworkId: undefined,
  currentNetwork: undefined,
  currentSession: undefined,
  currentSessionDemands: undefined,
  currentSolutionId: undefined,
  currentSolution: undefined,
  currentSolutionInfo: undefined,
  currentPeriodIndex: 0,
  currentGraphElement: undefined,
  serverStatus: undefined,
  graphIsRendering: false,
}

export interface State {
  networks: NetworkDict | undefined
  currentNetworkId: string | undefined
  currentNetwork: PowerGrid | undefined
  currentSession: Session | undefined
  currentSessionDemands: Demand | undefined
  currentSolutionId: string | undefined
  currentSolution: Solution | undefined
  currentSolutionInfo: SolutionInfo | undefined
  currentPeriodIndex: number
  currentGraphElement: CurrentGraphElement | undefined
  serverStatus: ServerStatus | undefined
  graphIsRendering: boolean
}

export interface NetworkDict {
  [id: string]: NetworkCollectionItem
}

export interface NetworkCollectionItem {
  id: string
  name: string | undefined
  sessions: Session[]
}

// Gather all the solution values pertaining to a single node in one place
export interface NodeSolutionValues {
  voltage: number
  outgoingCurrent: number | null
  outgoingActivePower: number | null
  outgoingReactivePower: number | null
  outgoingCurrents: OutgoingCurrent[]
  outgoingPowerValues: OutgoingPower[]
}

// Gather all the solution values pertaining to a single edge in one place
export interface EdgeSolutionValues {
  current: number
  activePower: number
  reactivePower: number
  thermalLoss: number
}

// An abstraction for any element that the user selects in the graph pane
export interface CurrentGraphElement {
  type: GraphElementType
  id: string
}

export enum GraphElementType {
  edge = "edge",
  node = "node",
}

// The demand values for a single node
export interface NodeDemand {
  p_load: number
  q_load: number
}
