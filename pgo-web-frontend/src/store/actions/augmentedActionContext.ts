import { MutationSignatures } from '@/store/mutations/mutationSignatures'
import { ActionContext } from 'vuex'
import { State } from '@/store/state'

export type AugmentedActionContext = {
  commit<K extends keyof MutationSignatures>(
    key: K, payload: Parameters<MutationSignatures[K]>[1]): ReturnType<MutationSignatures[K]>
} & Omit<ActionContext<State, State>, 'commit'>;
