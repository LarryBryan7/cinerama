import { screen, waitFor } from '@testing-library/react'
import { http as mswHttp, HttpResponse } from 'msw'
import { describe, expect, it } from 'vitest'
import { tokenStore } from '@/api/client'
import { customer, makeAuth } from '@/test/factories'
import { renderApp } from '@/test/render'
import { problem, server, url } from '@/test/server'

const noSession = () => mswHttp.post(url('/auth/refresh'), () => problem(401, 'invalid_refresh_token'))

describe('autenticación y rutas protegidas', () => {
  it('sin sesión, una ruta protegida lleva al login', async () => {
    server.use(noSession())
    renderApp('/movies')
    expect(await screen.findByRole('heading', { name: 'Cinerama' })).toBeInTheDocument()
    expect(screen.getByLabelText(/email/i)).toBeInTheDocument()
  })

  it('con la cookie de refresh vigente la sesión se recupera al cargar y no se pasa por el login', async () => {
    renderApp('/')
    expect(await screen.findByText(/Hola, Ana/)).toBeInTheDocument()
    expect(tokenStore.get()).toBe('token-1')
  })

  it('inicia sesión con credenciales válidas y vuelve a donde se quería ir', async () => {
    server.use(noSession())
    const { user } = renderApp('/users')
    await user.type(await screen.findByLabelText(/email/i), 'admin@cinerama.test')
    await user.type(screen.getByLabelText(/contraseña/i), 'Secreta123!')
    server.use(mswHttp.get(url('/users'), () => HttpResponse.json({ items: [], page: 1, pageSize: 20, totalCount: 0 })))
    await user.click(screen.getByRole('button', { name: 'Entrar' }))
    expect(await screen.findByRole('heading', { name: 'Usuarios' })).toBeInTheDocument()
  })

  it('valida los campos antes de llamar a la API', async () => {
    server.use(noSession())
    const { user } = renderApp('/login')
    await user.click(await screen.findByRole('button', { name: 'Entrar' }))
    expect(await screen.findByText('Escribe tu email.')).toBeInTheDocument()
    expect(screen.getByText('Escribe tu contraseña.')).toBeInTheDocument()
  })

  it('credenciales incorrectas: mensaje genérico, sin revelar si el email existe', async () => {
    server.use(noSession(), mswHttp.post(url('/auth/login'), () => problem(401, 'invalid_credentials')))
    const { user } = renderApp('/login')
    await user.type(await screen.findByLabelText(/email/i), 'alguien@x.com')
    await user.type(screen.getByLabelText(/contraseña/i), 'mala')
    await user.click(screen.getByRole('button', { name: 'Entrar' }))
    expect(await screen.findByRole('alert')).toHaveTextContent('El email o la contraseña no son correctos.')
  })

  it('una cuenta de cliente no entra al panel y su sesión recién abierta se revoca', async () => {
    let logouts = 0
    server.use(
      noSession(),
      mswHttp.post(url('/auth/login'), () => HttpResponse.json(makeAuth(customer))),
      mswHttp.post(url('/auth/logout'), () => { logouts++; return new HttpResponse(null, { status: 204 }) }),
    )
    const { user } = renderApp('/login')
    await user.type(await screen.findByLabelText(/email/i), customer.email)
    await user.type(screen.getByLabelText(/contraseña/i), 'Secreta123!')
    await user.click(screen.getByRole('button', { name: 'Entrar' }))
    expect(await screen.findByRole('alert')).toHaveTextContent('no tiene acceso al panel')
    expect(logouts).toBe(1)
    expect(tokenStore.get()).toBeNull()
  })

  it('un cliente con cookie de refresh vigente tampoco entra al panel', async () => {
    server.use(mswHttp.post(url('/auth/refresh'), () => HttpResponse.json(makeAuth(customer))))
    renderApp('/')
    expect(await screen.findByLabelText(/email/i)).toBeInTheDocument()
    expect(tokenStore.get()).toBeNull()
  })

  it('al cerrar sesión se vuelve al login y se limpia el token', async () => {
    const { user } = renderApp('/')
    await screen.findByText(/Hola, Ana/)
    await user.click(screen.getByRole('button', { name: /cerrar sesión|salir/i }))
    await waitFor(() => expect(screen.getByLabelText(/email/i)).toBeInTheDocument())
    expect(tokenStore.get()).toBeNull()
  })
})
