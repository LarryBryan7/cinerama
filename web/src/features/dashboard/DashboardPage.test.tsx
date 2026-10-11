import { screen } from '@testing-library/react'
import { http, HttpResponse } from 'msw'
import { describe, expect, it } from 'vitest'
import { makeShowtime, paged } from '@/test/factories'
import { renderApp } from '@/test/render'
import { server, url } from '@/test/server'

describe('Dashboard', () => {
  it('muestra las ventas de hoy y las próximas funciones', async () => {
    server.use(
      http.get(url('/reports/summary'), () => HttpResponse.json({ from: '2030-01-01', to: '2030-01-01', revenue: 100, bookings: 2, ticketsSold: 4, averageTicket: 50, averageOccupancy: 0.1, showtimes: 3 })),
      http.get(url('/admin/showtimes'), () => HttpResponse.json(paged([makeShowtime({ movieTitle: 'Tenet' })]))),
    )
    renderApp('/')
    expect(await screen.findByText('Hola, Ana')).toBeInTheDocument()
    expect(await screen.findByText('Tenet')).toBeInTheDocument()
    expect(await screen.findByLabelText('Resumen del periodo')).toHaveTextContent('Entradas vendidas')
  })

  it('sin funciones próximas lo dice con claridad', async () => {
    server.use(
      http.get(url('/reports/summary'), () => HttpResponse.json({ from: '', to: '', revenue: 0, bookings: 0, ticketsSold: 0, averageTicket: 0, averageOccupancy: 0, showtimes: 0 })),
      http.get(url('/admin/showtimes'), () => HttpResponse.json(paged([]))),
    )
    renderApp('/')
    expect(await screen.findByText(/No hay funciones programadas para hoy ni mañana/)).toBeInTheDocument()
  })
})
