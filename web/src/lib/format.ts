export const TIME_ZONE: string = import.meta.env.VITE_CINEMA_TIMEZONE ?? 'America/Lima'

const money = new Intl.NumberFormat('es-PE', { style: 'currency', currency: 'PEN', minimumFractionDigits: 2 })
const integer = new Intl.NumberFormat('es-PE')

export const formatMoney = (value: number) => money.format(value)
export const formatInteger = (value: number) => integer.format(value)

/** 0.2537 → "25,4 %". La ocupación llega de la API como fracción de 0 a 1. */
export const formatPercent = (fraction: number) => `${(fraction * 100).toLocaleString('es-PE', { maximumFractionDigits: 1, minimumFractionDigits: 0 })} %`

const dateTime = new Intl.DateTimeFormat('es-PE', { timeZone: TIME_ZONE, day: '2-digit', month: 'short', year: 'numeric', hour: '2-digit', minute: '2-digit', hour12: false })
const dateOnly = new Intl.DateTimeFormat('es-PE', { timeZone: 'UTC', day: '2-digit', month: 'short', year: 'numeric' })

/** Un instante UTC como "10 mar 2030, 15:00" en la hora del cine. */
export const formatDateTime = (isoUtc: string) => dateTime.format(new Date(isoUtc))

/** Una fecha del calendario ("2030-03-10"), sin convertirla de zona: es el día tal cual. */
export const formatDate = (isoDate: string) => dateOnly.format(new Date(`${isoDate}T00:00:00Z`))

export const formatMinutes = (minutes: number) => `${Math.floor(minutes / 60)} h ${String(minutes % 60).padStart(2, '0')} min`
