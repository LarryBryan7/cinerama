# Historias de usuario

Formato: *Como [actor] quiero [acción] para [beneficio]*. Los criterios de aceptación usan Gherkin (Dado / Cuando / Entonces) y son la fuente de los casos de prueba de [testing/test-cases.md](testing/test-cases.md). La relación completa está en la [matriz de trazabilidad](testing/traceability-matrix.md).

Estimación en puntos relativos (1, 2, 3, 5, 8). Una historia se considera terminada (Definition of Done) cuando:

- sus criterios tienen un test automatizado o un caso manual documentado que pasa;
- el endpoint está documentado en Swagger;
- los errores devuelven ProblemDetails con su `code`;
- la regla correspondiente está referenciada en el código.

---

## Épica E1: Cuenta y sesión

### HU-01 Registro (RF-AUTH-01, 2 pts)
**Como** visitante **quiero** crear una cuenta **para** reservar entradas.

```gherkin
Escenario: registro exitoso
  Dado que no existe una cuenta con el email "ana@example.test"
  Cuando me registro con nombre "Ana", ese email y la contraseña "Cine2026"
  Entonces se crea mi cuenta con rol Customer
  Y recibo un access token y un refresh token

Escenario: email ya registrado, con distinta capitalización
  Dado que existe la cuenta "ana@example.test"
  Cuando me registro con " ANA@Example.test "
  Entonces recibo 409 con code "email_already_registered"

Escenario: contraseña débil
  Cuando me registro con la contraseña "12345678"
  Entonces recibo 400 indicando que falta al menos una letra
```

### HU-02 Inicio de sesión (RF-AUTH-02, 2 pts)
**Como** cliente **quiero** iniciar sesión **para** acceder a mis reservas.

```gherkin
Escenario: credenciales correctas
  Cuando inicio sesión con email y contraseña válidos
  Entonces recibo un access token de 15 minutos y un refresh token

Escenario: credenciales incorrectas
  Cuando inicio sesión con una contraseña incorrecta
  Entonces recibo 401 con un mensaje que no revela si el email existe

Escenario: usuario desactivado
  Dado que mi cuenta fue desactivada
  Cuando inicio sesión con credenciales correctas
  Entonces recibo 401
```

### HU-03 Sesión continua (RF-AUTH-03, 3 pts)
**Como** cliente **quiero** que la app renueve mi sesión sola **para** no volver a escribir mi contraseña cada 15 minutos.

```gherkin
Escenario: renovación transparente
  Dado que mi access token expiró y mi refresh token es válido
  Cuando la app hace una solicitud
  Entonces recibe 401 con code "token_expired", renueva la sesión y repite la solicitud sin que yo lo note

Escenario: rotación
  Cuando uso un refresh token
  Entonces recibo uno nuevo y el anterior deja de ser válido

Escenario: reutilización de un token rotado
  Dado que un refresh token ya fue rotado
  Cuando alguien intenta usarlo de nuevo
  Entonces recibe 401 con code "refresh_token_reused"
  Y se revoca toda la cadena de esa sesión, incluido el token vigente

Escenario: varias solicitudes simultáneas con el token vencido
  Dado que tres solicitudes reciben 401 a la vez
  Entonces la app hace un único refresh y repite las tres solicitudes
```

### HU-04 Cierre de sesión (RF-AUTH-04, 1 pt)
**Como** cliente **quiero** cerrar sesión **para** que nadie use mi cuenta en este dispositivo.

```gherkin
Escenario: logout
  Cuando cierro sesión
  Entonces mi refresh token queda revocado
  Y la caché local de mi cuenta deja de estar disponible en el dispositivo
```

### HU-05 Perfil (RF-AUTH-05, 1 pt)
**Como** cliente **quiero** ver y editar mi nombre **para** que mis tickets muestren mis datos correctos.

## Épica E2: Cartelera

### HU-06 Explorar la cartelera (RF-CAT-01, 3 pts)
**Como** visitante **quiero** ver las películas en cartelera **para** elegir qué ver.

```gherkin
Escenario: cartelera paginada
  Dado que hay 25 películas activas y 2 inactivas
  Cuando consulto la página 1 con tamaño 20
  Entonces veo 20 películas activas, el total es 25 y no aparece ninguna inactiva

Escenario: búsqueda y filtro
  Cuando busco "noche" y filtro por el género "Drama"
  Entonces solo veo películas activas de ese género cuyo título contiene "noche", sin distinguir mayúsculas
```

### HU-07 Detalle y funciones (RF-CAT-02, RF-SHOW-01, 2 pts)
**Como** visitante **quiero** ver el detalle de una película y sus próximas funciones **para** elegir horario y sala.

```gherkin
Escenario: solo funciones futuras y programadas
  Dado que una película tiene una función ayer, una cancelada y dos mañana
  Cuando veo su detalle
  Entonces veo solo las dos funciones de mañana, con sala, hora local y asientos disponibles

Escenario: película inexistente
  Cuando consulto una película que no existe
  Entonces recibo 404
```

## Épica E3: Selección de asientos y reserva

### HU-08 Ver el mapa de asientos (RF-SEAT-01, 3 pts)
**Como** cliente **quiero** ver qué asientos están libres **para** elegir dónde sentarme.

```gherkin
Escenario: estados del mapa
  Dado una función con asientos libres, bloqueados por mí, bloqueados por otros y vendidos
  Cuando abro el mapa
  Entonces cada asiento muestra su estado con color, ícono y etiqueta accesible
  Y distingo mis bloqueos ("Seleccionado") de los de otras personas ("No disponible")

Escenario: bloqueo vencido sin limpiar
  Dado un asiento Locked cuyo bloqueo venció hace 10 segundos
  Cuando abro el mapa
  Entonces ese asiento se muestra disponible (RN-23)
```

### HU-09 Seleccionar asientos (RF-SEAT-02, 5 pts)
**Como** cliente **quiero** seleccionar asientos disponibles **para** reservarlos sin que otra persona me los quite mientras pago.

```gherkin
Escenario: bloqueo exitoso
  Dado que los asientos F7 y F8 están disponibles
  Cuando los selecciono
  Entonces quedan bloqueados a mi nombre durante 5 minutos
  Y veo una cuenta regresiva hasta el vencimiento

Escenario: asiento bloqueado por otra persona
  Dado que F7 está bloqueado por otro cliente
  Cuando intento seleccionar F7 y F8
  Entonces recibo 409 indicando que F7 no está disponible
  Y F8 tampoco queda bloqueado (todo o nada)

Escenario: dos clientes, mismo asiento, mismo instante
  Dado que F7 está disponible
  Cuando el cliente A y el cliente B lo seleccionan simultáneamente
  Entonces exactamente uno recibe 200 y el otro recibe 409

Escenario: límite por cliente
  Dado que ya tengo 10 asientos bloqueados en la función
  Cuando intento bloquear uno más
  Entonces recibo 422 con code "lock_limit_exceeded"

Escenario: la función ya empezó
  Cuando intento bloquear asientos de una función iniciada
  Entonces recibo 422 con code "sales_closed"
```

### HU-10 Deseleccionar (RF-SEAT-03, 1 pt)
**Como** cliente **quiero** liberar un asiento que ya no quiero **para** que otra persona pueda tomarlo de inmediato.

```gherkin
Escenario: liberar asiento propio
  Dado que tengo F7 bloqueado
  Cuando lo deselecciono
  Entonces F7 vuelve a Available y los demás clientes lo ven libre

Escenario: liberar asiento ajeno
  Cuando intento liberar un asiento bloqueado por otro cliente
  Entonces recibo 404 y el bloqueo ajeno se mantiene
```

### HU-11 Recomendación de asientos (RF-SEAT-04, 3 pts)
**Como** cliente **quiero** que la app me sugiera los mejores asientos juntos **para** elegir rápido cuando voy acompañado.

```gherkin
Escenario: grupo contiguo céntrico
  Dado una sala con la fila F libre por completo
  Cuando pido una recomendación para 2 personas
  Entonces la primera opción son 2 asientos contiguos de la fila F, lo más cerca posible del centro

Escenario: los pasillos cortan la contigüidad
  Dado una fila con F5 libre, un pasillo y luego F6 libre
  Cuando pido 2 asientos
  Entonces F5 y F6 no se recomiendan como pareja

Escenario: no hay grupo posible
  Dado que ninguna fila tiene 4 asientos contiguos libres
  Cuando pido 4
  Entonces recibo una lista vacía, no un error
```

### HU-12 Confirmar la reserva (RF-BOOK-01, 5 pts)
**Como** cliente **quiero** confirmar mi compra **para** obtener mis entradas.

```gherkin
Escenario: reserva confirmada
  Dado que tengo F7 y F8 bloqueados y vigentes
  Cuando confirmo
  Entonces recibo 201 con el código de reserva y el total
  Y F7 y F8 pasan a Booked para todos

Escenario: el bloqueo venció
  Dado que mi bloqueo de F7 venció y otra persona tomó F7
  Cuando confirmo
  Entonces recibo 409 con code "lock_expired" y no se crea ninguna reserva

Escenario: reintento tras perder la respuesta
  Dado que confirmé con Idempotency-Key "k-1" y la respuesta no llegó
  Cuando reintento con la misma clave
  Entonces recibo la misma reserva y no se crea una segunda
```

## Épica E4: Mis reservas y ticket

### HU-13 Mis reservas (RF-BOOK-02, 2 pts)
**Como** cliente **quiero** ver mis reservas **para** recordar a qué funciones voy.

```gherkin
Escenario: solo las mías
  Dado que el cliente A tiene 2 reservas y el cliente B tiene 3
  Cuando A lista sus reservas
  Entonces ve exactamente sus 2 reservas, de la más reciente a la más antigua
```

### HU-14 Ticket con QR (RF-BOOK-03, 3 pts)
**Como** cliente **quiero** mostrar un ticket con QR en la entrada **para** no imprimir nada.

```gherkin
Escenario: acceso a una reserva ajena (IDOR)
  Dado que la reserva R pertenece al cliente B
  Cuando el cliente A consulta R por su id
  Entonces recibe 404, no 403

Escenario: QR firmado
  Cuando se altera un carácter del contenido del QR
  Entonces la validación lo rechaza
```

### HU-15 Cancelar una reserva (RF-BOOK-04, 3 pts)
**Como** cliente **quiero** cancelar una reserva **para** liberar los asientos si no puedo asistir.

```gherkin
Escenario: cancelación a tiempo
  Dado que la función empieza en 5 horas
  Cuando cancelo mi reserva
  Entonces la reserva queda Cancelled y sus asientos vuelven a Available

Escenario: fuera de plazo
  Dado que la función empieza en 90 minutos
  Cuando cancelo
  Entonces recibo 422 con code "cancellation_window_closed"

Escenario: reserva ajena
  Cuando intento cancelar la reserva de otro cliente
  Entonces recibo 404 y la reserva no cambia
```

## Épica E5: Tiempo real

### HU-16 Mapa en vivo (RF-SEAT-05, RF-SYS-01, 5 pts)
**Como** cliente **quiero** ver los cambios en el mapa en el momento en que ocurren **para** no elegir asientos que ya se ocuparon.

```gherkin
Escenario: otro cliente bloquea un asiento
  Dado que A y B tienen abierto el mapa de la misma función
  Cuando A bloquea F7
  Entonces en menos de 2 segundos B ve F7 como no disponible, sin recargar la pantalla

Escenario: un bloqueo vence
  Dado que el bloqueo de A sobre F7 venció
  Cuando corre el proceso de liberación
  Entonces B ve F7 disponible

Escenario: reconexión
  Dado que B perdió la conexión 30 segundos
  Cuando la recupera
  Entonces la app vuelve a pedir el mapa completo antes de aplicar nuevos eventos
```

## Épica E6: Administración

### HU-17 Gestionar películas y géneros (RF-CAT-03, RF-CAT-04, 3 pts)
**Como** administrador **quiero** mantener el catálogo **para** que la cartelera esté al día.

```gherkin
Escenario: baja con reservas activas
  Dado que una película tiene una función mañana con reservas confirmadas
  Cuando intento darla de baja
  Entonces recibo 409 con code "movie_has_active_bookings"

Escenario: un cliente intenta crear una película
  Cuando un usuario Customer llama a POST /movies
  Entonces recibe 403
```

### HU-18 Salas y layout (RF-ROOM-01, RF-ROOM-02, 5 pts)
**Como** administrador **quiero** definir la distribución de cada sala **para** que el mapa refleje la sala real.

```gherkin
Escenario: layout con pasillo
  Cuando defino 10 filas de 14 posiciones, con la posición 7 vacía en todas las filas
  Entonces la sala queda con 130 asientos y un pasillo central

Escenario: sala con funciones futuras
  Dado que la sala tiene funciones programadas
  Cuando intento cambiar su layout
  Entonces recibo 409 con code "room_has_future_showtimes"
```

### HU-19 Programar funciones (RF-SHOW-02, RF-SHOW-03, 5 pts)
**Como** administrador **quiero** programar funciones **para** abrir la venta.

```gherkin
Escenario: solapamiento
  Dado que la sala 1 tiene una función de 18:00 a 20:15 (con limpieza)
  Cuando programo otra función en la sala 1 a las 20:00
  Entonces recibo 409 con code "showtime_overlap"

Escenario: generación de asientos
  Cuando programo una función en una sala de 130 asientos
  Entonces la función queda con 130 asientos de función en estado Available
```

### HU-20 Usuarios (RF-USR-01, 2 pts)
**Como** administrador **quiero** desactivar una cuenta **para** bloquear accesos indebidos.

```gherkin
Escenario: desactivación inmediata
  Cuando desactivo al cliente A
  Entonces sus refresh tokens quedan revocados
  Y su próximo refresh falla con 401
```

### HU-21 Dashboard de KPIs (RF-REP-01 a RF-REP-04, 5 pts)
**Como** administrador **quiero** ver ventas y ocupación **para** decidir qué funciones mantener.

```gherkin
Escenario: KPIs coherentes con los datos
  Dado el seed de reportes con ventas conocidas
  Cuando consulto el resumen del periodo
  Entonces los ingresos, la cantidad de reservas, las entradas y la ocupación coinciden con los valores calculados a mano en el caso de prueba

Escenario: las canceladas no suman
  Dado una reserva confirmada y luego cancelada
  Entonces no aparece en ingresos ni en entradas vendidas
```

## Épica E7: Conectividad limitada

### HU-22 Usar la app sin conexión (RF-OFF-01 a RF-OFF-04, 8 pts)
**Como** cliente **quiero** ver mi ticket aunque no tenga señal en el cine **para** poder entrar igual.

```gherkin
Escenario: ticket offline
  Dado que abrí mis reservas con conexión
  Cuando pierdo la conexión y abro el ticket
  Entonces veo el QR y los datos de la reserva, con un aviso de modo sin conexión

Escenario: reservar sin conexión
  Dado que no tengo conexión
  Cuando abro el mapa de asientos
  Entonces la app me informa que necesito conexión y no permite seleccionar asientos

Escenario: reconciliación
  Dado que cancelé una reserva desde otro dispositivo
  Cuando esta app recupera la conexión
  Entonces la reserva aparece como cancelada
```
