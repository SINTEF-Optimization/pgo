import { Session } from '@/pgoApi/entities/session'

export interface ServerStatus {
  networks: string[]
  sessions: Session[]
  licenceExpiryDate: string
}
