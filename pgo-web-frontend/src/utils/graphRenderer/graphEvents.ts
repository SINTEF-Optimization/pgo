import { EventEmitter } from 'events'

enum EventNames {
  clickNode = "clickNode",
  startHoverNode = "startHoverNode",
  endHoverNode = "endHoverNode",
  clickBackground = "clickBackground",
  clickEdge = "clickEdge",
  startHoverEdge = "startHoverEdge",
  endHoverEdge = "endHoverEdge",
}

export interface ClickNodeEvent {
  id: string
  x: number
  y: number
  clientX: number
  clientY: number
}

export interface ClickBackgroundEvent {
  x: number
  y: number
  clientX: number
  clientY: number
}

export interface HoverNodeEvent {
  id: string
  x: number
  y: number
  clientX: number
  clientY: number
}

export interface ClickEdgeEvent {
  id: string
  source: string
  target: string
  x: number
  y: number
  clientX: number
  clientY: number
  ctrlKeyPressed: boolean
}

export interface HoverEdgeEvent {
  id: string
  source: string
  target: string
  x: number
  y: number
  clientX: number
  clientY: number
}

export class GraphEventEmitter {
  private _emitter = new EventEmitter()

  // Add a listener
  public onClickNode(callback: (event: ClickNodeEvent) => void) {
    this._emitter.addListener(EventNames.clickNode, callback)
  }

  // Invoke the event
  public clickNode(event: ClickNodeEvent) {
    this._emitter.emit(EventNames.clickNode, event)
  }

  public onClickBackground(callback: (event: ClickBackgroundEvent) => void) {
    this._emitter.addListener(EventNames.clickBackground, callback)
  }

  public clickBackground(event: ClickBackgroundEvent) {
    this._emitter.emit(EventNames.clickBackground, event)
  }

  public onStartHoverNode(callback: (event: HoverNodeEvent) => void) {
    this._emitter.addListener(EventNames.startHoverNode, callback)
  }

  public startHoverNode(event: HoverNodeEvent) {
    this._emitter.emit(EventNames.startHoverNode, event)
  }

  public onEndHoverNode(callback: (event: HoverNodeEvent) => void) {
    this._emitter.addListener(EventNames.endHoverNode, callback)
  }

  public endHoverNode(event: HoverNodeEvent) {
    this._emitter.emit(EventNames.endHoverNode, event)
  }

  public onClickEdge(callback: (event: ClickEdgeEvent) => void) {
    this._emitter.addListener(EventNames.clickEdge, callback)
  }

  public clickEdge(event: ClickEdgeEvent) {
    this._emitter.emit(EventNames.clickEdge, event)
  }

  public onStartHoverEdge(callback: (event: HoverEdgeEvent) => void) {
    this._emitter.addListener(EventNames.startHoverNode, callback)
  }

  public startHoverEdge(event: HoverEdgeEvent) {
    this._emitter.emit(EventNames.startHoverNode, event)
  }

  public onEndHoverEdge(callback: (event: HoverEdgeEvent) => void) {
    this._emitter.addListener(EventNames.endHoverEdge, callback)
  }

  public endHoverEdge(event: HoverEdgeEvent) {
    this._emitter.emit(EventNames.endHoverEdge, event)
  }
}
