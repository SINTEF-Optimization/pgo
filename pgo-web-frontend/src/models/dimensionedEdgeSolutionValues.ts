import { EdgeSolutionValues } from '@/store/state'
import { DimensionedQuantity } from '@/models/dimensionedQuantity'

export class DimensionedEdgeSolutionValues {
  current: DimensionedQuantity
  activePower: DimensionedQuantity
  reactivePower: DimensionedQuantity
  thermalLoss: DimensionedQuantity

  constructor(edgeSolutionValues: EdgeSolutionValues) {
    this.current = new DimensionedQuantity(edgeSolutionValues.current, 'A', 1)
    this.activePower = new DimensionedQuantity(edgeSolutionValues.activePower, 'W', 1e6)
    this.thermalLoss = new DimensionedQuantity(edgeSolutionValues.thermalLoss, 'W', 1e6)
    this.reactivePower = new DimensionedQuantity(edgeSolutionValues.reactivePower, 'VAr', 1e6)
  }
}
