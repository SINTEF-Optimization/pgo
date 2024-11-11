export interface Session {
  id: string
  networkId: string
  optimizationIsRunning: boolean
  bestSolutionValue: number
  bestInfeasibleSolutionValue: number
  solutionIds: string[]
}
