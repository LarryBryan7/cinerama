import { screen, waitFor, within } from '@testing-library/react'
import { http, HttpResponse } from 'msw'
import { describe, expect, it } from 'vitest'
import type { ShowtimeInput } from '@/api/types'
import { makeCinema, makeMovieItem, makeRoom, makeShowtime, paged } from '@/test/factories'
import { renderApp } from '@/test/render'
import { problem, server, url } from '@/test/server'

const withShowtimes = (items = [makeShowtime()]) => server.use(http.get(url('/admin/showtimes'), () => HttpResponse.json(paged(items))))

describe('Funciones', () => {
  it('lista las funciones con su estado y bloquea la edición cuando ya hay reservas', async () => {
    withShowtimes([
      makeShowtime({ id: 1, movieTitle: 'Dune' }),
      makeShowtime({ id: 2, movieTitle: 'Barbie', hasBookings: true, confirmedBookings: 3, seatsSold: 6 }),
      makeShowtime({ id: 3, movieTitle: 'Tenet', status: 'Cancelled' }),
    ])
    renderApp('/showtimes')
    const table = await screen.findByRole('table', { name: 'Funciones' })
    const rows = within(table).getAllByRole('row').slice(1)
    expect(within(rows[0]!).getByRole('button', { name: /Editar/ })).toBeEnabled()
    expect(within(rows[1]!).getByRole('button', { name: /Editar/ })).toBeDisabled()
    expect(within(rows[1]!).getByRole('button', { name: /Cancelar/ })).toBeEnabled()
    expect(within(rows[2]!).getByText('Cancelada')).toBeInTheDocument()
    expect(within(rows[2]!).getByRole('button', { name: /Cancelar/ })).toBeDisabled()
  })

  it('muestra un estado vacío cuando no hay funciones', async () => {
    withShowtimes([])
    renderApp('/showtimes')
    expect(await screen.findByText('No hay funciones con esos filtros')).toBeInTheDocument()
  })

  it('muestra el error de la API con la opción de reintentar', async () => {
    server.use(http.get(url('/admin/showtimes'), () => problem(403, 'forbidden', 'No tienes permiso.')))
    renderApp('/showtimes')
    expect(await screen.findByRole('alert')).toHaveTextContent('No tienes permiso para hacer esto.')
  })

  async function openForm() {
    server.use(
      http.get(url('/admin/movies'), () => HttpResponse.json(paged([makeMovieItem({ id: 1, title: 'Dune' })]))),
      http.get(url('/admin/cinemas'), () => HttpResponse.json([makeCinema({ id: 1, name: 'Cinerama Centro' })])),
      http.get(url('/cinema-rooms'), () => HttpResponse.json([makeRoom({ id: 5, cinemaId: 1, name: 'Sala 1' })])),
    )
    withShowtimes([])
    const utils = renderApp('/showtimes')
    await utils.user.click(await screen.findByRole('button', { name: 'Programar función' }))
    const dialog = await screen.findByRole('dialog', { name: 'Programar función' })
    await within(dialog).findByRole('option', { name: 'Dune' })
    return { ...utils, dialog }
  }

  it('valida los campos obligatorios sin llamar a la API', async () => {
    let calls = 0
    server.use(http.post(url('/showtimes'), () => { calls++; return HttpResponse.json(makeShowtime(), { status: 201 }) }))
    const { user, dialog } = await openForm()
    await user.click(within(dialog).getByRole('button', { name: 'Guardar' }))
    expect(await within(dialog).findByText('Elige la película.')).toBeInTheDocument()
    expect(within(dialog).getByText('Elige el cine.')).toBeInTheDocument()
    expect(within(dialog).getByText('Elige el día y la hora de inicio.')).toBeInTheDocument()
    expect(calls).toBe(0)
  })

  it('la hora que escribe el administrador se envía como UTC según la zona del cine, no la del navegador', async () => {
    let sent: ShowtimeInput | null = null
    server.use(http.post(url('/showtimes'), async ({ request }) => { sent = (await request.json()) as ShowtimeInput; return HttpResponse.json(makeShowtime(), { status: 201 }) }))
    const { user, dialog } = await openForm()
    await user.selectOptions(within(dialog).getByLabelText(/Película/), 'Dune')
    await user.selectOptions(within(dialog).getByLabelText(/^Cine/), 'Cinerama Centro')
    await user.selectOptions(within(dialog).getByLabelText(/^Sala/), 'Sala 1 (12 asientos)')
    await user.type(within(dialog).getByLabelText(/Inicio/), '2099-03-10T20:00')
    await user.type(within(dialog).getByLabelText(/Precio/), '25.5')
    await user.click(within(dialog).getByRole('button', { name: 'Guardar' }))

    await waitFor(() => expect(sent).not.toBeNull())
    // 20:00 en Lima (UTC-5) = 01:00 UTC del día siguiente.
    expect(sent).toEqual({ movieId: 1, cinemaRoomId: 5, startsAt: '2099-03-11T01:00:00.000Z', price: 25.5 })
    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument())
    expect(await screen.findByText('Función programada.')).toBeInTheDocument()
  })

  it('un traslape de horario aparece junto al campo de la hora y el formulario sigue abierto', async () => {
    server.use(http.post(url('/showtimes'), () => problem(409, 'showtime_overlap', 'Se traslapa.')))
    const { user, dialog } = await openForm()
    await user.selectOptions(within(dialog).getByLabelText(/Película/), 'Dune')
    await user.selectOptions(within(dialog).getByLabelText(/^Cine/), 'Cinerama Centro')
    await user.selectOptions(within(dialog).getByLabelText(/^Sala/), 'Sala 1 (12 asientos)')
    await user.type(within(dialog).getByLabelText(/Inicio/), '2099-03-10T20:00')
    await user.type(within(dialog).getByLabelText(/Precio/), '20')
    await user.click(within(dialog).getByRole('button', { name: 'Guardar' }))
    expect(await within(dialog).findByText(/ya tiene otra función en ese horario/)).toBeInTheDocument()
    expect(screen.getByRole('dialog')).toBeInTheDocument()
  })

  it('cancelar advierte cuántas reservas confirmadas se cancelarán y llama a la API al confirmar', async () => {
    withShowtimes([makeShowtime({ id: 7, hasBookings: true, confirmedBookings: 3, seatsSold: 6 })])
    server.use(http.get(url('/admin/showtimes/7'), () => HttpResponse.json(makeShowtime({ id: 7, hasBookings: true, confirmedBookings: 3, seatsSold: 6 }))))
    let cancelled = 0
    server.use(http.post(url('/showtimes/7/cancel'), () => { cancelled++; return HttpResponse.json(makeShowtime({ id: 7, status: 'Cancelled' })) }))
    const { user } = renderApp('/showtimes')
    await user.click(await screen.findByRole('button', { name: /Cancelar la función de Dune/ }))
    const dialog = await screen.findByRole('dialog', { name: 'Cancelar la función' })
    expect(dialog).toHaveTextContent('Se cancelarán 3 reservas confirmadas')
    expect(cancelled).toBe(0)
    await user.click(within(dialog).getByRole('button', { name: 'Cancelar la función' }))
    await waitFor(() => expect(cancelled).toBe(1))
    expect(await screen.findByText('Función cancelada.')).toBeInTheDocument()
  })

  it('la advertencia usa el dato fresco: si un cliente reservó después de cargar la lista, se cuenta (defecto hallado en el recorrido real)', async () => {
    withShowtimes([makeShowtime({ id: 7, hasBookings: false, confirmedBookings: 0 })]) // la lista está vieja
    server.use(http.get(url('/admin/showtimes/7'), () => HttpResponse.json(makeShowtime({ id: 7, hasBookings: true, confirmedBookings: 2, seatsSold: 4 }))))
    const { user } = renderApp('/showtimes')
    await user.click(await screen.findByRole('button', { name: /Cancelar la función de Dune/ }))
    const dialog = await screen.findByRole('dialog', { name: 'Cancelar la función' })
    expect(await within(dialog).findByText(/Se cancelarán/)).toHaveTextContent('2 reservas confirmadas')
    expect(dialog).not.toHaveTextContent('No tiene reservas confirmadas')
  })

  it('no se puede confirmar la cancelación mientras no se conoce el número de reservas', async () => {
    withShowtimes([makeShowtime({ id: 7 })])
    server.use(http.get(url('/admin/showtimes/7'), () => problem(500, 'internal_error')))
    const { user } = renderApp('/showtimes')
    await user.click(await screen.findByRole('button', { name: /Cancelar la función de Dune/ }))
    const dialog = await screen.findByRole('dialog', { name: 'Cancelar la función' })
    await within(dialog).findByRole('alert')
    expect(within(dialog).getByRole('button', { name: 'Cancelar la función' })).toBeDisabled()
  })

  it('"Cancelar" del diálogo (volver) no cancela la función', async () => {
    withShowtimes([makeShowtime({ id: 7 })])
    server.use(http.get(url('/admin/showtimes/7'), () => HttpResponse.json(makeShowtime({ id: 7 }))))
    let cancelled = 0
    server.use(http.post(url('/showtimes/7/cancel'), () => { cancelled++; return HttpResponse.json(makeShowtime()) }))
    const { user } = renderApp('/showtimes')
    await user.click(await screen.findByRole('button', { name: /Cancelar la función de Dune/ }))
    const dialog = await screen.findByRole('dialog')
    await user.click(within(dialog).getByRole('button', { name: 'Cancelar' }))
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument()
    expect(cancelled).toBe(0)
  })
})
