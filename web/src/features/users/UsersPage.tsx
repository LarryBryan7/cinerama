import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useState } from 'react'
import { users as api } from '@/api/endpoints'
import type { AdminUser, Role } from '@/api/types'
import { useAuth } from '@/auth/AuthContext'
import { Button } from '@/components/ui/Button'
import { Badge, EmptyState, ErrorState, PageHeader, Spinner } from '@/components/ui/Feedback'
import { Input, Select } from '@/components/ui/Form'
import { ConfirmDialog } from '@/components/ui/Modal'
import { Pagination } from '@/components/ui/Pagination'
import { Table, Td, Th, Thead, Tr } from '@/components/ui/Table'
import { useToast } from '@/components/ui/Toast'
import { formatDate } from '@/lib/format'
import { useDebounced } from '../shared/useDebounced'

const PAGE_SIZE = 20

export default function UsersPage() {
  const { user: me } = useAuth()
  const [search, setSearch] = useState('')
  const [role, setRole] = useState<Role | ''>('')
  const [active, setActive] = useState<'' | 'true' | 'false'>('')
  const [page, setPage] = useState(1)
  const [toggling, setToggling] = useState<AdminUser | null>(null)
  const debounced = useDebounced(search)

  const filters = { search: debounced.trim() || undefined, role: role || undefined, isActive: active === '' ? undefined : active === 'true', page, pageSize: PAGE_SIZE }
  const query = useQuery({ queryKey: ['users', filters], queryFn: () => api.list(filters), placeholderData: (previous) => previous })
  const reset = (apply: () => void) => { apply(); setPage(1) }

  return (
    <>
      <PageHeader title="Usuarios" subtitle="Las cuentas de la plataforma. Desactivar una cuenta cierra sus sesiones y le impide volver a entrar." />

      <div className="mb-4 grid gap-3 sm:grid-cols-3">
        <Input type="search" aria-label="Buscar usuario" placeholder="Nombre o email" value={search} onChange={(e) => reset(() => setSearch(e.target.value))} />
        <Select aria-label="Rol" value={role} onChange={(e) => reset(() => setRole(e.target.value as Role | ''))}>
          <option value="">Todos los roles</option>
          <option value="Admin">Administradores</option>
          <option value="Customer">Clientes</option>
        </Select>
        <Select aria-label="Estado de la cuenta" value={active} onChange={(e) => reset(() => setActive(e.target.value as '' | 'true' | 'false'))}>
          <option value="">Activas e inactivas</option>
          <option value="true">Solo activas</option>
          <option value="false">Solo inactivas</option>
        </Select>
      </div>

      {query.isPending && <Spinner />}
      {query.isError && <ErrorState error={query.error} onRetry={() => void query.refetch()} />}
      {query.data && query.data.items.length === 0 && <EmptyState title="No hay usuarios con esos filtros" />}
      {query.data && query.data.items.length > 0 && (
        <>
          <Table caption="Usuarios">
            <Thead><Tr><Th>Usuario</Th><Th>Rol</Th><Th>Alta</Th><Th>Estado</Th><Th className="text-right">Acciones</Th></Tr></Thead>
            <tbody>
              {query.data.items.map((u) => {
                const isMe = u.id === me?.id
                return (
                  <Tr key={u.id} className={u.isActive ? '' : 'bg-slate-50 text-slate-500'}>
                    <Td><p className="font-medium">{u.fullName}{isMe && <span className="ml-2 text-xs text-slate-500">(tú)</span>}</p><p className="text-xs text-slate-500">{u.email}</p></Td>
                    <Td>{u.role === 'Admin' ? <Badge tone="blue">Administrador</Badge> : <Badge>Cliente</Badge>}</Td>
                    <Td>{formatDate(u.createdAt.slice(0, 10))}</Td>
                    <Td>{u.isActive ? <Badge tone="green">Activa</Badge> : <Badge tone="red">Inactiva</Badge>}</Td>
                    <Td className="text-right">
                      <Button
                        variant={u.isActive ? 'ghost' : 'secondary'} size="sm" disabled={isMe && u.isActive} onClick={() => setToggling(u)}
                        aria-label={`${u.isActive ? 'Desactivar' : 'Activar'} la cuenta de ${u.fullName}`}
                        title={isMe && u.isActive ? 'No puedes desactivar tu propia cuenta.' : undefined}
                      >
                        {u.isActive ? 'Desactivar' : 'Activar'}
                      </Button>
                    </Td>
                  </Tr>
                )
              })}
            </tbody>
          </Table>
          <Pagination page={page} pageSize={PAGE_SIZE} totalCount={query.data.totalCount} onPage={setPage} />
        </>
      )}

      {toggling && <ToggleUser user={toggling} onClose={() => setToggling(null)} />}
    </>
  )
}

function ToggleUser({ user, onClose }: { user: AdminUser; onClose: () => void }) {
  const client = useQueryClient()
  const toast = useToast()
  const next = !user.isActive
  const mutation = useMutation({
    mutationFn: () => api.setActive(user.id, next),
    onSuccess: async () => {
      await client.invalidateQueries({ queryKey: ['users'] })
      toast.success(next ? `La cuenta de ${user.fullName} está activa.` : `La cuenta de ${user.fullName} se desactivó y sus sesiones se cerraron.`)
      onClose()
    },
    onError: (error) => {
      toast.error(error) // cannot_deactivate_self
      onClose()
    },
  })

  return (
    <ConfirmDialog
      title={next ? 'Activar la cuenta' : 'Desactivar la cuenta'} confirmLabel={next ? 'Activar' : 'Desactivar'} danger={!next}
      loading={mutation.isPending} onCancel={onClose} onConfirm={() => mutation.mutate()}
      message={next
        ? <>¿Activar la cuenta de <strong>{user.fullName}</strong> ({user.email})? Podrá volver a iniciar sesión.</>
        : <>¿Desactivar la cuenta de <strong>{user.fullName}</strong> ({user.email})? Se cerrarán sus sesiones y no podrá volver a entrar. Su access token actual puede seguir funcionando hasta 15 minutos.</>}
    />
  )
}
