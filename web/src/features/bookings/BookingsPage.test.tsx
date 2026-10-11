import { screen, waitFor, within } from '@testing-library/react'
import { http, HttpResponse } from 'msw'
import { describe, expect, it } from 'vitest'
import { makeBooking, paged } from '@/test/factories'
import { renderApp } from '@/test/render'
import { problem, server, url } from '@/test/server'

describe('Reservas', () => {
  it('solo permite cancelar una reserva confirmada de una función que no empezó (RN-30)', async () => {
    server.use(http.get(url('/admin/bookings'), () => HttpResponse.json(paged([
      makeBooking({ id: 1, code: 'CIN-AAA111' }),
      makeBooking({ id: 2, code: 'CIN-BBB222', status: 'Cancelled', cancelledAt: '2099-03-02T00:00:00Z' }),
      makeBooking({ id: 3, code: 'CIN-CCC333', startsAt: '2000-01-01T10:00:00.000Z' }),
    ]))))
    renderApp('/bookings')
    expect(await screen.findByRole('button', { name: 'Cancelar la reserva CIN-AAA111' })).toBeEnabled()
    expect(screen.getByRole('button', { name: 'Cancelar la reserva CIN-BBB222' })).toBeDisabled()
    expect(screen.getByRole('button', { name: 'Cancelar la reserva CIN-CCC333' })).toBeDisabled()
  })

  it('envía los filtros a la API y vuelve a la primera página al cambiarlos', async () => {
    const seen: URLSearchParams[] = []
    server.use(http.get(url('/admin/bookings'), ({ request }) => { seen.push(new URL(request.url).searchParams); return HttpResponse.json(paged([makeBooking()])) }))
    const { user } = renderApp('/bookings')
    await screen.findByText('CIN-ABC123')
    await user.selectOptions(screen.getByLabelText('Estado'), 'cancelled')
    await waitFor(() => expect(seen.at(-1)?.get('status')).toBe('cancelled'))
    expect(seen.at(-1)?.get('page')).toBe('1')
    expect(seen.at(-1)?.has('search')).toBe(false) // los filtros vacíos no viajan
  })

  it('cancela una reserva tras confirmar y avisa del resultado', async () => {
    let cancelled = 0
    server.use(
      http.get(url('/admin/bookings'), () => HttpResponse.json(paged([makeBooking({ id: 9 })]))),
      http.post(url('/bookings/9/cancel'), () => { cancelled++; return HttpResponse.json({}) }),
    )
    const { user } = renderApp('/bookings')
    await user.click(await screen.findByRole('button', { name: 'Cancelar la reserva CIN-ABC123' }))
    const dialog = await screen.findByRole('dialog', { name: 'Cancelar la reserva' })
    expect(dialog).toHaveTextContent('A1, A2')
    await user.click(within(dialog).getByRole('button', { name: 'Cancelar la reserva' }))
    await waitFor(() => expect(cancelled).toBe(1))
    expect(await screen.findByText(/se canceló y sus asientos quedaron libres/)).toBeInTheDocument()
  })

  it('si la función empezó mientras se decidía, muestra el error y cierra el diálogo', async () => {
    server.use(
      http.get(url('/admin/bookings'), () => HttpResponse.json(paged([makeBooking({ id: 9 })]))),
      http.post(url('/bookings/9/cancel'), () => problem(409, 'showtime_already_started', 'La función ya empezó.')),
    )
    const { user } = renderApp('/bookings')
    await user.click(await screen.findByRole('button', { name: 'Cancelar la reserva CIN-ABC123' }))
    await user.click(within(await screen.findByRole('dialog')).getByRole('button', { name: 'Cancelar la reserva' }))
    expect(await screen.findByText('La función ya empezó.')).toBeInTheDocument()
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument()
  })
})
