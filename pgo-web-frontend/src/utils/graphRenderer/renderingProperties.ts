import { Node, NodeType } from '@/pgoApi/entities/node'
import { Line } from '@/pgoApi/entities/Line'

export interface NodeRenderingPropertySet {
  color: string
  size: number
}

const nodeRenderingProperties = {
  [NodeType.consumer]: {
    color: '#4d864d',
    size: 2,
  },
  [NodeType.provider]: {
    color: '#ac3838',
    size: 4,
  },
  [NodeType.transition]: {
    color: '#43857d',
    size: 1,
  },
}

export function getNodeRenderingProperties(node: Node): NodeRenderingPropertySet {
  return nodeRenderingProperties[node.type]
}

export interface EdgeRenderingPropertySet {
  color: string
  size: number
  type: string
}

enum EdgeType {
  breaker = 'breaker',
  switchOpen = 'switchOpen',
  switchClosed = 'switchClosed',
  switchIndeterminate = 'switchIndeterminate',
  default = 'default',
}

export const edgeRenderingProperties = {
  [EdgeType.breaker]: {
    color: '#301f1f',
    size: 3,
    type: 'line',
  },
  [EdgeType.switchOpen]: {
    color: '#943c3c',
    size: 3,
    type: 'dashed',
  },
  [EdgeType.switchClosed]: {
    color: '#459245',
    size: 3,
    type: 'line',
  },
  [EdgeType.switchIndeterminate]: {
    color: '#5a5a5a',
    size: 3,
    type: 'line',
  },
  [EdgeType.default]: {
    color: '#6b6b6b',
    size: 1,
    type: 'line',
  },
}

export function getEdgeRenderingProperties(edge: Line): EdgeRenderingPropertySet {
  if (edge.breaker) {
    return edgeRenderingProperties[EdgeType.breaker]
  } else if (edge.switchable) {
    return edgeRenderingProperties[EdgeType.switchIndeterminate]
  } else {
    return edgeRenderingProperties[EdgeType.default]
  }
}
