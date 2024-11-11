import { ConsumerCategory, ConsumerTypeFraction } from '@/pgoApi/entities/ConsumerTypeFraction'

export interface Node {
  id: string
  type: NodeType
  consumer_type: ConsumerCategory
  consumer_type_fractions: ConsumerTypeFraction
  v_min: number
  v_max: number
  v_gen: number
  p_gen_max: number
  p_gen_min: number
  q_gen_max: number
  q_gen_min: number
  coordinates: [number, number]
}

export enum NodeType {
  transition = 'transition',
  provider = 'provider',
  consumer = 'consumer',
}
