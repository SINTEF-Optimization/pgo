import { Period } from '@/pgoApi/entities/period'

export interface Demand {
  periods: Period[]
  loads: LoadSeries[]
}

export interface LoadSeries {
  node_id: string
  p_loads: number[]
  q_loads: number[]
}
