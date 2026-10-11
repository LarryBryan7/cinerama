import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it, vi } from 'vitest'
import { DateRangeControl, validateRange } from './DateRangeControl'

describe('validateRange', () => {
  it('acepta un rango válido, incluido un solo día y el máximo de 366 días', () => {
    expect(validateRange({ from: '2030-01-01', to: '2030-01-01' })).toBeNull()
    expect(validateRange({ from: '2030-01-01', to: '2031-01-01' })).toBeNull() // 366 días
  })
  it('rechaza fechas faltantes, invertidas o de más de 366 días', () => {
    expect(validateRange({ from: '', to: '2030-01-01' })).toMatch(/dos fechas/)
    expect(validateRange({ from: '2030-02-01', to: '2030-01-01' })).toMatch(/no puede ser posterior/)
    expect(validateRange({ from: '2030-01-01', to: '2031-01-02' })).toMatch(/366/)
  })
})

describe('DateRangeControl', () => {
  it('no aplica un rango inválido y muestra por qué', async () => {
    const onApply = vi.fn()
    render(<DateRangeControl value={{ from: '2030-01-01', to: '2030-01-31' }} onApply={onApply} />)
    const user = userEvent.setup()
    await user.clear(screen.getByLabelText('Hasta'))
    expect(screen.getByRole('alert')).toHaveTextContent('Elige las dos fechas.')
    expect(screen.getByRole('button', { name: 'Aplicar' })).toBeDisabled()
    expect(onApply).not.toHaveBeenCalled()
  })

  it('aplica el rango escrito y los atajos de 7 días', async () => {
    const onApply = vi.fn()
    render(<DateRangeControl value={{ from: '2030-01-01', to: '2030-01-31' }} onApply={onApply} />)
    const user = userEvent.setup()
    await user.click(screen.getByRole('button', { name: 'Aplicar' }))
    expect(onApply).toHaveBeenLastCalledWith({ from: '2030-01-01', to: '2030-01-31' })
    await user.click(screen.getByRole('button', { name: '7 días' }))
    const range = onApply.mock.calls.at(-1)![0] as { from: string; to: string }
    expect((Date.parse(range.to) - Date.parse(range.from)) / 86_400_000).toBe(6)
  })
})
