# Políticas y Mitigación de Seguridad — API "MiniCore"

Este documento detalla el modelo de seguridad implementado en la API de **MiniCore Bank** para neutralizar las amenazas críticas inherentes a las plataformas de mutación financiera y transacciones de alta disponibilidad.

## Matriz de Amenazas Identificadas y Mitigaciones

### 1. Ataque de Reentrada Financiera (Race Conditions / Concurrencia Simultánea)
*   **Amenaza:** Un actor malintencionado lanza múltiples peticiones de retiro de forma simultánea (mismo milisegundo) aprovechando el desfase del hilo del procesador. El sistema lee el saldo original antes de que el primer hilo actualice la base de datos, permitiendo retirar más dinero del disponible (Escenario E10).
*   **Mitigación:** Se implementó una estrategia de doble capa: la transacción opera bajo el nivel de aislamiento más estricto de SQL Server (`IsolationLevel.Serializable`) y bloquea activamente las filas de las cuentas en la lectura (`WITH (UPDLOCK)`). Adicionalmente, el mecanismo nativo `ROWVERSION` valida en el `UPDATE` que la estampa binaria no haya cambiado, abortando en seco cualquier intento de sobreescritura concurrente.

### 2. Fraude por Alteración de Payload en Reintentos de Red (Idempotency Tampering)
*   **Amenaza:** Un atacante intercepta una petición web legítima y reutiliza una cabecera `Idempotency-Key` ya aprobada en el sistema, pero altera el cuerpo del JSON (por ejemplo, modificando el monto de \$10.00 USD a \$9,999.00 USD o cambiando la cuenta destino) intentando forzar un segundo cobro fraudulento.
*   **Mitigación:** Se incorporó un validador criptográfico **SHA-256**. El controlador genera un hash único basado estrictamente en los datos del negocio (`SourceAccountId`, `DestinationAccountId`, `Amount`, `Currency`). Al recibir un reintento, el filtro inicial compara el hash del request actual contra el hash guardado en la tabla `IdempotencyKeys`. Si difieren, bloquea la transacción devolviendo un código `422 IDEMPOTENCY_KEY_REUSED`.

### 3. Inyección SQL (SQL Injection) en Consultas Dinámicas
*   **Amenaza:** Al utilizar ADO.NET puro (`System.Data.SqlClient`) para construir consultas de búsqueda complejas (como el filtrado dinámico por fechas y paginación en el historial de movimientos de la cuenta), un atacante podría inyectar comandos maliciosos a través de las variables de la URL para extraer o alterar datos de otros clientes.
*   **Mitigación:** Quedó estrictamente prohibida la concatenación de texto plano en las sentencias SQL. Todas las consultas dinámicas y condicionales se construyen utilizando **consultas parametrizadas** mediante la colección `SqlCommand.Parameters.AddWithValue`. SQL Server trata estas entradas de forma nativa como valores literales y nunca como código ejecutable.

### 4. Denegación de Servicio por Agotamiento de Memoria (Paginación Ilimitada)
*   **Amenaza:** Un usuario o script automatizado consume el endpoint de historial de movimientos (`GET /api/accounts/{id}/movements`) solicitando un tamaño de página masivo (`size=1000000`), forzando al servidor web a realizar lecturas masivas en la base de datos, saturando el búfer de memoria del IIS Express y provocando la caída del servicio.
*   **Mitigación:** Se estableció un **límite técnico estricto (Hard Limit)** en el controlador de cuentas. Si el parámetro `size` enviado por el cliente supera los 100 registros, el backend reescribe la variable forzándola de forma automática a un máximo de `100`. Las lecturas se realizan de forma indexada utilizando las cláusulas `OFFSET` y `FETCH NEXT ROWS ONLY` de SQL Server.

### 5. Fuga de Información Sensible en Respuestas de Error (Information Leakage)
*   **Amenaza:** Ante un fallo inesperado (como la caída del motor de base de datos o un desbordo matemático), la API expone al exterior trazas técnicas detalladas (*Stack Trace*, nombres de servidores, estructura de carpetas o consultas internas), dándole información valiosa a un atacante para mapear vulnerabilidades.
*   **Mitigación:** Toda la lógica de negocio sensible está envuelta en bloques estructurados `try-catch`. Las excepciones de negocio esperadas se transforman en respuestas estandarizadas bajo el formato industrial **`ProblemDetails`** (RFC 7807), mientras que los errores no controlados capturan la excepción de forma genérica en producción, evitando la fuga de detalles internos del sistema.