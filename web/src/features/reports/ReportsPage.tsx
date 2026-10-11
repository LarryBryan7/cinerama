import { useQuery } from '@tanstack/react-query'
import { useState } from 'react'
import { Bar, BarChart, CartesianGrid, Line, LineChart, ResponsiveContainer, Tooltip, XAxis, YAxis } from 'recharts'
import { reports } from '@/api/endpoints'
import type { DateRange } from '@/api/types'
import { EmptyState, ErrorState, PageHeader, Spinner } from '@/components/ui/Feedback'
import { Select } from '@/components/ui/Form'
import { Pagination } from '@/components/ui/Pagination'
import { Table, Td, Th, Thead, Tr } from '@/components/ui/Table'
import { formatDate, formatDateTime, formatInteger, formatMoney, formatPercent } from '@/lib/format'
import { DateRangeControl, defaultRange } from './DateRangeControl'

const PAGE_SIZE = 20

export default function ReportsPage() {
  const [range, setRange] = useState<DateRange>(defaultRange)

  return (
    <>
      <PageHeader title="Reportes" subtitle="Ventas y ocupación. Cada día es el día de la función en la hora del cine; solo cuentan las reservas confirmadas." />
      <DateRangeControl value={range} onApply={setRange} />
      <SummaryCards range={range} />
      <div className="mt-6 grid gap-6 2xl:grid-cols-2">
        <DailyChart range={range} />
        <MovieSalesTable range={range} />
      </div>
      <OccupancyTable range={range} />
    </>
  )
}

export function SummaryCards({ range }: { range: DateRange }) {
  const query = useQuery({ queryKey: ['reports', 'summary', range], queryFn: () => reports.summary(range) })
  if (query.isPending) return <Spinner />
  if (query.isError) return <ErrorState error={query.error} onRetry={() => void query.refetch()} />
  const s = query.data
  const cards = [
    ['Ingresos', formatMoney(s.revenue)],
    ['Reservas', formatInteger(s.bookings)],
    ['Entradas vendidas', formatInteger(s.ticketsSold)],
    ['Ticket promedio', formatMoney(s.averageTicket)],
    ['Ocupación promedio', formatPercent(s.averageOccupancy)],
    ['Funciones', formatInteger(s.showtimes)],
  ] as const
  return (
    <dl className="grid grid-cols-2 gap-3 lg:grid-cols-3 xl:grid-cols-6" aria-label="Resumen del periodo">
      {cards.map(([label, value]) => (
        <div key={label} className="rounded-xl border border-slate-200 bg-white p-4">
          <dt className="text-xs uppercase tracking-wide text-slate-500">{label}</dt>
          <dd className="mt-1 text-xl font-semibold text-slate-900">{value}</dd>
        </div>
      ))}
    </dl>
  )
}

function DailyChart({ range }: { range: DateRange }) {
  const query = useQuery({ queryKey: ['reports', 'daily', range], queryFn: () => reports.daily(range) })
  return (
    <section aria-labelledby="daily-title" className="rounded-xl border border-slate-200 bg-white p-4">
      <h2 id="daily-title" className="mb-3 font-semibold">Ingresos por día</h2>
      {query.isPending && <Spinner />}
      {query.isError && <ErrorState error={query.error} onRetry={() => void query.refetch()} />}
      {query.data && query.data.every((d) => d.revenue === 0) && <EmptyState title="Sin ventas en el periodo" />}
      {query.data && query.data.some((d) => d.revenue > 0) && (
        <>
          <div className="h-64" role="img" aria-label={`Ingresos diarios de ${formatDate(range.from)} a ${formatDate(range.to)}`}>
            <ResponsiveContainer width="100%" height="100%">
              <LineChart data={query.data} margin={{ left: 8, right: 8, top: 8 }}>
                <CartesianGrid strokeDasharray="3 3" stroke="#e2e8f0" />
                <XAxis dataKey="date" tickFormatter={(d: string) => d.slice(5)} fontSize={12} />
                <YAxis fontSize={12} width={56} tickFormatter={(v: number) => formatInteger(v)} />
                <Tooltip formatter={(v) => formatMoney(Number(v))} labelFormatter={(l) => formatDate(String(l))} />
                <Line type="monotone" dataKey="revenue" name="Ingresos" stroke="#0369a1" strokeWidth={2} dot={false} />
              </LineChart>
            </ResponsiveContainer>
          </div>
          {/* Los gráficos no son accesibles por sí solos: la misma información va en una tabla para lector de pantalla. */}
          <Table caption="Ingresos por día">
            <Thead><Tr><Th>Día</Th><Th>Ingresos</Th><Th>Entradas</Th><Th>Ocupación</Th></Tr></Thead>
            <tbody>
              {query.data.filter((d) => d.showtimes > 0).map((d) => (
                <Tr key={d.date}><Td>{formatDate(d.date)}</Td><Td>{formatMoney(d.revenue)}</Td><Td>{formatInteger(d.ticketsSold)}</Td><Td>{formatPercent(d.occupancy)}</Td></Tr>
              ))}
            </tbody>
          </Table>
        </>
      )}
    </section>
  )
}

function MovieSalesTable({ range }: { range: DateRange }) {
  const query = useQuery({ queryKey: ['reports', 'by-movie', range], queryFn: () => reports.byMovie(range) })
  return (
    <section aria-labelledby="movies-title" className="rounded-xl border border-slate-200 bg-white p-4">
      <h2 id="movies-title" className="mb-3 font-semibold">Ventas por película</h2>
      {query.isPending && <Spinner />}
      {query.isError && <ErrorState error={query.error} onRetry={() => void query.refetch()} />}
      {query.data && query.data.length === 0 && <EmptyState title="Sin funciones en el periodo" />}
      {query.data && query.data.length > 0 && (
        <>
          <div className="h-56" role="img" aria-label="Ingresos por película">
            <ResponsiveContainer width="100%" height="100%">
              <BarChart data={query.data.slice(0, 8)} layout="vertical" margin={{ left: 8, right: 8 }}>
                <CartesianGrid strokeDasharray="3 3" stroke="#e2e8f0" />
                <XAxis type="number" fontSize={12} tickFormatter={(v: number) => formatInteger(v)} />
                <YAxis type="category" dataKey="title" width={110} fontSize={11} />
                <Tooltip formatter={(v) => formatMoney(Number(v))} />
                <Bar dataKey="revenue" name="Ingresos" fill="#0369a1" />
              </BarChart>
            </ResponsiveContainer>
          </div>
          <Table caption="Ventas por película">
            <Thead><Tr><Th>Película</Th><Th>Ingresos</Th><Th>Entradas</Th><Th>Ocupación</Th></Tr></Thead>
            <tbody>
              {query.data.map((m) => (
                <Tr key={m.movieId}><Td className="font-medium">{m.title}</Td><Td>{formatMoney(m.revenue)}</Td><Td>{formatInteger(m.ticketsSold)}</Td><Td>{formatPercent(m.occupancy)}</Td></Tr>
              ))}
            </tbody>
          </Table>
        </>
      )}
    </section>
  )
}

function OccupancyTable({ range }: { range: DateRange }) {
  const [groupBy, setGroupBy] = useState<'showtime' | 'room'>('showtime')
  const [page, setPage] = useState(1)
  const query = useQuery({
    queryKey: ['reports', 'occupancy', range, groupBy, page],
    queryFn: () => reports.occupancy(range, groupBy, page, PAGE_SIZE),
    placeholderData: (previous) => previous,
  })

  return (
    <section aria-labelledby="occupancy-title" className="mt-6 rounded-xl border border-slate-200 bg-white p-4">
      <div className="mb-3 flex flex-wrap items-center justify-between gap-3">
        <h2 id="occupancy-title" className="font-semibold">Ocupación</h2>
        <Select aria-label="Agrupar ocupación por" value={groupBy} onChange={(e) => { setGroupBy(e.target.value as 'showtime' | 'room'); setPage(1) }} className="w-auto">
          <option value="showtime">Por función</option>
          <option value="room">Por sala</option>
        </Select>
      </div>
      {query.isPending && <Spinner />}
      {query.isError && <ErrorState error={query.error} onRetry={() => void query.refetch()} />}
      {query.data && query.data.items.length === 0 && <EmptyState title="Sin funciones en el periodo" />}
      {query.data && query.data.items.length > 0 && (
        <>
          <Table caption="Ocupación">
            <Thead>
              <Tr>
                <Th>{groupBy === 'room' ? 'Sala' : 'Película'}</Th>
                <Th>{groupBy === 'room' ? 'Cine' : 'Sala · inicio'}</Th>
                {groupBy === 'room' && <Th>Funciones</Th>}
                <Th>Vendidos</Th><Th>Ocupación</Th>
              </Tr>
            </Thead>
            <tbody>
              {query.data.items.map((r) => (
                <Tr key={r.id}>
                  <Td className="font-medium">{r.title}</Td>
                  <Td>{groupBy === 'room' ? r.subtitle : `${r.subtitle ?? ''}${r.startsAt ? ` · ${formatDateTime(r.startsAt)}` : ''}`}</Td>
                  {groupBy === 'room' && <Td>{formatInteger(r.showtimes)}</Td>}
                  <Td>{formatInteger(r.seatsSold)} / {formatInteger(r.capacity)}</Td>
                  <Td>{formatPercent(r.occupancy)}</Td>
                </Tr>
              ))}
            </tbody>
          </Table>
          <Pagination page={page} pageSize={PAGE_SIZE} totalCount={query.data.totalCount} onPage={setPage} />
        </>
      )}
    </section>
  )
}
