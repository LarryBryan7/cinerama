import { http as mswHttp, HttpResponse } from 'msw'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { problem, server, url } from '@/test/server'
import { http, onSessionLost, tokenStore } from './client'

beforeEach(() => tokenStore.set('viejo'))

describe('cliente HTTP', () => {
  it('envía el access token como Bearer', async () => {
    let header: string | null = null
    server.use(mswHttp.get(url('/ping'), ({ request }) => { header = request.headers.get('authorization'); return HttpResponse.json({}) }))
    await http.get('/ping')
    expect(header).toBe('Bearer viejo')
  })

  it('no guarda el token en localStorage ni en sessionStorage (ADR-006)', async () => {
    server.use(mswHttp.post(url('/auth/refresh'), () => HttpResponse.json({ accessToken: 'nuevo', accessTokenExpiresAt: '2099-01-01T00:00:00Z', user: {} })))
    server.use(mswHttp.get(url('/ping'), ({ request }) => request.headers.get('authorization') === 'Bearer nuevo' ? HttpResponse.json({}) : problem(401, 'token_expired')))
    await http.get('/ping')
    expect(JSON.stringify({ ...localStorage })).not.toContain('nuevo')
    expect(JSON.stringify({ ...sessionStorage })).not.toContain('nuevo')
  })

  it('ante token_expired renueva la sesión una vez y repite la solicitud', async () => {
    const refresh = vi.fn()
    server.use(
      mswHttp.post(url('/auth/refresh'), () => { refresh(); return HttpResponse.json({ accessToken: 'nuevo', accessTokenExpiresAt: '2099-01-01T00:00:00Z', user: {} }) }),
      mswHttp.get(url('/data'), ({ request }) => request.headers.get('authorization') === 'Bearer nuevo' ? HttpResponse.json({ ok: true }) : problem(401, 'token_expired')),
    )
    const response = await http.get('/data')
    expect(response.data).toEqual({ ok: true })
    expect(refresh).toHaveBeenCalledTimes(1)
    expect(tokenStore.get()).toBe('nuevo')
  })

  it('varias solicitudes que expiran a la vez comparten UN solo refresh (si no, el segundo se vería como reutilización y revocaría la sesión)', async () => {
    const refresh = vi.fn()
    server.use(
      mswHttp.post(url('/auth/refresh'), async () => { refresh(); await new Promise((r) => setTimeout(r, 30)); return HttpResponse.json({ accessToken: 'nuevo', accessTokenExpiresAt: '2099-01-01T00:00:00Z', user: {} }) }),
      mswHttp.get(url('/data'), ({ request }) => request.headers.get('authorization') === 'Bearer nuevo' ? HttpResponse.json({ ok: true }) : problem(401, 'token_expired')),
    )
    const results = await Promise.all([http.get('/data'), http.get('/data'), http.get('/data'), http.get('/data')])
    expect(results.every((r) => r.status === 200)).toBe(true)
    expect(refresh).toHaveBeenCalledTimes(1)
  })

  it('si el refresh falla con 401 la sesión se da por perdida y se avisa una vez', async () => {
    const lost = vi.fn()
    const off = onSessionLost(lost)
    server.use(
      mswHttp.post(url('/auth/refresh'), () => problem(401, 'invalid_refresh_token')),
      mswHttp.get(url('/data'), () => problem(401, 'token_expired')),
    )
    await expect(http.get('/data')).rejects.toMatchObject({ status: 401 })
    expect(lost).toHaveBeenCalledTimes(1)
    expect(tokenStore.get()).toBeNull()
    off()
  })

  it('un 401 distinto de token_expired no intenta renovar: la sesión se pierde directamente', async () => {
    const refresh = vi.fn()
    const lost = vi.fn()
    const off = onSessionLost(lost)
    server.use(mswHttp.post(url('/auth/refresh'), () => { refresh(); return HttpResponse.json({}) }), mswHttp.get(url('/data'), () => problem(401, 'unauthorized')))
    await expect(http.get('/data')).rejects.toMatchObject({ status: 401, code: 'unauthorized' })
    expect(refresh).not.toHaveBeenCalled()
    expect(lost).toHaveBeenCalledTimes(1)
    off()
  })

  it('un 401 en el login (credenciales incorrectas) no cierra ninguna sesión', async () => {
    const lost = vi.fn()
    const off = onSessionLost(lost)
    server.use(mswHttp.post(url('/auth/login'), () => problem(401, 'invalid_credentials')))
    await expect(http.post('/auth/login', {})).rejects.toMatchObject({ code: 'invalid_credentials' })
    expect(lost).not.toHaveBeenCalled()
    off()
  })

  it('traduce un problem+json a ApiError con code y errores por campo en camelCase', async () => {
    server.use(mswHttp.post(url('/x'), () => problem(400, 'validation_failed', 'Datos inválidos', { Title: ['Obligatorio'] })))
    await expect(http.post('/x', {})).rejects.toMatchObject({ status: 400, code: 'validation_failed', fieldErrors: { title: ['Obligatorio'] } })
  })

  it('un fallo de red se distingue de un error del servidor', async () => {
    server.use(mswHttp.get(url('/down'), () => HttpResponse.error()))
    await expect(http.get('/down')).rejects.toMatchObject({ isNetwork: true })
  })
})
