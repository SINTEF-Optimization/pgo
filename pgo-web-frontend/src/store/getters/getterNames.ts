import { EdgeSolutionValues, NodeSolutionValues, State } from '@/store/state'
import { Session } from '@/pgoApi/entities/session'
import { PowerFlow, SinglePeriodSettings } from '@/pgoApi/entities/solution'
import { Getters } from '@/store/getters/getters'
import { Line } from '@/pgoApi/entities/Line'
import { Node } from '@/pgoApi/entities/node'
import { DimensionedNode } from '@/models/dimensionedNode'
import { DimensionedEdge } from '@/models/dimensionedEdge'
import { DimensionedEdgeSolutionValues } from '@/models/dimensionedEdgeSolutionValues'
import { DimensionedNodeSolutionValues } from '@/models/dimensionedNodeSolutionValues'

type GetterNames = {
  sessionsForCurrentNetwork(state: State): Session[] | null
  currentSolutionHasNextPeriod(state: State): boolean
  currentSolutionHasPreviousPeriod(state: State): boolean
  currentPeriodSolution(state: State): SinglePeriodSettings | null
  currentPeriodFlow(state: State): PowerFlow | null
  currentNode(state: State): Node | null
  currentNodeDimensioned(state: State, getters: Getters): DimensionedNode | null
  currentNodeSolutionValues(state: State, getters: Getters): NodeSolutionValues | null
  currentNodeSolutionValuesDimensioned(state: State, getters: Getters): DimensionedNodeSolutionValues | null
  currentEdge(state: State): Line | null
  currentEdgeDimensioned(state: State, getters: Getters): DimensionedEdge | null
  currentEdgeSolutionValues(state: State, getters: Getters): EdgeSolutionValues | null
  currentEdgeSolutionValuesDimensioned(state: State, getters: Getters): DimensionedEdgeSolutionValues | null
  currentEdgeSwitchOpen(state: State, getters: Getters): boolean | null
}

export default GetterNames
