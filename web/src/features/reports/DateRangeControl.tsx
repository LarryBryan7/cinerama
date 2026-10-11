import { useState } from 'react'
import type { DateRange } from '@/api/types'
import { Button } from '@/components/ui/Button'
import { Field, Input } from '@/components/ui/Form'
import { addDays, cinemaToday } from '@/lib/time'

/** Los últimos 30 días, contando hoy (en la hora del cine). */
export const defaultRange = (): DateRange => {
  const today = cinemaToday()
  return { from: addDays(today, -29), to: today }
}

const MAX_DAYS = 366

/** Valida el rango igual que la API (RN de reportes): ambas fechas, desde ≤ hasta y como máximo 366 días. */
export function validateRange({ from, to }: DateRange): string | null {
  if (!from || !to) return 'Elige las dos fechas.'
  if (from > to) return 'La fecha inicial no puede ser posterior a la final.'
  const days = (Date.parse(`${to}T00:00:00Z`) - Date.parse(`${from}T00:00:00Z`)) / 86_400_000 + 1
  if (days > MAX_DAYS) return `El rango máximo es de ${MAX_DAYS} días.`
  return null
}

/** Selector de periodo: el rango se aplica con el botón, así un rango a medio escribir no dispara consultas inválidas. */
export function DateRangeControl({ value, onApply }: { value: DateRange; onApply: (range: DateRange) => void }) {
  const [draft, setDraft] = useState(value)
  const error = validateRange(draft)
  const preset = (days: number) => {
    const today = cinemaToday()
    const range = { from: addDays(today, -(days - 1)), to: today }
    setDraft(range)
    onApply(range)
  }

  return (
    <form
      className="mb-6 flex flex-wrap items-end gap-3" aria-label="Periodo del reporte"
      onSubmit={(e) => { e.preventDefault(); if (!error) onApply(draft) }}
    >
      <Field label="Desde">{(p) => <Input {...p} type="date" value={draft.from} onChange={(e) => setDraft({ ...draft, from: e.target.value })} />}</Field>
      <Field label="Hasta" error={error ?? undefined}>{(p) => <Input {...p} type="date" value={draft.to} onChange={(e) => setDraft({ ...draft, to: e.target.value })} />}</Field>
      <Button type="submit" disabled={Boolean(error)}>Aplicar</Button>
      <div className="flex gap-2">
        <Button type="button" variant="ghost" size="sm" onClick={() => preset(7)}>7 días</Button>
        <Button type="button" variant="ghost" size="sm" onClick={() => preset(30)}>30 días</Button>
        <Button type="button" variant="ghost" size="sm" onClick={() => preset(90)}>90 días</Button>
      </div>
    </form>
  )
}
