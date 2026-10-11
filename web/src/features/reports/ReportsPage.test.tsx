import { screen, waitFor, within } from '@testing-library/react'
import { http, HttpResponse } from 'msw'
import { describe, expect, it } from 'vitest'
import { paged } from '@/test/factories'
import { renderApp } from '@/test/render'
import { problem, server, url } from '@/test/server'

const summary = { from: '2030-01-01', to: '2030-01-31', revenue: 1234.5, bookings: 20, ticketsSold: 55, averageTicket: 61.73, averageOccupancy: 0.4237, showtimes: 9 }

const setup = () =>
  server.use(
    http.get(url('/reports/summary'), () => HttpResponse.json(summary)),
    http.get(url('/reports/sales-daily'), () => HttpResponse.json([
      { date: '2030-01-05', revenue: 500, bookings: 8, ticketsSold: 20, showtimes: 3, occupancy: 0.5 },
      { date: '2030-01-06', revenue: 0, bookings: 0, ticketsSold: 0, showtimes: 0, occupancy: 0 },
    ])),
    http.get(url('/reports/sales-by-movie'), () => HttpResponse.json([{ movieId: 1, title: 'Dune', revenue: 900, bookings: 12, ticketsSold: 30, showtimes: 5, occupancy: 0.6 }])),
    http.get(url('/reports/occupancy'), () => HttpResponse.json(paged([{ id: 1, title: 'Dune', subtitle: 'Sala 1', startsAt: '2030-01-05T20:00:00.000Z', capacity: 100, seatsSold: 25, occupancy: 0.25, showtimes: 1 }]))),
  )

describe('Reportes', () => {
  it('muestra el resumen formateado, las ventas por película y la ocupación', async () => {
    setup()
    renderApp('/reports')
    const cards = await screen.findByLabelText('Resumen del periodo')
    expect(cards).toHaveTextContent(/1,234\.50/)
    expect(cards).toHaveTextContent('42.4 %')
    const movies = await screen.findByRole('table', { name: 'Ventas por película' })
    expect(within(movies).getByText('Dune')).toBeInTheDocument()
    expect(within(await screen.findByRole('table', { name: 'Ocupación' })).getByText('25 / 100')).toBeInTheDocument()
  })

  it('la tabla accesible de ingresos diarios omite los días sin funciones', async () => {
    setup()
    renderApp('/reports')
    const table = await screen.findByRole('table', { name: 'Ingresos por día' })
    expect(within(table).getAllByRole('row')).toHaveLength(2) // encabezado + el único día con funciones
  })

  it('cambia la ocupación a "por sala" y pide groupBy=room', async () => {
    setup()
    const seen: string[] = []
    server.use(http.get(url('/reports/occupancy'), ({ request }) => { seen.push(new URL(request.url).searchParams.get('groupBy')!); return HttpResponse.json(paged([])) }))
    const { user } = renderApp('/reports')
    await user.selectOptions(await screen.findByLabelText('Agrupar ocupación por'), 'room')
    await waitFor(() => expect(seen).toContain('room'))
  })

  it('un periodo sin ventas muestra el estado vacío, no un gráfico en cero', async () => {
    setup()
    server.use(http.get(url('/reports/sales-daily'), () => HttpResponse.json([{ date: '2030-01-05', revenue: 0, bookings: 0, ticketsSold: 0, showtimes: 0, occupancy: 0 }])))
    renderApp('/reports')
    expect(await screen.findByText('Sin ventas en el periodo')).toBeInTheDocument()
  })

  it('si un reporte falla, solo esa sección muestra el error y el resto sigue', async () => {
    setup()
    server.use(http.get(url('/reports/sales-by-movie'), () => problem(500, 'internal_error')))
    renderApp('/reports')
    expect(await screen.findByText('El servidor tuvo un problema. Inténtalo de nuevo en un momento.')).toBeInTheDocument()
    expect(await screen.findByRole('table', { name: 'Ocupación' })).toBeInTheDocument()
  })
})
