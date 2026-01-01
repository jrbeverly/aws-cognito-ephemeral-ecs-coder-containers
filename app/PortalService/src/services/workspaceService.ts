import { getIdToken } from './cognitoService'
import type { WorkspaceResponse } from '@/types/workspace'

function getApiUrl(): string {
  return import.meta.env.VITE_API_URL || ''
}

async function apiFetch<T>(path: string, options?: RequestInit): Promise<T> {
  const token = getIdToken()
  if (!token) throw new Error('Not authenticated')

  const url = `${getApiUrl()}${path}`
  const response = await fetch(url, {
    ...options,
    headers: {
      Authorization: `Bearer ${token}`,
      'Content-Type': 'application/json',
      ...options?.headers,
    },
  })

  if (response.status === 401) {
    throw new Error('Session expired')
  }

  if (!response.ok) {
    const body = await response.json().catch(() => ({}))
    throw new Error((body as { detail?: string }).detail || `Request failed: ${response.status}`)
  }

  return response.json() as Promise<T>
}

export const workspaceService = {
  async getWorkspace(): Promise<WorkspaceResponse | null> {
    try {
      return await apiFetch<WorkspaceResponse>('/api/v1/workspaces/me')
    } catch (e) {
      if (e instanceof Error && e.message.includes('No workspace found')) {
        return null
      }
      throw e
    }
  },

  createWorkspace(): Promise<WorkspaceResponse> {
    return apiFetch<WorkspaceResponse>('/api/v1/workspaces/', {
      method: 'POST',
    })
  },

  async deleteWorkspace(): Promise<void> {
    const token = getIdToken()
    if (!token) throw new Error('Not authenticated')

    const url = `${getApiUrl()}/api/v1/workspaces/me`
    const response = await fetch(url, {
      method: 'DELETE',
      headers: {
        Authorization: `Bearer ${token}`,
      },
    })

    if (response.status === 401) throw new Error('Session expired')
    if (response.status === 404) throw new Error('No workspace found for this user.')
    if (!response.ok) {
      const body = await response.json().catch(() => ({}))
      throw new Error((body as { detail?: string }).detail || `Request failed: ${response.status}`)
    }
  },
}
