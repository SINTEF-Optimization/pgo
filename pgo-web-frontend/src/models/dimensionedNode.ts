import { ConsumerCategory, ConsumerTypeFraction } from '@/pgoApi/entities/ConsumerTypeFraction'
import { Node, NodeType } from '@/pgoApi/entities/node'
import { DimensionedQuantity } from '@/models/dimensionedQuantity'

export class DimensionedNode {
  id: string
  type: NodeType
  consumer_type: ConsumerCategory
  consumer_type_fractions: ConsumerTypeFraction
  v_min: DimensionedQuantity
  v_max: DimensionedQuantity
  v_gen: DimensionedQuantity
  p_gen_max: DimensionedQuantity
  p_gen_min: DimensionedQuantity
  q_gen_max: DimensionedQuantity
  q_gen_min: DimensionedQuantity
  coordinates: [number, number]

  constructor(node: Node) {
    this.id = node.id
    this.type = node.type
    this.consumer_type = node.consumer_type
    this.consumer_type_fractions = node.consumer_type_fractions
    this.coordinates = node.coordinates

    this.v_min = new DimensionedQuantity(node.v_min, 'V', 1e3)
    this.v_max = new DimensionedQuantity(node.v_max, 'V', 1e3)
    this.v_gen = new DimensionedQuantity(node.v_gen, 'V', 1e3)
    this.p_gen_max = new DimensionedQuantity(node.p_gen_max, 'W', 1e6)
    this.p_gen_min = new DimensionedQuantity(node.p_gen_min, 'W', 1e6)
    this.q_gen_max = new DimensionedQuantity(node.q_gen_max, 'W', 1e6)
    this.q_gen_min = new DimensionedQuantity(node.q_gen_min, 'W', 1e6)
  }
}
