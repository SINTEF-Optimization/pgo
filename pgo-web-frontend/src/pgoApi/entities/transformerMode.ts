import { TransformerOperation } from '@/pgoApi/entities/transformerOperation'

export interface TransformerMode {
  source: string
  target: string
  operation: TransformerOperation
  ratio: number
  power_factor: number
  bidirectional: boolean
}
