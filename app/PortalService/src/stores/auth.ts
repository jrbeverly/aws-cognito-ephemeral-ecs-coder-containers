import { defineStore } from 'pinia'
import { ref, computed } from 'vue'
import {
  isAuthenticated,
  getUserEmail,
  signIn as cognitoSignIn,
  handleCallback as cognitoHandleCallback,
  signOut as cognitoSignOut,
} from '@/services/cognitoService'

export const useAuthStore = defineStore('auth', () => {
  const authenticated = ref(isAuthenticated())
  const email = ref(getUserEmail())

  const isLoggedIn = computed(() => authenticated.value)

  async function login(): Promise<void> {
    await cognitoSignIn()
  }

  async function handleCallback(): Promise<void> {
    await cognitoHandleCallback()
    authenticated.value = true
    email.value = getUserEmail()
  }

  async function logout(): Promise<void> {
    await cognitoSignOut()
    authenticated.value = false
    email.value = null
  }

  function refreshAuthState(): void {
    authenticated.value = isAuthenticated()
    email.value = getUserEmail()
  }

  return {
    authenticated,
    email,
    isLoggedIn,
    login,
    handleCallback,
    logout,
    refreshAuthState,
  }
})
