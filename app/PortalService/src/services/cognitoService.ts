interface TokenSet {
  idToken: string
  accessToken: string
  refreshToken: string
  expiresAt: number
}

const STORAGE_KEY = 'portal_tokens'

function getDomain(): string {
  const domain = import.meta.env.VITE_COGNITO_DOMAIN
  if (!domain) throw new Error('VITE_COGNITO_DOMAIN is not configured')
  return domain
}

function getClientId(): string {
  const clientId = import.meta.env.VITE_COGNITO_CLIENT_ID
  if (!clientId) throw new Error('VITE_COGNITO_CLIENT_ID is not configured')
  return clientId
}

function getRedirectUri(): string {
  return import.meta.env.VITE_COGNITO_REDIRECT_URI || window.location.origin + '/callback'
}

function base64URLEncode(buffer: Uint8Array): string {
  return btoa(String.fromCharCode(...buffer))
    .replace(/\+/g, '-')
    .replace(/\//g, '_')
    .replace(/=+$/, '')
}

function generateRandomState(): string {
  const array = new Uint8Array(16)
  crypto.getRandomValues(array)
  return base64URLEncode(array)
}

function generateCodeVerifier(): string {
  const array = new Uint8Array(32)
  crypto.getRandomValues(array)
  return base64URLEncode(array)
}

async function generateCodeChallenge(verifier: string): Promise<string> {
  const encoder = new TextEncoder()
  const data = encoder.encode(verifier)
  const digest = await crypto.subtle.digest('SHA-256', data)
  return base64URLEncode(new Uint8Array(digest))
}

function storeTokens(tokens: TokenSet): void {
  sessionStorage.setItem(STORAGE_KEY, JSON.stringify(tokens))
}

export function getStoredTokens(): TokenSet | null {
  const raw = sessionStorage.getItem(STORAGE_KEY)
  if (!raw) return null
  try {
    const tokens = JSON.parse(raw) as TokenSet
    if (tokens.expiresAt < Date.now()) {
      clearTokens()
      return null
    }
    return tokens
  } catch {
    clearTokens()
    return null
  }
}

function clearTokens(): void {
  sessionStorage.removeItem(STORAGE_KEY)
}

function parseJwtPayload(token: string): Record<string, unknown> | null {
  try {
    const parts = token.split('.')
    if (parts.length !== 3) return null
    return JSON.parse(atob(parts[1].replace(/-/g, '+').replace(/_/g, '/')))
  } catch {
    return null
  }
}

export function getUserEmail(): string | null {
  const tokens = getStoredTokens()
  if (!tokens) return null
  const payload = parseJwtPayload(tokens.idToken)
  return (payload?.email as string) || null
}

export function getIdToken(): string | null {
  return getStoredTokens()?.idToken || null
}

export function isAuthenticated(): boolean {
  return getStoredTokens() !== null
}

export async function signIn(): Promise<void> {
  const verifier = generateCodeVerifier()
  const challenge = await generateCodeChallenge(verifier)
  const state = generateRandomState()

  sessionStorage.setItem('pkce_verifier', verifier)
  sessionStorage.setItem('pkce_state', state)

  const params = new URLSearchParams({
    response_type: 'code',
    client_id: getClientId(),
    redirect_uri: getRedirectUri(),
    code_challenge_method: 'S256',
    code_challenge: challenge,
    state,
    scope: 'email openid profile',
  })

  window.location.href = `https://${getDomain()}/oauth2/authorize?${params.toString()}`
}

export async function handleCallback(): Promise<void> {
  const urlParams = new URLSearchParams(window.location.search)
  const code = urlParams.get('code')
  const state = urlParams.get('state')
  const error = urlParams.get('error')

  if (error) {
    throw new Error(urlParams.get('error_description') || `Auth error: ${error}`)
  }

  if (!code) {
    throw new Error('No authorization code in callback')
  }

  const storedState = sessionStorage.getItem('pkce_state')
  if (state !== storedState) {
    throw new Error('State mismatch — possible CSRF')
  }
  sessionStorage.removeItem('pkce_state')

  const verifier = sessionStorage.getItem('pkce_verifier')
  if (!verifier) {
    throw new Error('No PKCE verifier found')
  }
  sessionStorage.removeItem('pkce_verifier')

  const tokenResponse = await fetch(`https://${getDomain()}/oauth2/token`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/x-www-form-urlencoded' },
    body: new URLSearchParams({
      grant_type: 'authorization_code',
      client_id: getClientId(),
      code,
      redirect_uri: getRedirectUri(),
      code_verifier: verifier,
    }),
  })

  if (!tokenResponse.ok) {
    const err = await tokenResponse.json().catch(() => ({ error: 'token_exchange_failed' }))
    throw new Error((err as { error_description?: string }).error_description || 'Token exchange failed')
  }

  const data = await tokenResponse.json() as {
    id_token: string
    access_token: string
    refresh_token: string
    expires_in: number
  }

  storeTokens({
    idToken: data.id_token,
    accessToken: data.access_token,
    refreshToken: data.refresh_token,
    expiresAt: Date.now() + data.expires_in * 1000,
  })
}

export async function signOut(): Promise<void> {
  const tokens = getStoredTokens()
  clearTokens()

  if (tokens) {
    const params = new URLSearchParams({
      client_id: getClientId(),
      logout_uri: window.location.origin,
    })
    window.location.href = `https://${getDomain()}/logout?${params.toString()}`
  }
}
