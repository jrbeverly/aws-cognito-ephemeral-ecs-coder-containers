<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { useRouter } from 'vue-router'
import { useAuthStore } from '@/stores/auth'

const router = useRouter()
const authStore = useAuthStore()
const error = ref<string | null>(null)
const loading = ref(true)

onMounted(async () => {
  try {
    await authStore.handleCallback()
    router.replace({ name: 'portal' })
  } catch (e) {
    error.value = e instanceof Error ? e.message : 'Authentication failed'
  } finally {
    loading.value = false
  }
})
</script>

<template>
  <v-container class="fill-height d-flex align-center justify-center">
    <v-sheet max-width="480" class="pa-6 text-center">
      <v-progress-circular
        v-if="loading"
        indeterminate
        color="primary"
        size="48"
      />
      <template v-else-if="error">
        <v-icon icon="mdi-alert-circle" size="48" color="error" class="mb-3" />
        <p class="text-body-1 mb-4">{{ error }}</p>
        <v-btn variant="tonal" @click="router.push({ name: 'login' })">
          Return to Sign In
        </v-btn>
      </template>
    </v-sheet>
  </v-container>
</template>
