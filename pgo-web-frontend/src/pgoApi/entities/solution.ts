export interface Solution {
  period_solutions: SinglePeriodSettings[]
  flows: PowerFlow[]
}

export interface SinglePeriodSettings {
  switch_settings: SwitchState[]
  period: string
}

export interface PowerFlow {
  period_id: string
  status: FlowStatus
  status_details: string
  voltages: { [key: string]: number }
  currents: { [key: string]: OutgoingCurrent[] }
  injected_power: { [key: string]: OutgoingPower[] }
}

export interface SwitchState {
  line_id: string
  open: boolean
}

export enum FlowStatus {
  failed = 'failed',
  approximate = 'approximate',
  exact = 'exact'
}

export interface OutgoingCurrent {
  target: string
  line_id: string
  current: number
}

export interface OutgoingPower {
  target: string
  line_id: string
  active: number
  reactive: number
  thermal_loss: number
}
