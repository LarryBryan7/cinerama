# ADR-008: Redis solo como backplane de SignalR y caché L2 condicionada a medición

- **Estado:** Aceptada
- **Fecha:** 2026-10-02
- **Fase:** 6

## Contexto

La pregunta que esta decisión debe responder es: **¿qué resuelve Redis que SQL Server o ASP.NET Core no resolverían bien?**

Con **una sola instancia** de la API la respuesta es "nada que haga falta":
- SignalR entrega los eventos a todos los clientes conectados a esa instancia.
- `IMemoryCache` alcanza para cachear.
- SQL Server garantiza la integridad (ADR-004).

Con **dos o más instancias** aparece un problema real. SignalR mantiene las conexiones en la memoria de cada proceso. Si el cliente A bloquea un asiento a través de la instancia 1, el evento solo llega a los clientes conectados a la instancia 1. El cliente B, conectado a la instancia 2, sigue viendo el asiento libre hasta que recarga la pantalla.

## Decisión

### Usos aprobados

| Uso | Problema que resuelve | Condición |
|---|---|---|
| **Backplane de SignalR** (`AddStackExchangeRedis`) | Propagar `SeatStatusChanged` entre instancias mediante pub/sub de Redis. | Se activa cuando hay `REDIS__CONNECTIONSTRING`. Sin él, la API funciona con una instancia. |
| **Caché L2 de HybridCache** para la cartelera y el detalle de películas | Datos de lectura frecuente que cambian poco; con varias instancias, evita que cada una vaya a SQL Server por separado. HybridCache además protege contra estampidas. | **Solo si la medición lo justifica.** Se mide la cartelera con k6 con y sin caché, y si la mejora no es significativa se queda en `IMemoryCache` (L1) y se documenta el resultado. Invalidación por etiqueta al editar películas o funciones. |

### Usos rechazados explícitamente

| Uso | Motivo |
|---|---|
| Fuente de verdad del estado de los asientos | SQL Server es la única fuente de verdad (ADR-004). |
| Lock distribuido de asientos (Redlock o `SET NX PX`) | Dos fuentes de verdad pueden desincronizarse: un lock de Redis que vence durante una pausa larga del proceso deja pasar una segunda escritura. La base igual necesitaría su propia garantía, así que el lock no aporta integridad, solo complejidad. |
| Lock para que una sola instancia ejecute la limpieza de bloqueos | Innecesario: el `UPDATE ... OUTPUT` es atómico e idempotente (ADR-005). |
| Caché del mapa de asientos | Cambia en cada bloqueo. La invalidación sería tan frecuente como las lecturas, y la consulta indexada de ~200 filas es trivial. Se reconsidera solo si una medición lo exige. |
| Rate limiting distribuido | No es necesario para la demo. El limitador por instancia es suficiente. |

### Demostración reproducible con 2 instancias

Un perfil de Docker Compose (`docker compose --profile scale up`) levanta `api-1` (puerto 8081) y `api-2` (puerto 8082), con la misma base y el mismo Redis. **No hay balanceador:** cada cliente se conecta de forma explícita a una instancia distinta, así el escenario se controla y se observa sin ambigüedad.

| Paso | Sin backplane (`SIGNALR__USEREDIS=false`) | Con backplane |
|---|---|---|
| 1. El cliente B (conectado a `api-2`) abre el mapa de la función 42 | Ve F7 libre | Ve F7 libre |
| 2. El cliente A (conectado a `api-1`) bloquea F7 | `api-1` notifica solo a sus clientes | `api-1` publica en Redis |
| 3. ¿B recibe `SeatStatusChanged`? | **No.** F7 sigue libre en su pantalla. | **Sí**, en menos de un segundo. |
| 4. B intenta bloquear F7 | 409 (SQL Server protege igual) | No llega a intentarlo |

El paso 4 es clave para la explicación: **sin Redis, el sistema sigue siendo correcto. Lo que se degrada es la experiencia.** Eso confirma que Redis está en el camino de la notificación y no en el de la integridad.

La demo se automatiza con un script (`demo/two-instances`) que abre dos conexiones SignalR, una a cada instancia, hace el bloqueo por REST e imprime si el evento llegó. Se documenta paso a paso en `docs/demo-two-instances.md`.

## Alternativas consideradas

| Alternativa | Motivo del descarte |
|---|---|
| Azure SignalR Service | Resuelve el escalado como servicio gestionado, pero requiere una cuenta de Azure y no corre en local. |
| Balanceador con sesiones persistentes (*sticky sessions*) | No resuelve el problema: cada cliente sigue atado a una instancia y los eventos siguen sin cruzar entre instancias. |
| Backplane sobre SQL Server | No existe un backplane oficial para ASP.NET Core SignalR (sí existía en el SignalR clásico de .NET Framework). |

## Consecuencias

- Redis pasa a ser una dependencia **opcional**: la API arranca y funciona sin él con una instancia. `/health` informa su estado cuando está configurado.
- La caché L2 queda sujeta a una medición documentada en `docs/performance.md`. Si no se justifica, se deja constancia y no se activa.

## Motivación de portafolio

**Portafolio, y se declara así.** Un cine de una sola sede funciona con una instancia de la API, y en ese caso Redis no es necesario. Se implementa **principalmente para demostrar** el escalado horizontal de SignalR y el uso de un caché distribuido.

El ADR limita su alcance a los usos con un problema técnico real **dentro del escenario de varias instancias**, y deja explícito lo que Redis *no* hace.
