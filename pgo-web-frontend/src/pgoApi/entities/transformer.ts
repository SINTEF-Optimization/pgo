import { TransformerMode } from '@/pgoApi/entities/transformerMode'
import { TransformerConnection } from '@/pgoApi/entities/transformerConnection'

export interface Transformer {
  connections: TransformerConnection[]
  modes: TransformerMode[]
  coordinates: [number, number]
}
