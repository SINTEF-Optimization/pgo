/* eslint-disable @typescript-eslint/no-explicit-any */

export interface Logger {
  action(name: string, ...payload: any[]): void
  debug(label: string, ...payload: any[]): void
  http(directionLabel: string, method: string, endpoint: string, ...payload: any[]): void
  log(label: string, ...payload: any[]): void
}

export class ConsoleLogger implements Logger {
  normalStyle = ''
  unobtrusiveStyle = 'color: rgb(200,200,200); font-size: smaller;'

  action(name: string, ...payload: any): void {
    const text = `%cAction: ${name}`
    this.logCollapsed(text, [this.unobtrusiveStyle], payload)
  }

  debug(label: string, ...payload: any[]): void {
    const text = `%c${label}`
    this.logCollapsed(text, [this.unobtrusiveStyle], payload)
  }

  http(directionLabel: string, method: string, endpoint: string, ...payload: any): void {
    console.groupCollapsed(`%c${directionLabel}\t${method}\t${endpoint}`, this.unobtrusiveStyle)
    for (const nested of payload) {
      console.log(nested)
    }
    console.groupEnd()
  }

  private logCollapsed(label: string, styles: string[], payload: any[]): void {
    if (payload.length) {
      console.groupCollapsed(label, ...styles)
      for (const item of payload) {
        console.log(item)
      }
      console.groupEnd()
    } else {
      console.log(label, ...styles)
    }
  }

  log(label: string, ...payload: any[]): void {
    const text = `%c${label}`
    this.logCollapsed(text, [this.normalStyle], payload)
  }
}

/* eslint-disable @typescript-eslint/no-unused-vars */
/* eslint-disable @typescript-eslint/no-empty-function */
export class DummyLogger implements Logger {
  action(name: string, payload?: any): void {
  }

  debug(...args: any): void {
  }

  http(...args: any): void {
  }

  log(label: string, ...payload: any[]): void {
  }
}
