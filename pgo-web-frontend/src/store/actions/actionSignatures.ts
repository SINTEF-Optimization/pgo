import ActionNames from '@/store/actions/actionNames'
import { AugmentedActionContext } from '@/store/actions/augmentedActionContext'
import { CreateSessionRequest } from '@/pgoApi/requests/createSessionRequest'
import { Session } from '@/pgoApi/entities/session'
import { ModalType } from '@/utils/modal/modal'
import { SolutionInfo } from '@/pgoApi/entities/solutionInfo'

export interface ActionSignatures {
  [ActionNames.CREATE_NETWORK](
    { commit }: AugmentedActionContext,
    payload: { networkDescriptionFile: File, name: string }
  ): Promise<void>

  [ActionNames.DELETE_NETWORK](
    { commit }: AugmentedActionContext,
    payload: { id: string }
  ): Promise<void>

  [ActionNames.__ON_NETWORK_CREATED](
    { commit }: AugmentedActionContext,
    payload: { id: string }
  ): Promise<void>

  [ActionNames.__ON_CURRENT_NETWORK_ID_CHANGED](
    { commit }: AugmentedActionContext,
    payload: undefined
  ): Promise<void>

  [ActionNames.__LOAD_CURRENT_SESSION_DEMANDS](
    { commit }: AugmentedActionContext,
    payload: undefined
  ): Promise<void>

  [ActionNames.__AUTO_SELECT_NETWORK](
    { commit }: AugmentedActionContext,
    payload: undefined
  ): Promise<void>

  [ActionNames.__RESET_CURRENT_NETWORK](
    { commit }: AugmentedActionContext,
    payload: undefined
  ): Promise<void>

  [ActionNames.__ON_CURRENT_SESSION_CHANGED](
    { commit }: AugmentedActionContext,
    payload: undefined
  ): Promise<void>

  [ActionNames.SET_CURRENT_NETWORK_ID](
    { commit }: AugmentedActionContext,
    payload: string
  ): Promise<void>

  [ActionNames.SET_CURRENT_SESSION](
    { commit }: AugmentedActionContext,
    payload: Session
  ): Promise<void>

  [ActionNames.__ON_SESSION_CREATED](
    { commit }: AugmentedActionContext,
    payload: { id: string }
  ): Promise<void>

  [ActionNames.__LOAD_CURRENT_NETWORK](
    { commit }: AugmentedActionContext
  ): Promise<void>

  [ActionNames.FETCH_SERVER_STATUS](
    { commit }: AugmentedActionContext,
    payload: undefined
  ): Promise<void>

  [ActionNames.__ON_SERVER_STATUS_FETCHED](
    { commit }: AugmentedActionContext,
    payload: undefined
  ): Promise<void>

  [ActionNames.__UPDATE_NETWORK_LIST_FROM_SERVER_STATUS](
    { commit }: AugmentedActionContext,
    payload: undefined
  ): Promise<void>

  [ActionNames.__UPDATE_CURRENT_SESSION_FROM_SERVER_STATUS](
    { commit }: AugmentedActionContext,
    payload: undefined
  ): Promise<void>

  [ActionNames.CREATE_SESSION](
    { commit }: AugmentedActionContext,
    payload: CreateSessionRequest
  ): Promise<void>

  [ActionNames.START_SESSION](
    { commit }: AugmentedActionContext,
    payload: { id: string}
  ): Promise<void>

  [ActionNames.STOP_SESSION](
    { commit }: AugmentedActionContext,
    payload: { id: string}
  ): Promise<void>

  [ActionNames.DELETE_SESSION](
    { commit }: AugmentedActionContext,
    payload: { id: string}
  ): Promise<void>

  [ActionNames.__RESET_CURRENT_SESSION](
    { commit }: AugmentedActionContext,
    payload: undefined
  ): Promise<void>

  [ActionNames.__AUTO_SELECT_SESSION](
    { commit }: AugmentedActionContext,
    payload: undefined
  ): Promise<void>

  [ActionNames.SET_CURRENT_SOLUTION_ID](
    { commit }: AugmentedActionContext,
    payload: string
  ): Promise<void>

  [ActionNames.CREATE_SOLUTION](
    { commit }: AugmentedActionContext,
    payload: { sessionId: string, solutionId: string, solutionFile: File }
  ): Promise<void>

  [ActionNames.__ON_SOLUTION_CREATED](
    { commit }: AugmentedActionContext,
    payload: undefined
  ): Promise<void>

  [ActionNames.DELETE_SOLUTION](
    { commit }: AugmentedActionContext,
    payload: { id: string }
  ): Promise<void>

  [ActionNames.__LOAD_CURRENT_SOLUTION](
    { commit }: AugmentedActionContext,
    payload: { id: string }
  ): Promise<void>

  [ActionNames.__RESET_CURRENT_SOLUTION](
    { commit }: AugmentedActionContext,
    payload: undefined
  ): Promise<void>

  [ActionNames.__ON_CURRENT_SOLUTION_CHANGED](
    { commit }: AugmentedActionContext,
    payload: undefined
  ): Promise<void>

  [ActionNames.__AUTO_SELECT_SOLUTION](
    { commit }: AugmentedActionContext,
    payload: undefined
  ): Promise<void>

  [ActionNames.ON_NEW_SOLUTION_INFO](
    { commit }: AugmentedActionContext,
    payload: SolutionInfo
  ): Promise<void>

  [ActionNames.__RESET_CURRENT_PERIOD](
    { commit }: AugmentedActionContext,
    payload: undefined
  ): Promise<void>

  [ActionNames.SHOW_MODAL](
    { commit }: AugmentedActionContext,
    payload: ModalType
  ): Promise<void>

  [ActionNames.ADD_DEMO_DATA](
    { commit }: AugmentedActionContext,
    payload: undefined
  ): Promise<void>

  [ActionNames.SET_GRAPH_RENDERING_STATE](
    { commit }: AugmentedActionContext,
    payload: boolean
  ): Promise<void>
  [ActionNames.SET_SWITCH_STATE](
    { commit }: AugmentedActionContext,
    payload: { edgeId: string, isOpen: boolean }
  ): Promise<void>
}
