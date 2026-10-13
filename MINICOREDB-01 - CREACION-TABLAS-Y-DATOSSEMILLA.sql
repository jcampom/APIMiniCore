USE [MiniCoreBankDb ]
GO

-- =========================================================================
-- 1. CREACIÓN DE TABLAS Y RESTRICCIONES (DDL)
-- =========================================================================

CREATE TABLE IdempotencyKeys (
    [Key] UNIQUEIDENTIFIER NOT NULL,
    ClientId VARCHAR(50) NOT NULL,
    RequestHash CHAR(64) NOT NULL,
    ResponseStatus INT NOT NULL,
    ResponseBody NVARCHAR(MAX) NOT NULL,
    CreatedAtUtc DATETIME2(7) NOT NULL CONSTRAINT DF_IdempotencyKeys_CreatedAtUtc DEFAULT SYSUTCDATETIME(),
    CONSTRAINT PK_IdempotencyKeys PRIMARY KEY CLUSTERED ([Key])
);

CREATE TABLE Accounts (
    Id VARCHAR(20) NOT NULL, -- Cambiado a VARCHAR para soportar el formato 'ACC-1001' del contrato
    OwnerId INT NOT NULL,
    HolderName NVARCHAR(150) NOT NULL,
    [Status] VARCHAR(20) NOT NULL,
    Balance DECIMAL(18,2) NOT NULL CONSTRAINT DF_Accounts_Balance DEFAULT 0.00,
    Currency CHAR(3) NOT NULL,
    RowVersion ROWVERSION NOT NULL,
    CONSTRAINT PK_Accounts PRIMARY KEY CLUSTERED (Id),
    CONSTRAINT CK_Accounts_Balance CHECK (Balance >= 0)
);

CREATE TABLE Transfers (
    Id BIGINT IDENTITY(1,1) NOT NULL,
    SourceAccountId VARCHAR(20) NOT NULL,
    DestinationAccountId VARCHAR(20) NOT NULL,
    Amount DECIMAL(18,2) NOT NULL,
    Fee DECIMAL(18,2) NOT NULL CONSTRAINT DF_Transfers_Fee DEFAULT 0.00,
    [Status] TINYINT NOT NULL, -- 1: Completed, 2: Reversed
    CreatedAtUtc DATETIME2(7) NOT NULL CONSTRAINT DF_Transfers_Utc DEFAULT SYSUTCDATETIME(),
    ReversedAtUtc DATETIME2(7) NULL,
    IdempotencyKey UNIQUEIDENTIFIER NOT NULL,
    CONSTRAINT PK_Transfers PRIMARY KEY CLUSTERED (Id),
    CONSTRAINT FK_Transfers_SourceAccount FOREIGN KEY (SourceAccountId) REFERENCES Accounts(Id),
    CONSTRAINT FK_Transfers_DestinationAccount FOREIGN KEY (DestinationAccountId) REFERENCES Accounts(Id),
    CONSTRAINT FK_Transfers_IdempotencyKey FOREIGN KEY (IdempotencyKey) REFERENCES IdempotencyKeys([Key]),
    CONSTRAINT CK_Transfers_Amount CHECK (Amount > 0),
    CONSTRAINT CK_Transfers_Fee CHECK (Fee >= 0),
    CONSTRAINT CK_Transfers_DistinctAccounts CHECK (SourceAccountId <> DestinationAccountId)
);

CREATE TABLE Movements (
    Id BIGINT IDENTITY(1,1) NOT NULL,
    AccountId VARCHAR(20) NOT NULL,
    TransferId BIGINT NULL,
    [Type] TINYINT NOT NULL,
    Amount DECIMAL(18,2) NOT NULL,
    BalanceAfter DECIMAL(18,2) NOT NULL,
    CreatedAtUtc DATETIME2(7) NOT NULL CONSTRAINT DF_Movements_Utc DEFAULT SYSUTCDATETIME(),
    CONSTRAINT PK_Movements PRIMARY KEY CLUSTERED (Id),
    CONSTRAINT FK_Movements_Account FOREIGN KEY (AccountId) REFERENCES Accounts(Id),
    CONSTRAINT FK_Movements_Transfer FOREIGN KEY (TransferId) REFERENCES Transfers(Id),
    CONSTRAINT CK_Movements_Amount CHECK (Amount > 0)
);

CREATE TABLE AuditLog (
    Id BIGINT IDENTITY(1,1) NOT NULL,
    TimestampUtc DATETIME2(7) NOT NULL CONSTRAINT DF_AuditLog_Utc DEFAULT SYSUTCDATETIME(),
    ActorId VARCHAR(50) NOT NULL,
    [Action] VARCHAR(100) NOT NULL,
    EntityId VARCHAR(50) NOT NULL,
    CorrelationId UNIQUEIDENTIFIER NOT NULL,
    Details NVARCHAR(MAX) NOT NULL,
    CONSTRAINT PK_AuditLog PRIMARY KEY CLUSTERED (Id)
);

-- Índices de rendimiento
CREATE NONCLUSTERED INDEX IX_Transfers_SourceAccount ON Transfers(SourceAccountId);
CREATE NONCLUSTERED INDEX IX_Transfers_DestinationAccount ON Transfers(DestinationAccountId);
CREATE NONCLUSTERED INDEX IX_Movements_Account_CreatedAt ON Movements(AccountId, CreatedAtUtc DESC);
GO

-- =========================================================================
-- 2. INSERCIÓN DE DATOS SEMILLA BANCARIOS OBLIGATORIOS
-- =========================================================================

INSERT INTO Accounts (Id, OwnerId, HolderName, [Status], Balance, Currency) VALUES
('ACC-1001', 1, 'Ana Torres (CUST-01)', 'Activa', 10000.00, 'USD'),
('ACC-1002', 2, 'Luis Pérez (CUST-02)', 'Activa', 250.00, 'USD'),
('ACC-1003', 3, 'Carmen Ruiz (CUST-03)', 'Bloqueada', 5000.00, 'USD'),
('ACC-1004', 4, 'Empresa XYZ (CUST-04)', 'Activa', 120000.00, 'USD'),
('ACC-1005', 5, 'Cuenta histórica (CUST-05)', 'Cerrada', 0.00, 'USD'),
('ACC-1006', 6, 'Pedro Gil (CUST-06)', 'Activa', 501.00, 'USD');
GO