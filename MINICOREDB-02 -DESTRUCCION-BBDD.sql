-- Conectarse a la base de datos maestra (obligatorio para poder borrar otra base de datos)
USE master;
GO

-- Validar si la base de datos existe antes de intentar borrarla
IF EXISTS (SELECT name FROM sys.databases WHERE name = N'MiniCoreBankDb')
BEGIN
    -- Forzar el cierre de todas las conexiones activas inmediatamente
    ALTER DATABASE MiniCoreBankDb SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
    
    -- Eliminar la base de datos y sus archivos físicos (.mdf y .ldf)
    DROP DATABASE MiniCoreBankDb;
    
    PRINT 'Base de datos "MiniCoreBankDb" eliminada de forma permanente.';
END
ELSE
BEGIN
    PRINT 'La base de datos "MiniCoreBankDb" no existe.';
END
GO