markdown
# API "MiniCore" — Servicio de Transferencias Bancarias (MiniCore Bank)

Este repositorio contiene la implementación de referencia para la API backend del servicio de transferencias entre cuentas de **MiniCore Bank**. El sistema está diseñado siguiendo estándares estrictos de consistencia contable, control de concurrencia en entornos multihilo e inmutabilidad de datos para auditorías de misión crítica.

## 1. Stack Tecnológico y Arquitectura

*   **Lenguaje y Framework:** C# sobre **.NET Framework 4.8**.
*   **Paradigma Web:** Controladores tradicionales basados en clases que heredan de `ApiController` (Uso explícito de **ASP.NET Web API 2**; sin soporte ni uso de Minimal APIs por restricciones técnicas de la versión).
*   **Acceso a Datos:** **ADO.NET Puro** (`System.Data.SqlClient`) ejecutado de forma parametrizada.
*   **Serialización:** **Newtonsoft.Json** configurado globalmente para formato `camelCase`.
*   **Motor de Base de Datos:** **SQL Server (LocalDB)**.
*   **Autenticación/Seguridad:** Endpoints de consumo anónimos y abiertos para simplificar la ejecución directa e inmediata a través de Postman.

---

## 2. Decisiones de Diseño y Arquitectura Financiera

### A. Control de Concurrencia Avanzado (Escenario E10)
Para evitar la pérdida de dinero por condiciones de carrera (Race Conditions) en ejecuciones simultáneas:
1.  **Transacción SQL Aislada (`IsolationLevel.Serializable`):** Cada proceso de transferencia bloquea temporalmente el rango de registros de cuentas involucrados en la base de datos utilizando bloqueos de actualización (`WITH (UPDLOCK)`) hasta que todo el bloque (débito, crédito, comisión y auditoría) finaliza con éxito o ejecuta un rollback.
2.  **Mecanismo `RowVersion`:** Se incluye la columna nativa `ROWVERSION` en la tabla `Accounts`. Cada actualización de saldo compara la estampa binaria recuperada al inicio. Si otra petición modifica la fila en el mismo milisegundo, las filas afectadas serán cero, lanzando un fallo controlado que salvaguarda la integridad contable.

### B. Inmutabilidad Contable (Libro Mayor / Append-Only)
El saldo de una cuenta nunca cambia de forma aislada. Cada registro en `Transfers` genera de manera obligatoria y atómica entre dos y tres asientos contables en la tabla `Movements` (`Debit`, `Credit` y opcionalmente `Fee`). Los registros de movimientos nunca se editan ni se eliminan, permitiendo reconstruir el historial financiero paso a paso.

### C. Idempotencia Rigurosa en Dos Tiempos con Hash Criptográfico
Para cumplir la regla de negocio y convivir con la restricción física de llaves foráneas intactas en la base de datos:
1.  **Validación de Alteraciones (SHA-256):** El sistema genera un hash criptográfico único basado en las propiedades esenciales del payload recibido (`sourceAccountId`, `destinationAccountId`, `amount`, `currency`).
2.  **Filtro de Lectura Previa:** Al recibir un request, la API ejecuta primero un `SELECT` limpio fuera de la transacción. Si la clave existe y el hash coincide, devuelve el JSON previo inyectando la cabecera HTTP `Idempotent-Replayed: true`. Si el hash no coincide, frena la operación y devuelve un error `422 IDEMPOTENCY_KEY_REUSED`.
3.  **Persistencia Segura (INSERT + UPDATE):** Si la clave es nueva, se inicia la transacción y se ejecuta un `INSERT` inicial de la idempotencia con el payload temporal `"PENDING"` para satisfacer al motor relacional. Una vez procesado el dinero y obtenido el `transferId` real, se efectúa un `UPDATE` sustituyendo la palabra por el JSON bancario real definitivo antes de hacer el `Commit`.

---

## 3. Supuestos Adoptados ante Ambigüedades

*   **Identificadores Alfanuméricos:** El contrato define las cuentas bajo el formato alfanumérico (**`ACC-1001`**, **`ACC-1002`**). Por lo tanto, se determinó que la columna `Id` de la tabla `Accounts` y sus respectivas llaves foráneas operen como tipo de dato **`VARCHAR(20)`** en lugar de enteros tradicionales.
*   **Inconsistencia de Fondos Semilla (Escenarios E3 y E4):** Los datos semilla otorgan a la cuenta `ACC-1006` un saldo exacto de \$501.00 USD. Al intentar transferir \$501.00 USD, la regla de negocio gatilla una comisión mínima de \$3.00 USD (Costo total requerido: \$504.00 USD). Por lo tanto, el sistema rechazará de forma correcta el escenario con un código `422 INSUFFICIENT_FUNDS` para evitar un saldo negativo ilegal. Para probar el éxito de la comisión mínima (E3), se asume el uso temporal del endpoint de recarga de balance.

---

## 4. Limitaciones Conocidas

*   **Dependencia de Windows:** Al implementarse sobre .NET Framework 4.8 clásico en formato de proyectos `.csproj` heredados (no SDK-style), el entorno de ejecución está estrictamente ligado al ecosistema de Windows y al servidor **IIS Express / IIS Completo**, requiriendo MSBuild para su compilación.

---

## 5. Instrucciones de Ejecución y Pruebas (Comandos Exactos)

### Paso 1: Inicialización de la Base de Datos
1. Abre **Visual Studio 2022** y dirígete al menú `Ver ➔ Explorador de objetos de SQL Server`.
2. Conéctate a la instancia local estándar: `(localdb)\MSSQLLocalDB`.
3. Haz clic derecho sobre la carpeta `Databases`, selecciona `Agregar nueva base de datos` y nómbrala exactamente **`MiniCoreBankDb`**.
4. Abre una ventana de comandos de consulta sobre `MiniCoreBankDb`, pega el contenido de los scripts de creación de tablas e inserción de datos semilla, y haz clic en **Ejecutar** (o presiona `F5`).

### Paso 2: Despliegue de la Web API
1. Abre la solución `MiniCoreBank.csproj` dentro de Visual Studio 2022.
2. Restaura los paquetes NuGet ejecutando la consola del administrador de paquetes (`Herramientas ➔ Administrador de paquetes NuGet ➔ Consola`) e introduciendo el comando exacto:
   ```powershell
   Update-Package -Reinstall
   ```
3. Limpia y recompila la solución completa presionando secuencialmente los atajos de teclado:
   *   **`Ctrl + Mayús + Enter`** (o a través del menú menú `Compilar ➔ Limpiar solución`).
   *   **`Ctrl + Mayús + B`** (para reconstruir las DLLs de forma limpia).
4. Presiona el botón verde de reproducción **IIS Express** (o la tecla **`F5`**) para levantar el servidor web local. Copia la URL e IIS Port asignados en la barra de direcciones del navegador web secundario (ej. `http://localhost:44372`).

### Paso 3: Lanzamiento de la Suite de Pruebas en Postman
1. Abre **Postman** e importa el archivo de la colección: `MiniCoreBank_AcceptanceTests.postman_collection.json`.
2. Haz clic derecho en el nombre de la colección importada, selecciona **Edit** (Editar), ve a la pestaña **Variables** y actualiza el campo de valor actual (`Current Value`) de la variable `baseUrl` con la dirección local copiada en el paso anterior. Guarda los cambios.
3. Ejecuta cada uno de los requests de forma ordenada para verificar la suite completa de escenarios de aceptación (E1 a E12).