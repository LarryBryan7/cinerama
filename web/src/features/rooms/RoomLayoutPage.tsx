import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { memo, useMemo, useState, type KeyboardEvent } from 'react'
import { Link, useParams } from 'react-router-dom'
import { rooms as api } from '@/api/endpoints'
import type { RoomSeats } from '@/api/types'
import { Button } from '@/components/ui/Button'
import { cn } from '@/components/ui/cn'
import { ErrorState, PageHeader, Spinner } from '@/components/ui/Feedback'
import { Field, Input } from '@/components/ui/Form'
import { useToast } from '@/components/ui/Toast'

const MAX_ROWS = 26
const MAX_POSITIONS = 40

const label = (row: number, position: number) => `${String.fromCharCode(65 + row)}${position}`
const sorted = (cells: Iterable<string>) => [...cells].sort((a, b) => a.charCodeAt(0) - b.charCodeAt(0) || Number(a.slice(1)) - Number(b.slice(1)))

export default function RoomLayoutPage() {
  const { id } = useParams()
  const roomId = Number(id)
  const seats = useQuery({ queryKey: ['room-seats', roomId], queryFn: () => api.seats(roomId) })
  const rooms = useQuery({ queryKey: ['rooms', 'all'], queryFn: () => api.list() })

  if (seats.isError || rooms.isError) return <ErrorState error={seats.error ?? rooms.error} onRetry={() => { void seats.refetch(); void rooms.refetch() }} />
  if (!seats.isSuccess || !rooms.isSuccess) return <Spinner />

  const room = rooms.data.find((r) => r.id === roomId)
  return <LayoutEditor key={seats.dataUpdatedAt} data={seats.data} roomName={room?.name ?? seats.data.roomName} cinemaName={room?.cinemaName} futureShowtimes={room?.upcomingShowtimes ?? 0} />
}

function LayoutEditor({ data, roomName, cinemaName, futureShowtimes }: { data: RoomSeats; roomName: string; cinemaName?: string; futureShowtimes: number }) {
  const client = useQueryClient()
  const toast = useToast()
  const [rows, setRows] = useState(Math.max(data.rows, 1))
  const [positions, setPositions] = useState(Math.max(data.positions, 1))
  const [empty, setEmpty] = useState<Set<string>>(() => new Set(data.emptyCells))
  const [aisle, setAisle] = useState('')
  const [focused, setFocused] = useState('A1')
  const locked = futureShowtimes > 0

  // Solo cuentan las celdas que siguen dentro de la cuadrícula (al achicarla, las de afuera dejan de importar).
  const emptyInside = useMemo(() => sorted([...empty].filter((c) => c.charCodeAt(0) - 65 < rows && Number(c.slice(1)) <= positions)), [empty, rows, positions])
  const seatCount = rows * positions - emptyInside.length
  const gridValid = Number.isInteger(rows) && rows >= 1 && rows <= MAX_ROWS && Number.isInteger(positions) && positions >= 1 && positions <= MAX_POSITIONS
  const dirty = rows !== data.rows || positions !== data.positions || emptyInside.join() !== sorted(data.emptyCells).join()

  const save = useMutation({
    mutationFn: () => api.updateLayout(data.roomId, { rows, positions, emptyCells: emptyInside }),
    onSuccess: async () => {
      await Promise.all([client.invalidateQueries({ queryKey: ['room-seats', data.roomId] }), client.invalidateQueries({ queryKey: ['rooms'] })])
      toast.success('Distribución guardada.')
    },
    onError: async (error) => {
      toast.error(error) // room_has_future_showtimes: alguien programó una función mientras se editaba
      await client.invalidateQueries({ queryKey: ['rooms'] })
    },
  })

  const toggle = (cell: string) => {
    if (locked) return
    setEmpty((current) => {
      const next = new Set(current)
      if (!next.delete(cell)) next.add(cell)
      return next
    })
  }

  const applyAisle = () => {
    const column = Number(aisle)
    if (!Number.isInteger(column) || column < 1 || column > positions) return
    setEmpty((current) => {
      const next = new Set(current)
      for (let r = 0; r < rows; r++) next.add(label(r, column))
      return next
    })
  }

  // Flechas del teclado: mover el foco por la cuadrícula (si no, recorrer 1000 asientos con Tab sería inviable).
  const onKeyDown = (event: KeyboardEvent<HTMLDivElement>) => {
    const match = /^cell-([A-Z])(\d+)$/.exec((event.target as HTMLElement).id)
    if (!match) return
    const row = match[1]!.charCodeAt(0) - 65
    const position = Number(match[2])
    const move = { ArrowRight: [0, 1], ArrowLeft: [0, -1], ArrowDown: [1, 0], ArrowUp: [-1, 0] }[event.key]
    if (!move) return
    event.preventDefault()
    const r = Math.min(Math.max(row + move[0]!, 0), rows - 1)
    const p = Math.min(Math.max(position + move[1]!, 1), positions)
    setFocused(label(r, p))
    document.getElementById(`cell-${label(r, p)}`)?.focus()
  }

  return (
    <>
      <PageHeader
        title={`Distribución de ${roomName}`} subtitle={cinemaName ? `${cinemaName}. Toca un asiento para convertirlo en pasillo, y otra vez para volver a ponerlo.` : 'Toca un asiento para convertirlo en pasillo.'}
        actions={<Link to="/rooms" className="inline-flex min-h-11 items-center rounded-lg border border-slate-300 bg-white px-4 text-sm hover:bg-slate-50">Volver a las salas</Link>}
      />

      {locked && (
        <p role="alert" className="mb-4 rounded-lg bg-amber-50 p-3 text-sm font-medium text-amber-900">
          La sala tiene {futureShowtimes} {futureShowtimes === 1 ? 'función futura' : 'funciones futuras'}: su distribución no se puede cambiar hasta cancelarlas.
        </p>
      )}

      <div className="mb-4 grid max-w-3xl gap-3 sm:grid-cols-4">
        <Field label="Filas" hint="De 1 a 26" error={rows < 1 || rows > MAX_ROWS || !Number.isInteger(rows) ? 'Entre 1 y 26.' : undefined}>
          {(p) => <Input {...p} type="number" inputMode="numeric" value={Number.isNaN(rows) ? '' : rows} disabled={locked} onChange={(e) => setRows(e.target.valueAsNumber)} />}
        </Field>
        <Field label="Posiciones" hint="De 1 a 40" error={positions < 1 || positions > MAX_POSITIONS || !Number.isInteger(positions) ? 'Entre 1 y 40.' : undefined}>
          {(p) => <Input {...p} type="number" inputMode="numeric" value={Number.isNaN(positions) ? '' : positions} disabled={locked} onChange={(e) => setPositions(e.target.valueAsNumber)} />}
        </Field>
        <Field label="Pasillo en la posición" hint="Vacía esa columna">
          {(p) => <Input {...p} inputMode="numeric" value={aisle} disabled={locked} onChange={(e) => setAisle(e.target.value)} />}
        </Field>
        <div className="flex items-end gap-2">
          <Button variant="secondary" onClick={applyAisle} disabled={locked}>Hacer pasillo</Button>
          <Button variant="ghost" onClick={() => setEmpty(new Set())} disabled={locked}>Sin pasillos</Button>
        </div>
      </div>

      {gridValid ? (
        <div className="overflow-x-auto rounded-xl border border-slate-200 bg-white p-4">
          <div className="mx-auto w-max">
            <div aria-hidden className="mb-4 rounded-md bg-slate-300 py-1 text-center text-xs font-medium uppercase tracking-widest text-slate-600">Pantalla</div>
            <div role="grid" aria-label={`Asientos de ${roomName}`} onKeyDown={onKeyDown} className="flex flex-col gap-1">
              {Array.from({ length: rows }, (_, r) => (
                <div role="row" key={r} className="flex items-center gap-1">
                  <span aria-hidden className="w-6 text-right text-xs font-medium text-slate-500">{String.fromCharCode(65 + r)}</span>
                  {Array.from({ length: positions }, (_, i) => {
                    const cell = label(r, i + 1)
                    return <SeatCell key={cell} cell={cell} number={i + 1} isSeat={!empty.has(cell)} tabbable={focused === cell} disabled={locked} onToggle={toggle} onFocus={setFocused} />
                  })}
                </div>
              ))}
            </div>
          </div>
        </div>
      ) : (
        <p role="alert" className="rounded-lg bg-red-50 p-3 text-sm text-red-800">Corrige las filas y las posiciones para ver la cuadrícula.</p>
      )}

      <div className="mt-4 flex flex-wrap items-center gap-3">
        <p aria-live="polite" className="text-sm text-slate-700"><strong>{gridValid ? seatCount : 0}</strong> asientos · {emptyInside.length} {emptyInside.length === 1 ? 'celda' : 'celdas'} de pasillo</p>
        {gridValid && seatCount === 0 && <p role="alert" className="text-sm font-medium text-red-700">La sala necesita al menos un asiento.</p>}
        <div className="ml-auto flex gap-2">
          <Button variant="secondary" disabled={!dirty || locked} onClick={() => { setRows(Math.max(data.rows, 1)); setPositions(Math.max(data.positions, 1)); setEmpty(new Set(data.emptyCells)) }}>Descartar cambios</Button>
          <Button onClick={() => save.mutate()} loading={save.isPending} disabled={!dirty || locked || !gridValid || seatCount === 0}>Guardar distribución</Button>
        </div>
      </div>
    </>
  )
}

interface SeatCellProps {
  cell: string
  number: number
  isSeat: boolean
  tabbable: boolean
  disabled: boolean
  onToggle: (cell: string) => void
  onFocus: (cell: string) => void
}

const SeatCell = memo(function SeatCell({ cell, number, isSeat, tabbable, disabled, onToggle, onFocus }: SeatCellProps) {
  return (
    <button
      type="button"
      role="gridcell"
      id={`cell-${cell}`}
      aria-label={`${cell}: ${isSeat ? 'asiento' : 'pasillo'}`}
      aria-pressed={isSeat}
      tabIndex={tabbable ? 0 : -1}
      disabled={disabled}
      onClick={() => onToggle(cell)}
      onFocus={() => onFocus(cell)}
      className={cn(
        'flex size-8 shrink-0 items-center justify-center rounded text-[10px] font-medium transition-colors disabled:cursor-not-allowed',
        isSeat ? 'bg-panel text-lime hover:bg-panel-2' : 'border border-dashed border-slate-300 bg-slate-50 text-slate-300 hover:border-slate-400',
      )}
    >
      {number}
    </button>
  )
})
