import HttpClient from '../httpClient'
import { PowerGrid } from '@/pgoApi/entities/powerGrid'
import { CreateNetworkRequest } from '@/pgoApi/requests/createNetworkRequest'
import { AxiosRequestConfig } from 'axios'
import { ServerStatus } from '@/pgoApi/entities/serverStatus'
import { Session } from '@/pgoApi/entities/session'
import { CreateSessionRequest } from '@/pgoApi/requests/createSessionRequest'
import { Solution } from '@/pgoApi/entities/solution'
import { SolutionInfo } from '@/pgoApi/entities/solutionInfo'
import { Demand } from '@/pgoApi/entities/demand'

export default class PgoApi extends HttpClient {
  public constructor() {
    super('')
    this.initializeResponseInterceptors()
  }

  private initializeResponseInterceptors() {
    this.client.interceptors.response.use(undefined, this.handleError)
  }

  private handleError(error: any) {
    if (error.response) {
      // The request was made and the server responded with a status code
      // that falls out of the range of 2xx
      if (error.response.status === 400
          || error.response.status === 404
          || error.response.status === 403
      ) {
        throw new Error(error.response.data)
      } else if (error.response.status === 401) {
        // The request was not authenticated.
        // Redirect to the sign-in page
        window.location.assign("/.auth/sign-in")
      } else if (error.response.status === 500) {
        throw new Error("A server error occurred")
      }
    }
    // Something else went wrong
    throw new Error("An error occurred while communicating with the server")
  }

  public async getNetwork(id: string): Promise<PowerGrid> {
    const response = await this.client.get<PowerGrid>(`/api/networks/${id}`)
    return response.data
  }

  public async createNetwork({ id, networkDescriptionFile }: CreateNetworkRequest): Promise<void> {
    const formData = new FormData()
    formData.append('networkDescription', networkDescriptionFile)
    const config: AxiosRequestConfig = {
      headers: {
        'Content-Type': 'multipart/form-data',
      },
    }
    await this.client.post(`/api/networks/${id}`, formData, config)
  };

  public async deleteNetwork(id: string): Promise<void> {
    await this.client.delete(`/api/networks/${id}`)
  };

  public async getNetworkAnalysis(id: string): Promise<string> {
    const response = await this.client.get<string>(`/api/networks/${id}/analysis`)
    return response.data
  }

  public async getServerStatus(): Promise<ServerStatus> {
    const response = await this.client.get<ServerStatus>('/api/server')
    return response.data
  }

  public async getSession(id: string): Promise<Session> {
    const response = await this.client.get<Session>(`/api/sessions/${id}`)
    return response.data
  }

  public async createSession(args: CreateSessionRequest): Promise<void> {
    const formData = new FormData()
    formData.append('networkId', args.networkId)
    formData.append('demands', args.forecastFile)
    formData.append('startConfiguration', args.startConfigurationFile)
    const config: AxiosRequestConfig = {
      headers: {
        'Content-Type': 'multipart/form-data',
      },
    }
    await this.client.post(`/api/sessions/${args.id}`, formData, config)
  }

  public async startSession(id: string): Promise<void> {
    const config: AxiosRequestConfig = {
      headers: {
        'Content-Type': 'application/json',
      },
    }
    await this.client.put(`/api/sessions/${id}/runOptimization`, JSON.stringify(true), config)
  }

  public async stopSession(id: string): Promise<void> {
    const config: AxiosRequestConfig = {
      headers: {
        'Content-Type': 'application/json',
      },
    }
    await this.client.put(`/api/sessions/${id}/runOptimization`, JSON.stringify(false), config)
  }

  public async deleteSession(id: string): Promise<void> {
    await this.client.delete(`/api/sessions/${id}`)
  }

  public async getSolution(sessionId: string, solutionId: string): Promise<Solution> {
    const response = await this.client.get(`/api/sessions/${sessionId}/solutions/${solutionId}`)
    return response.data
  }

  public async createSolution(sessionId: string, solutionId: string, solution: Solution): Promise<void> {
    await this.client.post(`/api/sessions/${sessionId}/solutions/${solutionId}`, solution)
  }

  public async updateSolution(sessionId: string, solutionId: string, solution: Solution): Promise<void> {
    await this.client.put(`/api/sessions/${sessionId}/solutions/${solutionId}`, solution)
  }

  public async deleteSolution(sessionId: string, solutionId: string): Promise<void> {
    await this.client.delete(`/api/sessions/${sessionId}/solutions/${solutionId}`)
  }

  public async getSolutionInfo(sessionId: string, solutionId: string): Promise<SolutionInfo> {
    const response = await this.client.get(`/api/sessions/${sessionId}/solutions/${solutionId}/info`)
    return response.data
  }

  public async getDemands(sessionId: string): Promise<Demand> {
    const response = await this.client.get(`/api/sessions/${sessionId}/demands`)
    return response.data
  }
}
