# Panel de administración de Cinerama

React 19 + TypeScript + Vite. Consume la API de Cinerama; no tiene lógica de negocio propia ni habla con la base de datos ([ADR-018](../docs/adr/ADR-018-admin-panel.md)).

## Pantallas

| Ruta | Qué hace |
|---|---|
| `/` | Dashboard: ventas del día en la hora del cine y próximas funciones. |
| `/movies`, `/movies/new`, `/movies/:id` | Películas: filtros, alta, edición, baja lógica y reactivación. |
| `/genres` | Géneros. No se borra uno que tenga películas. |
| `/cinemas` | Cines. Desactivar uno cancela sus funciones futuras sin ventas. |
| `/rooms`, `/rooms/:id/layout` | Salas y editor de asientos (filas, posiciones, pasillos) con teclado. Bloqueado si la sala tiene funciones futuras. |
| `/showtimes` | Funciones: programar, editar (sin reservas) y cancelar (cancela sus reservas confirmadas). |
| `/bookings` | Reservas de todos los clientes, con filtros; cancelar hasta el inicio de la función. |
| `/users` | Usuarios: filtros y activar o desactivar (no la propia cuenta). |
| `/reports` | KPIs, ingresos por día, ventas por película y ocupación por función o sala. |

## Cómo se ejecuta

Requisitos: Node 22 o superior y la API en marcha (`docker compose up` en la raíz del repositorio).

```bash
cp .env.example .env     # VITE_API_URL y VITE_CINEMA_TIMEZONE
npm install
npm run dev              # http://localhost:5173
```

La cuenta de administrador demo es `admin@cinerama.test`; la contraseña es la de `SEED__ADMINPASSWORD` en el `.env` de la raíz. Para que la cookie del refresh token (`SameSite=Strict`) viaje, el panel y la API deben compartir *site*: `localhost:5173` y `localhost:8090` lo hacen. La API debe permitir ese origen (`CORS__ALLOWEDORIGINS`, ya es el valor por defecto).

| Comando | |
|---|---|
| `npm test` | Pruebas (Vitest + Testing Library + MSW). Corren en la zona `Asia/Tokyo` a propósito. |
| `npm run typecheck` | Solo tipos. |
| `npm run build` | Tipos + paquete de producción en `dist/`. |

## Decisiones que conviene conocer

- **Sesión:** el access token vive solo en una variable del módulo (`src/api/client.ts`); el refresh token, en una cookie HttpOnly que JavaScript no puede leer. Al recargar se recupera con `POST /auth/refresh`. Hay un único refresh en curso: las solicitudes que reciben `401 token_expired` esperan el mismo resultado.
- **Errores:** las pantallas deciden por el `code` estable de la API (`src/lib/errors.ts`), nunca por su texto.
- **Horas:** lo que se escribe y se muestra es la hora del cine (`VITE_CINEMA_TIMEZONE`), no la del navegador (`src/lib/time.ts`).
- **Estado del servidor:** TanStack Query; sin reintentos ante un 4xx. Las listas conservan la página anterior mientras cargan la siguiente.
- **Accesibilidad:** diálogos con foco atrapado y Esc, errores de campo conectados con `aria-describedby`, cuadrícula de asientos navegable con flechas, y cada gráfico acompañado de su tabla.

## Estructura

```
src/
├── api/          # cliente Axios (sesión), endpoints y tipos del contrato
├── auth/         # contexto de sesión, guardia de rutas y login
├── components/   # ui/ (botón, formulario, modal, tabla, toast…) y layout/
├── features/     # una carpeta por pantalla (+ sus pruebas)
├── lib/          # errores, formatos, horas, validación
└── test/         # servidor MSW, fábricas de datos y render de pruebas
```
