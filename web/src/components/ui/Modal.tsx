import { useEffect, useId, useRef, type ReactNode } from 'react'
import { Button } from './Button'
import { cn } from './cn'

interface ModalProps {
  title: string
  onClose: () => void
  children: ReactNode
  footer?: ReactNode
  size?: 'sm' | 'md' | 'lg'
}

const FOCUSABLE = 'a[href], button:not([disabled]), input:not([disabled]), select:not([disabled]), textarea:not([disabled]), [tabindex]:not([tabindex="-1"])'

/**
 * Diálogo modal accesible: role="dialog" + aria-modal, el foco entra al abrir, se queda dentro (Tab cicla), Esc cierra y al cerrar
 * el foco vuelve a quien lo abrió. El fondo no se desplaza mientras está abierto.
 */
export function Modal({ title, onClose, children, footer, size = 'md' }: ModalProps) {
  const titleId = useId()
  const ref = useRef<HTMLDivElement>(null)
  const onCloseRef = useRef(onClose)
  onCloseRef.current = onClose

  useEffect(() => {
    const previous = document.activeElement as HTMLElement | null
    const node = ref.current
    const first = node?.querySelector<HTMLElement>('[data-autofocus]') ?? node?.querySelector<HTMLElement>(FOCUSABLE)
    first?.focus()
    document.body.style.overflow = 'hidden'

    const onKey = (event: KeyboardEvent) => {
      if (event.key === 'Escape') {
        event.stopPropagation()
        onCloseRef.current()
        return
      }
      if (event.key !== 'Tab' || !node) return
      const items = [...node.querySelectorAll<HTMLElement>(FOCUSABLE)]
      if (items.length === 0) return
      const firstItem = items[0]!
      const lastItem = items[items.length - 1]!
      if (event.shiftKey && document.activeElement === firstItem) {
        event.preventDefault()
        lastItem.focus()
      } else if (!event.shiftKey && document.activeElement === lastItem) {
        event.preventDefault()
        firstItem.focus()
      }
    }
    document.addEventListener('keydown', onKey)
    return () => {
      document.removeEventListener('keydown', onKey)
      document.body.style.overflow = ''
      previous?.focus()
    }
  }, [])

  return (
    <div className="fixed inset-0 z-50 flex items-end justify-center bg-black/50 p-0 sm:items-center sm:p-4" onMouseDown={(e) => e.target === e.currentTarget && onClose()}>
      <div
        ref={ref}
        role="dialog"
        aria-modal="true"
        aria-labelledby={titleId}
        className={cn(
          'flex max-h-[92vh] w-full flex-col rounded-t-2xl bg-white shadow-xl sm:rounded-2xl',
          size === 'sm' && 'sm:max-w-md', size === 'md' && 'sm:max-w-xl', size === 'lg' && 'sm:max-w-3xl',
        )}
      >
        <div className="flex items-center justify-between border-b border-slate-200 px-5 py-4">
          <h2 id={titleId} className="text-lg font-semibold text-slate-900">{title}</h2>
          <button type="button" onClick={onClose} aria-label="Cerrar" className="inline-flex size-11 items-center justify-center rounded-lg text-slate-500 hover:bg-slate-100">✕</button>
        </div>
        <div className="overflow-y-auto px-5 py-4">{children}</div>
        {footer && <div className="flex flex-wrap justify-end gap-2 border-t border-slate-200 px-5 py-3">{footer}</div>}
      </div>
    </div>
  )
}

interface ConfirmProps {
  title: string
  message: ReactNode
  confirmLabel: string
  danger?: boolean
  loading?: boolean
  /** Deshabilita «confirmar» mientras falta información (por ejemplo, el dato fresco que justifica la advertencia). */
  confirmDisabled?: boolean
  onConfirm: () => void
  onCancel: () => void
}

/** Confirmación de una acción destructiva. El foco empieza en "Cancelar": Enter sin querer no destruye nada. */
export function ConfirmDialog({ title, message, confirmLabel, danger = true, loading, confirmDisabled, onConfirm, onCancel }: ConfirmProps) {
  return (
    <Modal
      title={title}
      size="sm"
      onClose={onCancel}
      footer={
        <>
          <Button variant="secondary" onClick={onCancel} data-autofocus disabled={loading}>Cancelar</Button>
          <Button variant={danger ? 'danger' : 'primary'} onClick={onConfirm} loading={loading} disabled={confirmDisabled}>{confirmLabel}</Button>
        </>
      }
    >
      <div className="text-sm text-slate-700">{message}</div>
    </Modal>
  )
}
