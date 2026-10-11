import { Button } from './Button'

interface Props {
  page: number
  pageSize: number
  totalCount: number
  onPage: (page: number) => void
}

export function Pagination({ page, pageSize, totalCount, onPage }: Props) {
  const pages = Math.max(1, Math.ceil(totalCount / pageSize))
  if (totalCount === 0) return null
  const from = (page - 1) * pageSize + 1
  const to = Math.min(page * pageSize, totalCount)
  return (
    <nav aria-label="Paginación" className="mt-4 flex flex-wrap items-center justify-between gap-3 text-sm text-slate-600">
      <span>Mostrando {from}–{to} de {totalCount}</span>
      <div className="flex items-center gap-2">
        <Button variant="secondary" size="sm" disabled={page <= 1} onClick={() => onPage(page - 1)}>Anterior</Button>
        <span aria-current="page">Página {page} de {pages}</span>
        <Button variant="secondary" size="sm" disabled={page >= pages} onClick={() => onPage(page + 1)}>Siguiente</Button>
      </div>
    </nav>
  )
}
