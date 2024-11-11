import axios, {
  AxiosInstance, AxiosRequestConfig, AxiosResponse,
} from 'axios'
import { logger } from '@/main'

abstract class HttpClient {
  protected readonly client: AxiosInstance;

  protected constructor(baseURL: string) {
    this.client = axios.create({
      baseURL,
    })

    this.initializeRequestInterceptor()
    this.initializeResponseInterceptor()
  }

  private initializeResponseInterceptor = () => {
    this.client.interceptors.response.use(
      this.echoIncomingResponseMiddleware,
      this.echoErrorResponseMiddleware
    )
  };

  private initializeRequestInterceptor = () => {
    this.client.interceptors.request.use(
      this.echoOutgoingRequestMiddleware
    )
  };

  protected echoOutgoingRequestMiddleware = async (requestConfig: AxiosRequestConfig) => {
    logger.http('-----> Request', requestConfig.method as string, requestConfig.url as string, requestConfig)
    return requestConfig
  }

  protected echoIncomingResponseMiddleware = async (response: AxiosResponse) => {
    logger.http('<----- Response', response.config.method as string, response.config.url as string, response)
    return response
  }

  protected echoErrorResponseMiddleware(error: any) {
    if (error.response) {
      // The request was made and the server responded with a status code
      // that falls out of the range of 2xx
      logger.http(
        '!----- Error response',
        error.response.config.method as string,
        error.response.config.url as string,
        error.response)
    }
    throw error
  }
}

export default HttpClient
