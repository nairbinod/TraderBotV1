DROP PROC dbo.ResetData
GO
Create PROC dbo.ResetData
AS
	INSERT INTO [dbo].[tb_tradeshistory]
			([id]
			,[symbol]
			,[signal_timestamp]
			,[bar_date]
			,[side]
			,[quantity]
			,[price]
			,[total_value]
			,[confidence]
			,[quality]
			,[created_at]
			,[current_price]
			,[lastupdatedon]
			,[isActive])
	SELECT [id]
		,[symbol]
		,[signal_timestamp]
		,[bar_date]
		,[side]
		,[quantity]
		,[price]
		,[total_value]
		,[confidence]
		,[quality]
		,[created_at]
		,[current_price]
		,[lastupdatedon]
		,[isActive]
	FROM [dbo].[tb_trades]
	where side = 'Buy';
	DELETE FROM [dbo].[tb_trades];
	DELETE FROM [dbo].[tb_signals];
	-- flag trades older than 20 days and stop tracking prices 
	update dbo.tb_tradeshistory
	set isActive = 0 
	where DATEDIFF(day,bar_date , lastupdatedon) > 20;
	-- flag low quality trades as inactive
	update dbo.tb_tradeshistory
	set isActive = 0 
	where (confidence <.7 or quality < .7);
	
	-- DELETE Dduplicate records 
	WITH Dups AS (
	SELECT 
		id,
		symbol,
		bar_date,
		ROW_NUMBER() OVER (
			PARTITION BY symbol, bar_date
			ORDER BY id DESC       -- keep newest id
		) AS rn
	FROM [tb_tradeshistory]
	)
	delete FROM Dups
	WHERE rn > 1;
GO
drop PROCEDURE [dbo].[InsertTradeHistoryDetail]
go
CREATE PROCEDURE [dbo].[InsertTradeHistoryDetail]
    @tradeHistoryId INT,
    @symbol NVARCHAR(50),
    @timestamp DATETIME2 NULL,
    @barDate DATETIME2 NULL,
    @price DECIMAL(18,6),
    @created_at DATETIME2
AS
BEGIN
    SET NOCOUNT ON;

	if not exists(select * from [tb_tradeshistory_details] where  [tb_tradehistory_id] = @tradeHistoryId and [symbol] = @symbol and bar_date = @barDate)
	BEGIN
		INSERT INTO [tb_tradeshistory_details] (
			 [tb_tradehistory_id],
			 [symbol],
			 [signal_timestamp],
			 [bar_date],
			 [price],
			 [created_at]
		)
		VALUES (
			 @tradeHistoryId,
			 @symbol,
			 @timestamp,
			 @barDate,
			 @price,
			 @created_at
		);
		END
END
go
