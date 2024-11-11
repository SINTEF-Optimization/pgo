import { DimensionedQuantity } from '@/models/dimensionedQuantity'
import { NodeDemand } from '@/store/state'

export class DimensionedNodeDemand {
  p_load: DimensionedQuantity
  q_load: DimensionedQuantity
  constructor(nodeDemand: NodeDemand) {
    this.p_load = new DimensionedQuantity(nodeDemand.p_load, 'W', 1e6)
    this.q_load = new DimensionedQuantity(nodeDemand.q_load, 'W', 1e6)
  }
}
