# ADR-015: Rutas inexistentes: 401 sin sesión, 404 con sesión

- **Estado:** Aceptada
- **Fecha:** 2026-10-02
- **Tipo:** decisión de seguridad

## Contexto

La API es segura por defecto (RNF-03): una política de respaldo (`FallbackPolicy`) exige un usuario autenticado en todo endpoint que no se marque como público. Un test de la fase 1 (que recorre todas las rutas sin token) mostró un efecto lateral: esa política también se aplica a las **rutas que no existen**. Una solicitud anónima a `/api/v1/no-existe` responde `401`, no `404`.

Se evaluó si eso es un defecto a corregir o un comportamiento a conservar.

## Decisión

Se **conserva** y se documenta como decisión de seguridad:

| Quién llama | Ruta | Respuesta |
|---|---|---|
| Anónimo | Pública (`/movies`, `/auth/login`, `/health`…) | La respuesta normal. |
| Anónimo | Protegida **o inexistente** | `401` `unauthorized`, **idéntica** en ambos casos. |
| Autenticado | Inexistente | `404` ProblemDetails con `code: not_found`. |
| Autenticado | Protegida sin el rol necesario | `403` `forbidden`. |

## Por qué

Con `404` para lo que no existe y `401` para lo que existe pero está protegido, un anónimo podría **enumerar los endpoints** de la API probando rutas y mirando qué código recibe. Con la política actual, para un anónimo "no existe" y "existe pero necesitas sesión" son indistinguibles. La misma lógica es la que sigue RN-32 para las reservas ajenas (404 igual que una inexistente).

## Alternativas consideradas

| Alternativa | Motivo del descarte |
|---|---|
| Permitir el acceso anónimo al `404` (que las rutas inexistentes respondan `404` a todos) | Reabre la enumeración de rutas, que es justo lo que se quiere evitar, a cambio de un mensaje de error más "limpio" para quien se equivoca de ruta. |
| `404` para las rutas protegidas cuando no hay sesión | Rompe el contrato estándar: un recurso protegido sin credenciales debe responder `401` para que el cliente sepa que debe autenticarse (y para que el refresh funcione). |

## Consecuencias

- Quien se equivoca de ruta sin haber iniciado sesión recibe `401`, que puede confundir. Swagger (`/swagger`, en Development o con `Swagger:Enabled`) es la referencia de rutas válidas.
- Los escáneres automáticos ven `401` en todo salvo en las pocas rutas públicas. En la monitorización, un pico de `401` no implica por sí solo un ataque a una ruta concreta.
- Dos tests lo fijan: `UnknownRoute_WithoutAToken_Returns401_SoRoutesCannotBeEnumerated` y `UnknownRoute_WhenAuthenticated_Returns404…`.

## Motivación de portafolio

**Producto.** Es una decisión de seguridad que se descubrió por una prueba y se eligió conservar de forma consciente.
