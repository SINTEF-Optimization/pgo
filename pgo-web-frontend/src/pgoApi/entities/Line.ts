export interface Line {
  id: string
  source: string
  target: string
  r: number
  x: number
  imax: number | string
  vmax: number | string
  switchable: boolean
  switching_cost: number
  breaker: boolean
  fault_frequency: number
  sectioning_time: string // ISO duration string (not datetime!)
  repair_time: string // ISO duration string (not datetime!)
}
