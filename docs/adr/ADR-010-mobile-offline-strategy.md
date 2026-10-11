# ADR-010: Offline parcial en la app móvil: lectura sí, escrituras no

- **Estado:** Aceptada
- **Fecha:** 2026-10-02
- **Fase:** 5

> **Avance en la fase 3.** Ya están: Drift por usuario, el almacén de reservas y el **ticket con QR visible sin conexión** una vez descargado (el QR es el contenido firmado por el servidor; la app lo guarda y lo dibuja, pero no puede fabricar uno válido, y su validez la decide el cine al escanearlo), el aviso de conectividad real (`/health`) y la regla de que bloquear, reservar y cancelar exigen conexión (sin cola de escrituras). Siguen en la fase 5: caché de cartelera y detalle, reconciliación y reintentos.

## Contexto

Dentro de un cine la señal suele ser mala, y es justo donde el cliente necesita mostrar su ticket. Al mismo tiempo, el estado de un asiento solo tiene sentido si lo confirma el servidor: una reserva hecha sin conexión no puede garantizarse.

## Decisión

### Qué funciona sin conexión

| Dato | Persistencia | Política |
|---|---|---|
| Cartelera y detalle de películas vistas | Drift (SQLite) | *Stale-while-revalidate*: se muestra la caché al instante y se actualiza en segundo plano. Aviso "Actualizado hace X". |
| Mis reservas | Drift | El servidor reemplaza la caché local en cada sincronización. |
| Ticket y QR | Drift (contenido firmado del QR) | El QR se genera en el dispositivo a partir del contenido guardado. |
| Perfil | Drift | Solo lectura. |
| Sesión | Refresh token en `flutter_secure_storage` | La app abre sin conexión con los datos locales y renueva la sesión al reconectar. |

**Aislamiento entre cuentas:** cada usuario tiene su propio archivo de base local (`cinerama_<userId>.db`), que se abre al iniciar sesión y se cierra al salir. Una consulta nueva no puede filtrar mal datos de otra cuenta, porque no hay una tabla compartida.

### Qué requiere conexión

Login, registro, mapa de asientos, bloqueo, liberación, recomendación, confirmación y cancelación. Sin conexión, estas acciones se muestran deshabilitadas con un mensaje.

**No existe una cola de escrituras pendientes.** Ninguna escritura del dominio tiene sentido sin la validación del servidor: bloquear o confirmar sin conexión prometería algo que el sistema no puede garantizar. Tampoco se encola la cancelación, porque tiene un plazo (RN-30) que podría vencer antes de que vuelva la conexión.

### Detección de conectividad

`connectivity_plus` informa si hay una interfaz de red activa, pero no si la API es alcanzable (por ejemplo, un Wi-Fi con portal cautivo). Por eso la app combina:
- el estado de la interfaz;
- un ping a `/health`;
- los errores de red de las solicitudes reales.

### Sincronización y fallos

| Situación | Comportamiento |
|---|---|
| Vuelve la conexión | Se refrescan la cartelera y las reservas (*pull*). El servidor manda: si una reserva fue cancelada en otro dispositivo, aparece cancelada. |
| La confirmación da timeout | Se reintenta con la misma `Idempotency-Key` (RN-27) y se obtiene la misma reserva, sin duplicados. |
| La app se cierra después de confirmar y antes de recibir la respuesta | Al volver, `GET /bookings` muestra la reserva si se creó. |
| El bloqueo vence durante una desconexión breve | Al reconectar, el mapa se vuelve a pedir y la confirmación devuelve 409 `lock_expired` si corresponde. |
| Reintentos de lectura | Backoff exponencial con un máximo de 3 intentos, solo para errores de red o 5xx. Nunca ante 4xx. |

## Alternativas consideradas

| Alternativa | Motivo del descarte |
|---|---|
| Reservas offline con cola y conciliación posterior | Generaría reservas "aceptadas" que luego fallan. Es una mala experiencia y contradice RNF-01. |
| Hive (clave-valor) | Válido para datos simples. Drift ofrece consultas tipadas y migraciones de esquema, más adecuadas para listas relacionadas (películas, funciones, reservas). |
| Sin caché local | El ticket no se podría mostrar en la sala sin señal, que es el caso principal. |

## Consecuencias

- Riesgo aceptado: un ticket cancelado en otro dispositivo puede seguir viéndose en un dispositivo sin conexión. El control de acceso valida el QR en línea, y el ticket local muestra la fecha de la última sincronización.

## Motivación de portafolio

**Producto.** Mostrar el ticket sin señal es un caso real. La decisión de *no* permitir escrituras offline también es de producto.
