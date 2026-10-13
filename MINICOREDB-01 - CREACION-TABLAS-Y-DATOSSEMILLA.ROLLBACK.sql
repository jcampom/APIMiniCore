USE [MiniCoreBankDb ]
GO

-- =========================================================================
-- SCRIPT DE REVERSIÓN DE OBJETOS DE BASE DE DATOS (ROLLBACK)
-- Mantiene la integridad relacional eliminando dependencias de abajo hacia arriba.
-- =========================================================================

-- 1. Eliminar Índices No Agrupados de forma explícita
IF EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_Movements_Account_CreatedAt' AND object_id = OBJECT_ID('dbo.Movements'))
    DROP INDEX IX_Movements_Account_CreatedAt ON dbo.Movements;

IF EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_Transfers_DestinationAccount' AND object_id = OBJECT_ID('dbo.Transfers'))
    DROP INDEX IX_Transfers_DestinationAccount ON dbo.Transfers;

IF EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_Transfers_SourceAccount' AND object_id = OBJECT_ID('dbo.Transfers'))
    DROP INDEX IX_Transfers_SourceAccount ON dbo.Transfers;
GO

-- 2. Eliminar Tabla: Movements (Depende de Accounts y Transfers)
IF OBJECT_ID('dbo.Movements', 'U') IS NOT NULL
    DROP TABLE dbo.Movements;
GO

-- 3. Eliminar Tabla: Transfers (Depende de Accounts e IdempotencyKeys)
IF OBJECT_ID('dbo.Transfers', 'U') IS NOT NULL
    DROP TABLE dbo.Transfers;
GO

-- 4. Eliminar Tabla: Accounts (Tabla base e inmutable de forma relacional directa)
IF OBJECT_ID('dbo.Accounts', 'U') IS NOT NULL
    DROP TABLE dbo.Accounts;
GO

-- 5. Eliminar Tabla: IdempotencyKeys (Tabla base primaria de control)
IF OBJECT_ID('dbo.IdempotencyKeys', 'U') IS NOT NULL
    DROP TABLE dbo.IdempotencyKeys;
GO

-- 6. Eliminar Tabla: AuditLog (Tabla independiente y agnóstica de infraestructura)
IF OBJECT_ID('dbo.AuditLog', 'U') IS NOT NULL
    DROP TABLE dbo.AuditLog;
GO

PRINT 'Reversión completada con éxito. Todos los objetos e índices operacionales fueron removidos de MiniCoreBankDb.';
GO