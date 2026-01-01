<script setup lang="ts">
import { useAuthStore } from '@/stores/auth'

const authStore = useAuthStore()

async function handleSignIn() {
  await authStore.login()
}
</script>

<template>
  <v-container class="fill-height d-flex align-center justify-center">
    <v-sheet max-width="480" class="pa-8 text-center">
      <v-icon icon="mdi-cloud-braces" size="64" color="primary" class="mb-4" />
      <h1 class="text-h4 mb-2">Workspace Portal</h1>
      <p class="text-body-1 text-medium-emphasis mb-6">
        Sign in to access your secure, browser-based development workspace.
      </p>
      <v-btn
        variant="tonal"
        color="primary"
        size="large"
        block
        @click="handleSignIn"
      >
        Sign In
      </v-btn>
    </v-sheet>
  </v-container>
</template>
