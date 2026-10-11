import { screen, waitFor } from '@testing-library/react'
import { http, HttpResponse } from 'msw'
import { describe, expect, it } from 'vitest'
import type { MovieInput } from '@/api/types'
import { makeGenre } from '@/test/factories'
import { renderApp } from '@/test/render'
import { problem, server, url } from '@/test/server'

const setup = () => server.use(http.get(url('/admin/genres'), () => HttpResponse.json([makeGenre({ id: 1, name: 'Acción' }), makeGenre({ id: 2, name: 'Drama' })])))

describe('Formulario de película', () => {
  it('valida los obligatorios y no llama a la API mientras haya errores', async () => {
    setup()
    let calls = 0
    server.use(http.post(url('/movies'), () => { calls++; return HttpResponse.json({}, { status: 201 }) }))
    const { user } = renderApp('/movies/new')
    await user.click(await screen.findByRole('button', { name: 'Crear película' }))
    expect(await screen.findByText('Escribe la duración en minutos.')).toBeInTheDocument()
    expect(screen.getByText('Elige al menos un género.')).toBeInTheDocument()
    expect(screen.getByText('Elige la fecha de estreno.')).toBeInTheDocument()
    expect(calls).toBe(0)
  })

  it('rechaza un tráiler que no es de YouTube', async () => {
    setup()
    const { user } = renderApp('/movies/new')
    await user.type(await screen.findByLabelText(/Tráiler/), 'https://vimeo.com/123')
    await user.click(screen.getByRole('button', { name: 'Crear película' }))
    expect(await screen.findByText('Debe ser un enlace de YouTube válido.')).toBeInTheDocument()
  })

  it('rechaza una preventa posterior al estreno cuando el resto de los campos es válido', async () => {
    setup()
    let calls = 0
    server.use(http.post(url('/movies'), () => { calls++; return HttpResponse.json({}, { status: 201 }) }))
    const { user } = renderApp('/movies/new')
    await user.type(await screen.findByLabelText(/Título/), 'Dune')
    await user.type(screen.getByLabelText(/Duración/), '155')
    await user.type(screen.getByLabelText(/Clasificación/), '+14')
    await user.click(screen.getByRole('checkbox', { name: 'Acción' }))
    await user.type(screen.getByLabelText(/Fecha de estreno/), '2030-03-10')
    await user.type(screen.getByLabelText(/Abre la preventa/), '2030-03-20')
    await user.click(screen.getByRole('button', { name: 'Crear película' }))
    expect(await screen.findByText('La preventa no puede abrir después del estreno.')).toBeInTheDocument()
    expect(calls).toBe(0)
  })

  it('crea la película enviando textos recortados, opcionales como null y solo el código del tráiler no se altera', async () => {
    setup()
    let sent: MovieInput | null = null
    server.use(
      http.post(url('/movies'), async ({ request }) => { sent = (await request.json()) as MovieInput; return HttpResponse.json({ id: 9, ...sent }, { status: 201 }) }),
      http.get(url('/admin/movies'), () => HttpResponse.json({ items: [], page: 1, pageSize: 20, totalCount: 0 })),
    )
    const { user } = renderApp('/movies/new')
    await user.type(await screen.findByLabelText(/Título/), '  Dune  ')
    await user.type(screen.getByLabelText(/Duración/), '155')
    await user.type(screen.getByLabelText(/Clasificación/), '+14')
    await user.click(screen.getByRole('checkbox', { name: 'Acción' }))
    await user.type(screen.getByLabelText(/Fecha de estreno/), '2030-03-10')
    await user.click(screen.getByRole('button', { name: 'Crear película' }))

    await waitFor(() => expect(sent).not.toBeNull())
    expect(sent).toMatchObject({
      title: 'Dune', durationMinutes: 155, classification: '+14', genreIds: [1], synopsis: null, posterUrl: null, trailerUrl: null,
      previewVideoUrl: null, releaseDate: '2030-03-10', preSaleStartsOn: null, isActive: true,
    })
    expect(await screen.findByText('Película creada.')).toBeInTheDocument()
  })

  it('muestra junto al campo el error de validación que devuelve el servidor (claves en PascalCase o camelCase)', async () => {
    setup()
    server.use(http.post(url('/movies'), () => problem(400, 'validation_failed', 'Datos inválidos', { Title: ['Ya existe una película con ese título.'] })))
    const { user } = renderApp('/movies/new')
    await user.type(await screen.findByLabelText(/Título/), 'Dune')
    await user.type(screen.getByLabelText(/Duración/), '155')
    await user.type(screen.getByLabelText(/Clasificación/), '+14')
    await user.click(screen.getByRole('checkbox', { name: 'Drama' }))
    await user.type(screen.getByLabelText(/Fecha de estreno/), '2030-03-10')
    await user.click(screen.getByRole('button', { name: 'Crear película' }))
    expect(await screen.findByText('Ya existe una película con ese título.')).toBeInTheDocument()
    expect(screen.getByLabelText(/Título/)).toHaveAttribute('aria-invalid', 'true')
  })
})
