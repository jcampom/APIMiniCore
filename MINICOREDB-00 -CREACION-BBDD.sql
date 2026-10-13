-- Conectarse a la base de datos maestra
USE master;
GO

-- Validar si la base de datos ya existe antes de crearla
IF NOT EXISTS (SELECT name FROM sys.databases WHERE name = N'MiniCoreBankDb')
BEGIN
    CREATE DATABASE MiniCoreBankDb;
    PRINT 'Base de datos "MiniCoreBankDb" creada con éxito.';
END
ELSE
BEGIN
    PRINT 'La base de datos "MiniCoreBankDb" ya existe.';
END
GO