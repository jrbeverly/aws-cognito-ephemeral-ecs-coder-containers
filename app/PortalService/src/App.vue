<script setup lang="ts">
import { useAuthStore } from '@/stores/auth'
import { useRouter, useRoute } from 'vue-router'
import { watch } from 'vue'

const authStore = useAuthStore()
const router = useRouter()
const route = useRoute()

watch(
  () => authStore.isLoggedIn,
  (loggedIn) => {
    if (loggedIn && route.name === 'login') {
      router.replace({ name: 'portal' })
    }
  },
)
</script>

<template>
  <v-app>
    <v-main>
      <router-view />
    </v-main>
  </v-app>
</template>
