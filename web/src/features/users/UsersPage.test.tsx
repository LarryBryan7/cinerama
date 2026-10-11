import { screen, waitFor, within } from '@testing-library/react'
import { http, HttpResponse } from 'msw'
import { describe, expect, it } from 'vitest'
import { admin, makeUser, paged } from '@/test/factories'
import { renderApp } from '@/test/render'
import { server, url } from '@/test/server'

describe('Usuarios', () => {
  it('no deja desactivar la propia cuenta', async () => {
    server.use(http.get(url('/users'), () => HttpResponse.json(paged([
      makeUser({ id: admin.id, fullName: admin.fullName, email: admin.email, role: 'Admin' }),
      makeUser({ id: 2 }),
    ]))))
    renderApp('/users')
    expect(await screen.findByRole('button', { name: `Desactivar la cuenta de ${admin.fullName}` })).toBeDisabled()
    expect(screen.getByRole('button', { name: 'Desactivar la cuenta de Carlos Cliente' })).toBeEnabled()
    expect(screen.getByText('(tú)')).toBeInTheDocument()
  })

  it('desactivar pide confirmación, advierte del cierre de sesiones y recarga la lista', async () => {
    let body: unknown = null
    server.use(
      http.get(url('/users'), () => HttpResponse.json(paged([makeUser({ id: 2 })]))),
      http.put(url('/users/2/status'), async ({ request }) => { body = await request.json(); return HttpResponse.json(makeUser({ isActive: false })) }),
    )
    const { user } = renderApp('/users')
    await user.click(await screen.findByRole('button', { name: 'Desactivar la cuenta de Carlos Cliente' }))
    const dialog = await screen.findByRole('dialog', { name: 'Desactivar la cuenta' })
    expect(dialog).toHaveTextContent('Se cerrarán sus sesiones')
    expect(body).toBeNull()
    await user.click(within(dialog).getByRole('button', { name: 'Desactivar' }))
    await waitFor(() => expect(body).toEqual({ isActive: false }))
    expect(await screen.findByText(/se desactivó y sus sesiones se cerraron/)).toBeInTheDocument()
  })

  it('activar una cuenta inactiva no usa un diálogo de peligro y envía isActive true', async () => {
    let body: unknown = null
    server.use(
      http.get(url('/users'), () => HttpResponse.json(paged([makeUser({ id: 3, fullName: 'Inés Inactiva', isActive: false })]))),
      http.put(url('/users/3/status'), async ({ request }) => { body = await request.json(); return HttpResponse.json(makeUser({ id: 3 })) }),
    )
    const { user } = renderApp('/users')
    await user.click(await screen.findByRole('button', { name: 'Activar la cuenta de Inés Inactiva' }))
    await user.click(within(await screen.findByRole('dialog')).getByRole('button', { name: 'Activar' }))
    await waitFor(() => expect(body).toEqual({ isActive: true }))
  })

  it('manda rol y estado como filtros', async () => {
    const seen: URLSearchParams[] = []
    server.use(http.get(url('/users'), ({ request }) => { seen.push(new URL(request.url).searchParams); return HttpResponse.json(paged([makeUser()])) }))
    const { user } = renderApp('/users')
    await screen.findByText('Carlos Cliente')
    await user.selectOptions(screen.getByLabelText('Rol'), 'Admin')
    await user.selectOptions(screen.getByLabelText('Estado de la cuenta'), 'false')
    await waitFor(() => expect(seen.at(-1)?.get('isActive')).toBe('false'))
    expect(seen.at(-1)?.get('role')).toBe('Admin')
  })
})
