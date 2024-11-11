import { DimensionedQuantity } from '@/models/dimensionedQuantity'
import { NodeSolutionValues } from '@/store/state'

export class DimensionedNodeSolutionValues {
    voltage: DimensionedQuantity
    outgoingCurrent: DimensionedQuantity
    outgoingActivePower: DimensionedQuantity
    outgoingReactivePower: DimensionedQuantity

    constructor(nodeSolutionValues: NodeSolutionValues) {
      this.voltage = new DimensionedQuantity(nodeSolutionValues.voltage, 'V', 1e3)
      this.outgoingCurrent = new DimensionedQuantity(
        nodeSolutionValues.outgoingCurrent,
        'A',
        1e3
      )
      this.outgoingActivePower = new DimensionedQuantity(
        nodeSolutionValues.outgoingActivePower,
        'W',
        1e6
      )
      this.outgoingReactivePower = new DimensionedQuantity(
        nodeSolutionValues.outgoingReactivePower,
        'W',
        1e6
      )
    }
}
