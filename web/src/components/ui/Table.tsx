import type { HTMLAttributes, ReactNode, TdHTMLAttributes, ThHTMLAttributes } from 'react'
import { cn } from './cn'

/** Tabla con desplazamiento horizontal en pantallas angostas: nunca desborda la página. */
export function Table({ children, caption }: { children: ReactNode; caption: string }) {
  return (
    <div className="overflow-x-auto rounded-xl border border-slate-200 bg-white">
      <table className="w-full min-w-[640px] text-left text-sm">
        <caption className="sr-only">{caption}</caption>
        {children}
      </table>
    </div>
  )
}

export const Thead = ({ children }: { children: ReactNode }) => <thead className="bg-slate-50 text-xs uppercase tracking-wide text-slate-500">{children}</thead>
export const Th = ({ className, ...rest }: ThHTMLAttributes<HTMLTableCellElement>) => <th scope="col" className={cn('px-4 py-3 font-medium', className)} {...rest} />
export const Tr = ({ className, ...rest }: HTMLAttributes<HTMLTableRowElement>) => <tr className={cn('border-t border-slate-100', className)} {...rest} />
export const Td = ({ className, ...rest }: TdHTMLAttributes<HTMLTableCellElement>) => <td className={cn('px-4 py-3 align-middle text-slate-800', className)} {...rest} />
