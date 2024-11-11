import { Node } from '@/pgoApi/entities/node'
import { Line } from '@/pgoApi/entities/Line'

export interface PowerGrid {
  name: string
  nodes: Node[]
  lines: Line[]
  transformers: Transformer[]
}
