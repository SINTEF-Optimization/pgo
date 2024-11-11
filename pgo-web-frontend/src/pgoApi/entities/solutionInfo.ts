import { Period } from '@/pgoApi/entities/period'

export interface SolutionInfo {
  is_feasible: boolean
  is_optimal: boolean
  objective_value: number
  objective_components: ObjectiveComponentWithWeight[]
  period_information: PeriodInfo[]
  violations: ConstraintViolation[]
}

export interface PeriodInfo {
  period: Period
  changed_switches: number
}

export interface ObjectiveComponentWithWeight {
  name: string
  value: number
  weight: number
}

export interface ConstraintViolation {
  name: string
  description: string
}
