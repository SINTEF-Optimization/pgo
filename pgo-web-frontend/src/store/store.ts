import { CommitOptions, DispatchOptions, Store as VuexStore } from 'vuex'
import { MutationSignatures } from '@/store/mutations/mutationSignatures'
import { ActionSignatures } from '@/store/actions/actionSignatures'
import { State } from '@/store/state'
import GetterNames from '@/store/getters/getterNames'

export type Store = Omit<
  VuexStore<State>,
  'getters' | 'commit' | 'dispatch'
  > & {
  commit<K extends keyof MutationSignatures, P extends Parameters<MutationSignatures[K]>[1]>(
    key: K,
    payload: P,
    options?: CommitOptions
  ): ReturnType<MutationSignatures[K]>
} & {
  dispatch<K extends keyof ActionSignatures>(
    key: K,
    payload: Parameters<ActionSignatures[K]>[1],
    options?: DispatchOptions
  ): ReturnType<ActionSignatures[K]>
} & {
  getters: {
    [K in keyof GetterNames]: ReturnType<GetterNames[K]>;
  }
}
