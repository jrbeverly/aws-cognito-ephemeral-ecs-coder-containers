export type WorkspaceStatus = 'provisioning' | 'active' | 'failed' | 'deleting'

export interface WorkspaceResponse {
  workspaceId: string
  userEmail: string
  status: WorkspaceStatus
  workspaceUrl?: string
  albDnsName?: string
  ecsServiceName?: string
  stackName?: string
  createdAt: string
  updatedAt: string
}
