import { describe, expect, it } from 'vitest'
import { addDays, cinemaLocalToUtcIso, cinemaToday, utcIsoToCinemaLocal } from './time'

describe('hora del cine', () => {
  it('convierte la hora de Lima (UTC-5) a UTC sin depender de la zona del navegador', () => {
    expect(cinemaLocalToUtcIso('2030-03-10T15:00', 'America/Lima')).toBe('2030-03-10T20:00:00.000Z')
  })

  it('cruza la medianoche UTC: 20:00 en Lima ya es el día siguiente en UTC', () => {
    expect(cinemaLocalToUtcIso('2030-03-10T20:00', 'America/Lima')).toBe('2030-03-11T01:00:00.000Z')
  })

  it('el viaje de ida y vuelta devuelve la misma hora', () => {
    expect(utcIsoToCinemaLocal(cinemaLocalToUtcIso('2030-12-31T23:30', 'America/Lima'), 'America/Lima')).toBe('2030-12-31T23:30')
  })

  it('respeta una zona con horario de verano', () => {
    // Nueva York: verano UTC-4, invierno UTC-5.
    expect(cinemaLocalToUtcIso('2030-07-01T12:00', 'America/New_York')).toBe('2030-07-01T16:00:00.000Z')
    expect(cinemaLocalToUtcIso('2030-01-01T12:00', 'America/New_York')).toBe('2030-01-01T17:00:00.000Z')
  })

  it('rechaza una fecha mal formada', () => {
    expect(() => cinemaLocalToUtcIso('10/03/2030 15:00')).toThrow()
  })

  it('"hoy" es el día del cine aunque en UTC ya sea mañana', () => {
    expect(cinemaToday(new Date('2030-03-11T02:00:00Z'), 'America/Lima')).toBe('2030-03-10')
  })

  it('suma días sin pasar por zonas horarias', () => {
    expect(addDays('2030-03-01', -1)).toBe('2030-02-28')
    expect(addDays('2030-12-31', 1)).toBe('2031-01-01')
  })
})
