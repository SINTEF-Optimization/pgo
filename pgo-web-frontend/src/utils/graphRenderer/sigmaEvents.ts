// Sigma v2 events. At the time of writing, there is no documentation for this, so these are found from examples and by
// inspecting the source. Update as you go
export enum SigmaEvent {
  clickNode = "clickNode",
  rightClickNode = "rightClickNode",
  downStage = "downStage",
  clickStage = "clickStage",
  rightClickStage = "rightClickStage",
  enterNode = "enterNode",
  leaveNode = "leaveNode",

  // Fires when the instance is killed.
  kill = 'kill',
}
