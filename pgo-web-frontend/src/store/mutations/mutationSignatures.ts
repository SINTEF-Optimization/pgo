import MutationNames from '@/store/mutations/mutationNames'
import { CurrentGraphElement, NetworkDict, State } from '@/store/state'
import { PowerGrid } from '@/pgoApi/entities/powerGrid'
import { ServerStatus } from '@/pgoApi/entities/serverStatus'
import { Session } from '@/pgoApi/entities/session'
import { Solution } from '@/pgoApi/entities/solution'
import { SolutionInfo } from '@/pgoApi/entities/solutionInfo'
import { Demand } from '@/pgoApi/entities/demand'

export type MutationSignatures
  <S = State> = {
  [MutationNames.SET_NETWORKS](state: S, payload: NetworkDict): void
  [MutationNames.SET_CURRENT_NETWORK](state: S, payload: PowerGrid | undefined): void
  [MutationNames.SET_CURRENT_NETWORK_ID](state: S, payload: string | undefined): void
  [MutationNames.SET_CURRENT_SESSION](state: S, payload: Session | undefined): void
  [MutationNames.SET_CURRENT_SESSION_DEMANDS](state: S, payload: Demand | undefined): void
  [MutationNames.SET_CURRENT_SOLUTION_ID](state: S, payload: string | undefined): void
  [MutationNames.SET_CURRENT_SOLUTION](state: S, payload: Solution | undefined): void
  [MutationNames.SET_CURRENT_SOLUTION_INFO](state: S, payload: SolutionInfo | undefined): void
  [MutationNames.SET_CURRENT_PERIOD](state: S, payload: number): void
  [MutationNames.SET_CURRENT_GRAPH_ELEMENT](state: S, payload: CurrentGraphElement | undefined): void
  [MutationNames.SET_SERVER_STATUS](state: S, payload: ServerStatus): void
  [MutationNames.SET_GRAPH_RENDERING_STATE](state: S, payload: boolean): void
}
