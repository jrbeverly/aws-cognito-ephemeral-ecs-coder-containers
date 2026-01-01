import { useQuery, useMutation, useQueryClient } from '@tanstack/vue-query'
import { workspaceService } from '@/services/workspaceService'
import type { WorkspaceResponse } from '@/types/workspace'

export function useWorkspace() {
  return useQuery({
    queryKey: ['workspace'],
    queryFn: workspaceService.getWorkspace,
    staleTime: 5_000,
    retry: false,
  })
}

export function useCreateWorkspace() {
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: workspaceService.createWorkspace,
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['workspace'] })
    },
  })
}

export function useDeleteWorkspace() {
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: workspaceService.deleteWorkspace,
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['workspace'] })
    },
  })
}

export function useProvisioningWorkspace() {
  return useQuery({
    queryKey: ['workspace'],
    queryFn: workspaceService.getWorkspace,
    refetchInterval: (query) => {
      const data = query.state.data as WorkspaceResponse | null | undefined
      if (data && (data.status === 'active' || data.status === 'failed')) {
        return false
      }
      return 3_000
    },
    staleTime: 2_000,
    retry: false,
  })
}
