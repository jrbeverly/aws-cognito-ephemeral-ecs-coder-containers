<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { useAuthStore } from '@/stores/auth'
import { useProvisioningWorkspace, useCreateWorkspace, useDeleteWorkspace } from '@/composables/useWorkspace'
import type { WorkspaceResponse } from '@/types/workspace'

const authStore = useAuthStore()
const {
  mutateAsync,
  isPending: isCreating,
  error: createError,
} = useCreateWorkspace()

const {
  data: workspace,
  isLoading,
  isError,
  error: queryError,
  refetch,
} = useProvisioningWorkspace()

const {
  mutateAsync: deleteAsync,
  isPending: isDeleting,
  error: deleteError,
} = useDeleteWorkspace()

const showCreate = ref(false)

const errorMessage = computed(() => {
  const e = queryError.value
  return e instanceof Error ? e.message : String(e ?? '')
})

const createErrorMessage = computed(() => {
  const e = createError.value
  return e instanceof Error ? e.message : String(e ?? '')
})

const deleteErrorMessage = computed(() => {
  const e = deleteError.value
  return e instanceof Error ? e.message : String(e ?? '')
})

const workspaceUrl = computed(() => {
  const ws = workspace.value as WorkspaceResponse | undefined
  return ws?.workspaceUrl
})

const statusText = computed(() => {
  const ws = workspace.value as WorkspaceResponse | undefined
  if (!ws) return null
  switch (ws.status) {
    case 'provisioning':
      return 'Your workspace is being provisioned. This usually takes about a minute.'
    case 'active':
      return 'Your workspace is ready.'
    case 'failed':
      return 'Workspace provisioning failed. You can try creating a new one.'
    case 'deleting':
      return 'Your workspace is being removed.'
    default:
      return null
  }
})

const noWorkspace = computed(() => !workspace.value && !isLoading.value)

watch(workspaceUrl, (url) => {
  if (url) {
    window.location.href = url
  }
})

async function handleCreate() {
  showCreate.value = true
  try {
    await mutateAsync()
    refetch()
  } catch {
    // Error is surfaced via createError
  }
}

function handleReconnect() {
  const url = workspaceUrl.value
  if (url) {
    window.location.href = url
  }
}

async function handleDelete() {
  try {
    await deleteAsync()
    refetch()
  } catch {
    // Error is surfaced via deleteError
  }
}

async function handleLogout() {
  await authStore.logout()
}
</script>

<template>
  <v-container class="py-8" style="max-width: 720px">
    <!-- Header -->
    <v-row class="mb-6">
      <v-col class="d-flex align-center justify-space-between">
        <div>
          <h1 class="text-h4">Workspace Portal</h1>
          <p v-if="authStore.email" class="text-body-2 text-medium-emphasis">
            {{ authStore.email }}
          </p>
        </div>
        <v-btn variant="text" size="small" @click="handleLogout">
          Sign Out
        </v-btn>
      </v-col>
    </v-row>

    <!-- Loading -->
    <v-row v-if="isLoading">
      <v-col class="text-center py-12">
        <v-progress-circular indeterminate color="primary" size="48" />
        <p class="text-body-1 text-medium-emphasis mt-4">
          Checking workspace status...
        </p>
      </v-col>
    </v-row>

    <!-- Error -->
    <v-row v-else-if="isError">
      <v-col>
        <v-alert type="error" variant="tonal" class="mb-4">
          {{ errorMessage || 'Failed to check workspace status' }}
        </v-alert>
        <v-btn variant="outlined" @click="refetch()">Retry</v-btn>
      </v-col>
    </v-row>

    <!-- Workspace Active: Reconnect -->
    <v-row v-else-if="workspace && workspace.status === 'active'">
      <v-col>
        <v-card variant="outlined">
          <v-card-item>
            <template #prepend>
              <v-icon icon="mdi-check-circle" color="success" size="36" />
            </template>
            <v-card-title>Workspace Ready</v-card-title>
            <v-card-subtitle>{{ statusText }}</v-card-subtitle>
          </v-card-item>
          <v-card-text>
            <v-list density="compact">
              <v-list-item>
                <template #prepend>
                  <v-icon icon="mdi-identifier" size="small" />
                </template>
                <v-list-item-title class="text-caption">
                  {{ workspace.workspaceId }}
                </v-list-item-title>
                <v-list-item-subtitle class="text-caption text-medium-emphasis">
                  Workspace ID
                </v-list-item-subtitle>
              </v-list-item>
              <v-list-item v-if="workspaceUrl">
                <template #prepend>
                  <v-icon icon="mdi-link" size="small" />
                </template>
                <v-list-item-title class="text-caption text-break">
                  {{ workspaceUrl }}
                </v-list-item-title>
                <v-list-item-subtitle class="text-caption text-medium-emphasis">
                  Workspace URL
                </v-list-item-subtitle>
              </v-list-item>
            </v-list>
          </v-card-text>
          <v-card-actions>
            <v-btn
              variant="tonal"
              color="primary"
              block
              @click="handleReconnect"
            >
              Open Workspace
            </v-btn>
          </v-card-actions>
          <v-card-actions>
            <v-btn
              variant="text"
              color="error"
              size="small"
              :loading="isDeleting"
              @click="handleDelete"
            >
              Delete Workspace
            </v-btn>
          </v-card-actions>
          <v-alert
            v-if="deleteError"
            type="error"
            variant="tonal"
            class="ma-4"
            density="compact"
          >
            {{ deleteErrorMessage }}
          </v-alert>
        </v-card>
      </v-col>
    </v-row>

    <!-- Workspace Provisioning -->
    <v-row v-else-if="workspace && workspace.status === 'provisioning'">
      <v-col>
        <v-card variant="outlined">
          <v-card-item>
            <template #prepend>
              <v-progress-circular
                indeterminate
                color="primary"
                size="32"
                width="3"
              />
            </template>
            <v-card-title>Provisioning Workspace</v-card-title>
            <v-card-subtitle>{{ statusText }}</v-card-subtitle>
          </v-card-item>
          <v-card-actions>
            <v-btn
              variant="text"
              color="error"
              size="small"
              :loading="isDeleting"
              @click="handleDelete"
            >
              Cancel
            </v-btn>
          </v-card-actions>
          <v-alert
            v-if="deleteError"
            type="error"
            variant="tonal"
            class="ma-4"
            density="compact"
          >
            {{ deleteErrorMessage }}
          </v-alert>
        </v-card>
      </v-col>
    </v-row>

    <!-- Workspace Failed -->
    <v-row v-else-if="workspace && workspace.status === 'failed'">
      <v-col>
        <v-alert type="error" variant="tonal" class="mb-4">
          {{ statusText }}
        </v-alert>
        <v-btn
          variant="tonal"
          color="primary"
          :loading="isCreating"
          @click="handleCreate"
        >
          Create New Workspace
        </v-btn>
        <v-btn
          variant="text"
          color="error"
          size="small"
          class="mt-2"
          :loading="isDeleting"
          @click="handleDelete"
        >
          Remove
        </v-btn>
      </v-col>
    </v-row>

    <!-- Workspace Deleting -->
    <v-row v-else-if="workspace && workspace.status === 'deleting'">
      <v-col class="text-center py-12">
        <v-progress-circular indeterminate color="warning" size="48" />
        <p class="text-body-1 text-medium-emphasis mt-4">
          {{ statusText }}
        </p>
      </v-col>
    </v-row>

    <!-- No Workspace -->
    <v-row v-else-if="noWorkspace && !showCreate">
      <v-col>
        <v-card variant="outlined">
          <v-card-item>
            <template #prepend>
              <v-icon icon="mdi-play-circle" color="primary" size="36" />
            </template>
            <v-card-title>No Active Workspace</v-card-title>
            <v-card-subtitle>
              You don't have an active workspace. Create one to get started.
            </v-card-subtitle>
          </v-card-item>
          <v-card-actions>
            <v-btn
              variant="tonal"
              color="primary"
              block
              :loading="isCreating"
              @click="handleCreate"
            >
              Create Workspace
            </v-btn>
          </v-card-actions>
        </v-card>
      </v-col>
    </v-row>

    <!-- Creating -->
    <v-row v-else-if="showCreate && isCreating">
      <v-col class="text-center py-12">
        <v-progress-circular indeterminate color="primary" size="48" />
        <p class="text-body-1 text-medium-emphasis mt-4">
          Creating your workspace...
        </p>
      </v-col>
    </v-row>

    <!-- Create Error -->
    <v-row v-else-if="showCreate && createError">
      <v-col>
        <v-alert type="error" variant="tonal" class="mb-4">
          {{ createErrorMessage || 'Failed to create workspace' }}
        </v-alert>
        <v-btn variant="outlined" @click="showCreate = false">Back</v-btn>
      </v-col>
    </v-row>
  </v-container>
</template>
