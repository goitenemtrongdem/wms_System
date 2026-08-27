/*
    AS/RS WMS v2 - non-destructive upgrade
    - Preserves the original three tables and all existing INFEED_ITEMS data.
    - Adds durable QR staging, outgoing orders, FEFO allocation support and
      the Item ID links needed by Dashboard / History / Transfer / Report.
*/
IF DB_ID(N'ASRS_Warehouse') IS NULL
    CREATE DATABASE ASRS_Warehouse;
GO
USE ASRS_Warehouse;
GO
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_PADDING ON;
SET ANSI_WARNINGS ON;
SET ARITHABORT ON;
SET CONCAT_NULL_YIELDS_NULL ON;
SET NUMERIC_ROUNDABORT OFF;
GO

/* Existing WMS installations already have these tables. The CREATE clauses
   allow the same script to initialise a fresh ASRS_Warehouse as well. */
IF OBJECT_ID(N'dbo.WAREHOUSE_SLOTS', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.WAREHOUSE_SLOTS (
        Id INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_WAREHOUSE_SLOTS PRIMARY KEY,
        Name VARCHAR(10) NOT NULL CONSTRAINT UQ_WAREHOUSE_SLOTS_Name UNIQUE,
        RowNo INT NOT NULL,
        ColNo INT NOT NULL,
        Status VARCHAR(20) NOT NULL CONSTRAINT DF_WAREHOUSE_SLOTS_Status DEFAULT 'EMPTY',
        SensorOccupied BIT NOT NULL CONSTRAINT DF_WAREHOUSE_SLOTS_SensorOccupied DEFAULT 0,
        RequestType VARCHAR(20) NULL,
        RequestedAt DATETIME2 NULL,
        LastSensorUpdate DATETIME2 NULL,
        UpdatedAt DATETIME2 NOT NULL CONSTRAINT DF_WAREHOUSE_SLOTS_UpdatedAt DEFAULT SYSDATETIME()
    );
END
GO

IF OBJECT_ID(N'dbo.INFEED_ITEMS', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.INFEED_ITEMS (
        Id INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_INFEED_ITEMS PRIMARY KEY,
        ItemCode NVARCHAR(100) NOT NULL,
        ProductId NVARCHAR(100) NOT NULL,
        ProductName NVARCHAR(400) NOT NULL,
        BatchNumber NVARCHAR(200) NOT NULL,
        Quantity INT NOT NULL,
        WeightKg DECIMAL(10,2) NULL,
        CompanyName NVARCHAR(400) NOT NULL,
        ManufactureDate DATE NULL,
        ExpiryDate DATE NULL,
        ReceivedBy NVARCHAR(200) NULL,
        ReceivedAt DATETIME2 NOT NULL CONSTRAINT DF_INFEED_ITEMS_ReceivedAt DEFAULT SYSDATETIME(),
        Status NVARCHAR(60) NOT NULL CONSTRAINT DF_INFEED_ITEMS_Status DEFAULT N'RECEIVED',
        CurrentSlotId INT NULL,
        QRCodeValue NVARCHAR(200) NOT NULL,
        Description NVARCHAR(1000) NULL,
        UpdatedAt DATETIME2 NOT NULL CONSTRAINT DF_INFEED_ITEMS_UpdatedAt DEFAULT SYSDATETIME(),
        QRCode NVARCHAR(1000) NULL,
        QRCodeImagePath NVARCHAR(1000) NULL
    );
END
GO

IF COL_LENGTH(N'dbo.INFEED_ITEMS', N'ParentItemId') IS NULL
    ALTER TABLE dbo.INFEED_ITEMS ADD ParentItemId INT NULL;
GO

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_INFEED_ITEMS_WAREHOUSE_SLOTS')
    ALTER TABLE dbo.INFEED_ITEMS ADD CONSTRAINT FK_INFEED_ITEMS_WAREHOUSE_SLOTS
        FOREIGN KEY (CurrentSlotId) REFERENCES dbo.WAREHOUSE_SLOTS(Id);
GO
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_INFEED_ITEMS_PARENT')
    ALTER TABLE dbo.INFEED_ITEMS ADD CONSTRAINT FK_INFEED_ITEMS_PARENT
        FOREIGN KEY (ParentItemId) REFERENCES dbo.INFEED_ITEMS(Id);
GO

IF OBJECT_ID(N'dbo.sensor_readings', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.sensor_readings (
        Id BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_sensor_readings PRIMARY KEY,
        SlotId INT NOT NULL,
        Occupied BIT NOT NULL,
        RecordedAt DATETIME2 NOT NULL CONSTRAINT DF_sensor_readings_RecordedAt DEFAULT SYSDATETIME(),
        CONSTRAINT FK_SensorReadings_WarehouseSlots FOREIGN KEY (SlotId) REFERENCES dbo.WAREHOUSE_SLOTS(Id)
    );
END
GO
IF COL_LENGTH(N'dbo.sensor_readings', N'InfeedItemId') IS NULL
    ALTER TABLE dbo.sensor_readings ADD InfeedItemId INT NULL;
GO
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_sensor_readings_INFEED_ITEMS')
    ALTER TABLE dbo.sensor_readings ADD CONSTRAINT FK_sensor_readings_INFEED_ITEMS
        FOREIGN KEY (InfeedItemId) REFERENCES dbo.INFEED_ITEMS(Id);
GO

IF OBJECT_ID(N'dbo.movement_history', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.movement_history (
        Id BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_movement_history PRIMARY KEY,
        SlotId INT NOT NULL,
        MovementType NVARCHAR(40) NOT NULL,
        Result NVARCHAR(40) NOT NULL CONSTRAINT DF_movement_history_Result DEFAULT N'COMPLETED',
        Description NVARCHAR(510) NULL,
        CreatedAt DATETIME2 NOT NULL CONSTRAINT DF_movement_history_CreatedAt DEFAULT SYSDATETIME(),
        CONSTRAINT FK_HISTORY_SLOT FOREIGN KEY (SlotId) REFERENCES dbo.WAREHOUSE_SLOTS(Id)
    );
END
GO
IF COL_LENGTH(N'dbo.movement_history', N'InfeedItemId') IS NULL
    ALTER TABLE dbo.movement_history ADD InfeedItemId INT NULL;
GO
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_movement_history_INFEED_ITEMS')
    ALTER TABLE dbo.movement_history ADD CONSTRAINT FK_movement_history_INFEED_ITEMS
        FOREIGN KEY (InfeedItemId) REFERENCES dbo.INFEED_ITEMS(Id);
GO

IF OBJECT_ID(N'dbo.inbound_queue', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.inbound_queue (
        Id BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_inbound_queue PRIMARY KEY,
        InfeedItemId INT NOT NULL,
        Status NVARCHAR(30) NOT NULL CONSTRAINT DF_inbound_queue_Status DEFAULT N'READY',
        QRValue NVARCHAR(1000) NOT NULL,
        ScanSource NVARCHAR(30) NOT NULL,
        ScannedAt DATETIME2 NOT NULL CONSTRAINT DF_inbound_queue_ScannedAt DEFAULT SYSDATETIME(),
        CaptureImagePath NVARCHAR(1000) NULL,
        TargetSlotId INT NULL,
        RequestedAt DATETIME2 NULL,
        StoredAt DATETIME2 NULL,
        FailureReason NVARCHAR(1000) NULL,
        CONSTRAINT FK_inbound_queue_INFEED_ITEMS FOREIGN KEY (InfeedItemId) REFERENCES dbo.INFEED_ITEMS(Id),
        CONSTRAINT FK_inbound_queue_WAREHOUSE_SLOTS FOREIGN KEY (TargetSlotId) REFERENCES dbo.WAREHOUSE_SLOTS(Id)
    );
END
GO

IF OBJECT_ID(N'dbo.outbound_orders', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.outbound_orders (
        Id BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_outbound_orders PRIMARY KEY,
        ProductName NVARCHAR(400) NOT NULL,
        RequestedQuantity INT NOT NULL,
        AllocatedQuantity INT NOT NULL,
        Status NVARCHAR(30) NOT NULL CONSTRAINT DF_outbound_orders_Status DEFAULT N'REQUESTED',
        RequestedAt DATETIME2 NOT NULL CONSTRAINT DF_outbound_orders_RequestedAt DEFAULT SYSDATETIME(),
        CompletedAt DATETIME2 NULL
    );
END
GO

IF OBJECT_ID(N'dbo.outbound_order_lines', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.outbound_order_lines (
        Id BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_outbound_order_lines PRIMARY KEY,
        OutboundOrderId BIGINT NOT NULL,
        InfeedItemId INT NOT NULL,
        SlotId INT NOT NULL,
        QuantityPicked INT NOT NULL,
        ResidualQuantity INT NOT NULL CONSTRAINT DF_outbound_order_lines_ResidualQuantity DEFAULT 0,
        ResidualItemId INT NULL,
        Status NVARCHAR(30) NOT NULL CONSTRAINT DF_outbound_order_lines_Status DEFAULT N'REQUESTED',
        CompletedAt DATETIME2 NULL,
        CONSTRAINT FK_outbound_order_lines_order FOREIGN KEY (OutboundOrderId) REFERENCES dbo.outbound_orders(Id),
        CONSTRAINT FK_outbound_order_lines_item FOREIGN KEY (InfeedItemId) REFERENCES dbo.INFEED_ITEMS(Id),
        CONSTRAINT FK_outbound_order_lines_slot FOREIGN KEY (SlotId) REFERENCES dbo.WAREHOUSE_SLOTS(Id),
        CONSTRAINT FK_outbound_order_lines_residual FOREIGN KEY (ResidualItemId) REFERENCES dbo.INFEED_ITEMS(Id)
    );
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_INFEED_ITEMS_CurrentSlotId' AND object_id = OBJECT_ID(N'dbo.INFEED_ITEMS'))
    CREATE UNIQUE INDEX UX_INFEED_ITEMS_CurrentSlotId ON dbo.INFEED_ITEMS(CurrentSlotId) WHERE CurrentSlotId IS NOT NULL;
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_inbound_queue_Status_ScannedAt' AND object_id = OBJECT_ID(N'dbo.inbound_queue'))
    CREATE INDEX IX_inbound_queue_Status_ScannedAt ON dbo.inbound_queue(Status, ScannedAt);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_outbound_order_lines_Status_SlotId' AND object_id = OBJECT_ID(N'dbo.outbound_order_lines'))
    CREATE INDEX IX_outbound_order_lines_Status_SlotId ON dbo.outbound_order_lines(Status, SlotId);
GO

IF NOT EXISTS (SELECT 1 FROM dbo.WAREHOUSE_SLOTS)
BEGIN
    INSERT INTO dbo.WAREHOUSE_SLOTS(Name, RowNo, ColNo, Status, SensorOccupied) VALUES
    ('R01',1,1,'EMPTY',0), ('R02',1,2,'EMPTY',0), ('R03',1,3,'EMPTY',0), ('R04',1,4,'EMPTY',0),
    ('R05',2,1,'EMPTY',0), ('R06',2,2,'EMPTY',0), ('R07',2,3,'EMPTY',0), ('R08',2,4,'EMPTY',0);
END
GO

PRINT N'AS/RS WMS v2 schema is ready. No existing data was deleted.';
