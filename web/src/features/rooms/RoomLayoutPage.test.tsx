import { screen, waitFor } from '@testing-library/react'
import { http, HttpResponse } from 'msw'
import { describe, expect, it } from 'vitest'
import type { RoomLayout } from '@/api/types'
import { makeRoom } from '@/test/factories'
import { renderApp } from '@/test/render'
import { problem, server, url } from '@/test/server'

const seatsOf = (rows: number, positions: number, empty: string[] = []) => ({
  roomId: 5, roomName: 'Sala 1', rows, positions, emptyCells: empty,
  seats: Array.from({ length: rows }, (_, r) => Array.from({ length: positions }, (_, p) => ({ id: r * 100 + p, row: String.fromCharCode(65 + r), number: p + 1 }))).flat()
    .filter((s) => !empty.includes(`${s.row}${s.number}`)),
})

const setup = (empty: string[] = [], upcoming = 0) =>
  server.use(
    http.get(url('/cinema-rooms/5/seats'), () => HttpResponse.json(seatsOf(2, 4, empty))),
    http.get(url('/cinema-rooms'), () => HttpResponse.json([makeRoom({ id: 5, upcomingShowtimes: upcoming })])),
  )

describe('Distribución de asientos', () => {
  it('dibuja la cuadrícula y marca los pasillos', async () => {
    setup(['A3', 'B3'])
    renderApp('/rooms/5/layout')
    expect(await screen.findByRole('gridcell', { name: 'A1: asiento' })).toBeInTheDocument()
    expect(screen.getByRole('gridcell', { name: 'A3: pasillo' })).toHaveAttribute('aria-pressed', 'false')
    expect(screen.getAllByRole('gridcell')).toHaveLength(8)
  })

  it('guardar queda deshabilitado hasta que hay cambios y envía las celdas vacías', async () => {
    setup()
    let sent: RoomLayout | null = null
    server.use(http.put(url('/cinema-rooms/5/layout'), async ({ request }) => { sent = (await request.json()) as RoomLayout; return HttpResponse.json(seatsOf(2, 4, ['A2'])) }))
    const { user } = renderApp('/rooms/5/layout')
    const save = await screen.findByRole('button', { name: 'Guardar distribución' })
    expect(save).toBeDisabled()
    await user.click(screen.getByRole('gridcell', { name: 'A2: asiento' }))
    expect(save).toBeEnabled()
    await user.click(save)
    await waitFor(() => expect(sent).toEqual({ rows: 2, positions: 4, emptyCells: ['A2'] }))
  })

  it('"Hacer pasillo" vacía la columna elegida', async () => {
    setup()
    const { user } = renderApp('/rooms/5/layout')
    await user.type(await screen.findByLabelText('Pasillo en la posición'), '3')
    await user.click(screen.getByRole('button', { name: 'Hacer pasillo' }))
    expect(screen.getByRole('gridcell', { name: 'A3: pasillo' })).toBeInTheDocument()
    expect(screen.getByRole('gridcell', { name: 'B3: pasillo' })).toBeInTheDocument()
    expect(screen.getByRole('gridcell', { name: 'A2: asiento' })).toBeInTheDocument()
  })

  it('se navega con las flechas del teclado (un solo asiento en el orden de tabulación)', async () => {
    setup()
    const { user } = renderApp('/rooms/5/layout')
    const first = await screen.findByRole('gridcell', { name: 'A1: asiento' })
    expect(screen.getAllByRole('gridcell').filter((c) => c.tabIndex === 0)).toHaveLength(1)
    first.focus()
    await user.keyboard('{ArrowRight}')
    expect(screen.getByRole('gridcell', { name: 'A2: asiento' })).toHaveFocus()
    await user.keyboard('{ArrowDown}')
    expect(screen.getByRole('gridcell', { name: 'B2: asiento' })).toHaveFocus()
  })

  it('con funciones futuras la distribución queda bloqueada y se explica por qué', async () => {
    setup([], 2)
    renderApp('/rooms/5/layout')
    expect(await screen.findByRole('alert')).toHaveTextContent('2 funciones futuras')
    expect(screen.getByRole('button', { name: 'Guardar distribución' })).toBeDisabled()
    expect(screen.getByRole('button', { name: 'Hacer pasillo' })).toBeDisabled()
  })

  it('si la API rechaza el cambio (409) avisa y conserva lo editado', async () => {
    setup()
    server.use(http.put(url('/cinema-rooms/5/layout'), () => problem(409, 'room_has_future_showtimes', 'La sala tiene funciones futuras.')))
    const { user } = renderApp('/rooms/5/layout')
    await user.click(await screen.findByRole('gridcell', { name: 'A2: asiento' }))
    await user.click(screen.getByRole('button', { name: 'Guardar distribución' }))
    expect(await screen.findByText(/La sala tiene funciones futuras/)).toBeInTheDocument()
    expect(screen.getByRole('gridcell', { name: 'A2: pasillo' })).toBeInTheDocument()
  })
})
