import { Line } from '@/pgoApi/entities/Line'
import { DimensionedQuantity } from '@/models/dimensionedQuantity'

export class DimensionedEdge {
  id: string
  source: string
  target: string
  r: DimensionedQuantity
  x: DimensionedQuantity
  imax: DimensionedQuantity
  vmax: DimensionedQuantity
  switchable: boolean
  switching_cost: number
  breaker: boolean
  fault_frequency: number
  sectioning_time: string // ISO duration string (not datetime!)
  repair_time: string // ISO duration string (not datetime!)

  constructor(edge: Line) {
    this.id = edge.id
    this.source = edge.source
    this.target = edge.target
    this.switchable = edge.switchable
    this.switching_cost = edge.switching_cost
    this.breaker = edge.breaker
    this.fault_frequency = edge.fault_frequency
    this.sectioning_time = edge.sectioning_time
    this.repair_time = edge.repair_time

    this.r = new DimensionedQuantity(edge.r, 'Ω', 1)
    this.x = new DimensionedQuantity(edge.x, 'Ω', 1)
    this.imax = new DimensionedQuantity(edge.imax, 'A', 1e3)
    this.vmax = new DimensionedQuantity(edge.vmax, 'V', 1e3)
  }
}
