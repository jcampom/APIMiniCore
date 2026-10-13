# Bitácora de Uso de Inteligencia Artificial — API "MiniCore"

Este documento describe de forma transparente cómo se integraron las herramientas de Inteligencia Artificial Generativa como copilotos estratégicos de desarrollo durante el diseño, codificación y resolución de problemas del servicio Web API de MiniCore Bank.

## 1. Herramientas Utilizadas y Propósito

*   **Asistente de Arquitectura y Codificación (IA):** Utilizado de forma conversacional y como motor de generación de código estructurado.
*   **Propósito Principal:** 
    *   Generar el esqueleto inicial del script de base de datos DDL alineado con el motor SQL Server.
    *   Escribir el código repetitivo (*boilerplate*) de los comandos ADO.NET puros (`SqlCommand`, `SqlDataReader`) para los controladores de Web API.
    *   Diseñar y estructurar la colección automatizada de pruebas de aceptación en formato JSON compatible con Postman v2.1.0.

---

## 2. Decisiones Técnicas: Qué se rechazó y qué se corrigió

El uso de la IA no fue automatizado; requirió de una supervisión técnica humana rigurosa (*Human-in-the-Loop*) debido a discrepancias críticas con el requerimiento del negocio:

1.  **Rechazo de Sintaxis Moderna (.NET Core / Minimal APIs):** Las IAs tienden por defecto a escribir código utilizando sintaxis modernas de .NET 6/8/9 (`Program.cs`, `builder.Services`, `System.Text.Json`). Se rechazaron estas propuestas de forma tajante debido a la restricción física del stack corporativo exigido: **.NET Framework 4.8 clásico** basado en archivos `.csproj` antiguos, herencia estricta de `ApiController` y uso de `Newtonsoft.Json`.
2.  **Corrección de Orden por Restricción de Llave Foránea:** Inicialmente, la IA sugirió insertar el registro de la transferencia (`Transfers`) antes de guardar el registro de la llave de idempotencia. Esto provocaba un error `500` inmediato en SQL Server debido a la violación de la restricción de llave foránea (`FK_Transfers_IdempotencyKey`). Se corrigió el flujo para realizar un **INSERT preliminar en IdempotencyKeys (estado "PENDING") y un UPDATE final con el JSON definitivo** una vez obtenido el `transferId`.
3.  **Remoción del Mensaje Ficticio "Processing":** Se corrigió una lógica intermedia sugerida por la IA que devolvía un texto plano `{"message":"Processing"}` en el primer intento exitoso. Se reemplazó por la serialización dinámica del objeto financiero real exigido de forma síncrona por el contrato de Postman.
4.  **Inclusión del Validador Criptográfico SHA-256:** En los primeros borradores, el filtro de idempotencia de la IA solo verificaba si la `Idempotency-Key` existía en la base de datos, lo que permitía que un usuario alterara el monto en Postman y la API procesara la transacción sin alertar del fraude. Se corrigió forzando la creación de un hash criptográfico SHA-256 para comparar semánticamente el cuerpo del request (Fase 3 del escenario E9).

---

## 3. ¿Qué aprendiste?

*(Sección preparada para ser diligenciada por el Líder Técnico. A continuación, se presenta un texto guía/plantilla para orientar tu redacción final)*

> **[Texto Guía para Diligenciar]**
> *   **Concurrencia a bajo nivel:** *"Al trabajar este proyecto, consolidé mi entendimiento sobre cómo los niveles de aislamiento de transacciones (`IsolationLevel.Serializable`) y los bloqueos directos en memoria (`WITH (UPDLOCK)`) son indispensables en sistemas transaccionales financieros para evitar condiciones de carrera, garantizando que el dinero nunca se duplique ni se pierda de forma fantasma en escenarios concurrentes como el E10."*
> *   **El rol del Ingeniero frente a la IA:** *"Reafirmé que los copilotos de IA son excelentes aceleradores de código repetitivo, pero carecen de visión arquitectónica contextual. Sin una supervisión humana que conozca las limitaciones de .NET Framework 4.8 y las reglas estrictas de integridad relacional en bases de datos, el código generado de forma automática introduce fallos de seguridad y rompe los contratos de negocio."*
> *   **Idempotencia en sistemas críticos:** *"Aprendí a diseñar mecanismos de idempotencia robustos basados en estados intermedios ("PENDING" ➔ "UPDATE real") para acoplar la lógica de la API con restricciones de integridad de base de datos intactas, asegurando la resiliencia del servicio ante fallas inesperadas de la red de comunicación."*