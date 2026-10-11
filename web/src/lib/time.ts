import { TIME_ZONE } from './format'

/**
 * Convierte "2030-03-10T15:00" (la hora que escribe el administrador, en la zona del cine) en el instante UTC ISO que espera la API
 * ("2030-03-10T20:00:00.000Z"). La API rechaza una hora sin zona por ambigua (RN-38), y el navegador del administrador puede estar en
 * otra zona: por eso no se usa la zona del navegador (`new Date("2030-03-10T15:00")`), sino la del cine.
 */
export function cinemaLocalToUtcIso(local: string, timeZone: string = TIME_ZONE): string {
  const match = /^(\d{4})-(\d{2})-(\d{2})T(\d{2}):(\d{2})$/.exec(local)
  if (!match) throw new Error(`Fecha y hora inválidas: ${local}`)
  const [, y, mo, d, h, mi] = match.map(Number) as [number, number, number, number, number, number]
  const asUtc = Date.UTC(y, mo - 1, d, h, mi)

  // El desfase de la zona en ese momento. Se aproxima con el instante "como si fuera UTC" y se corrige una vez (cubre los cambios de horario).
  let guess = asUtc - offsetMinutes(asUtc, timeZone) * 60_000
  guess = asUtc - offsetMinutes(guess, timeZone) * 60_000
  return new Date(guess).toISOString()
}

/** El inverso: un instante UTC ISO como "2030-03-10T15:00" en la zona del cine (para llenar un campo datetime-local). */
export function utcIsoToCinemaLocal(isoUtc: string, timeZone: string = TIME_ZONE): string {
  const parts = new Intl.DateTimeFormat('en-CA', {
    timeZone, year: 'numeric', month: '2-digit', day: '2-digit', hour: '2-digit', minute: '2-digit', hourCycle: 'h23',
  }).formatToParts(new Date(isoUtc))
  const get = (type: string) => parts.find((p) => p.type === type)?.value ?? '00'
  return `${get('year')}-${get('month')}-${get('day')}T${get('hour')}:${get('minute')}`
}

/** Minutos que la zona está por delante (+) o por detrás (−) de UTC en el instante dado. Lima: −300. */
function offsetMinutes(utcMs: number, timeZone: string): number {
  const parts = new Intl.DateTimeFormat('en-CA', {
    timeZone, year: 'numeric', month: '2-digit', day: '2-digit', hour: '2-digit', minute: '2-digit', second: '2-digit', hourCycle: 'h23',
  }).formatToParts(new Date(utcMs))
  const n = (type: string) => Number(parts.find((p) => p.type === type)?.value)
  const asIfUtc = Date.UTC(n('year'), n('month') - 1, n('day'), n('hour'), n('minute'), n('second'))
  return Math.round((asIfUtc - Math.floor(utcMs / 1000) * 1000) / 60_000)
}

/** Hoy en la zona del cine, como "yyyy-MM-dd". */
export function cinemaToday(now: Date = new Date(), timeZone: string = TIME_ZONE): string {
  return utcIsoToCinemaLocal(now.toISOString(), timeZone).slice(0, 10)
}

/** Suma días a una fecha "yyyy-MM-dd" sin pasar por zonas horarias. */
export function addDays(isoDate: string, days: number): string {
  const [y, m, d] = isoDate.split('-').map(Number) as [number, number, number]
  return new Date(Date.UTC(y, m - 1, d + days)).toISOString().slice(0, 10)
}
