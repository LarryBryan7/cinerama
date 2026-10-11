import { screen, waitFor, within } from '@testing-library/react'
import { http, HttpResponse } from 'msw'
import { describe, expect, it } from 'vitest'
import { makeGenre } from '@/test/factories'
import { renderApp } from '@/test/render'
import { problem, server, url } from '@/test/server'

describe('Géneros', () => {
  it('no permite borrar un género con películas', async () => {
    server.use(http.get(url('/admin/genres'), () => HttpResponse.json([makeGenre({ id: 1, name: 'Acción', movieCount: 3 }), makeGenre({ id: 2, name: 'Drama', movieCount: 0 })])))
    renderApp('/genres')
    expect(await screen.findByRole('button', { name: 'Borrar Acción' })).toBeDisabled()
    expect(screen.getByRole('button', { name: 'Borrar Drama' })).toBeEnabled()
  })

  it('crea un género y recarga la lista', async () => {
    let names = ['Acción']
    server.use(
      http.get(url('/admin/genres'), () => HttpResponse.json(names.map((name, i) => makeGenre({ id: i + 1, name })))),
      http.post(url('/genres'), async ({ request }) => { const { name } = (await request.json()) as { name: string }; names = [...names, name]; return HttpResponse.json(makeGenre({ id: 9, name }), { status: 201 }) }),
    )
    const { user } = renderApp('/genres')
    await user.click(await screen.findByRole('button', { name: 'Nuevo género' }))
    await user.type(within(await screen.findByRole('dialog')).getByLabelText(/Nombre/), 'Terror')
    await user.click(within(screen.getByRole('dialog')).getByRole('button', { name: 'Guardar' }))
    expect(await screen.findByText('Género creado.')).toBeInTheDocument()
    expect(await screen.findByText('Terror')).toBeInTheDocument()
  })

  it('un nombre repetido (409) se muestra en el campo y el diálogo sigue abierto', async () => {
    server.use(
      http.get(url('/admin/genres'), () => HttpResponse.json([makeGenre()])),
      http.post(url('/genres'), () => problem(409, 'genre_name_taken', 'Duplicado')),
    )
    const { user } = renderApp('/genres')
    await user.click(await screen.findByRole('button', { name: 'Nuevo género' }))
    const dialog = await screen.findByRole('dialog')
    await user.type(within(dialog).getByLabelText(/Nombre/), 'Acción')
    await user.click(within(dialog).getByRole('button', { name: 'Guardar' }))
    expect(await within(dialog).findByText('Ya existe un género con ese nombre.')).toBeInTheDocument()
  })

  it('pide confirmación antes de borrar y no borra si se vuelve atrás', async () => {
    let deleted = 0
    server.use(
      http.get(url('/admin/genres'), () => HttpResponse.json([makeGenre({ id: 2, name: 'Drama', movieCount: 0 })])),
      http.delete(url('/genres/2'), () => { deleted++; return new HttpResponse(null, { status: 204 }) }),
    )
    const { user } = renderApp('/genres')
    await user.click(await screen.findByRole('button', { name: 'Borrar Drama' }))
    await user.click(within(await screen.findByRole('dialog')).getByRole('button', { name: 'Cancelar' }))
    expect(deleted).toBe(0)
    await user.click(screen.getByRole('button', { name: 'Borrar Drama' }))
    await user.click(within(await screen.findByRole('dialog')).getByRole('button', { name: 'Borrar' }))
    await waitFor(() => expect(deleted).toBe(1))
  })
})
