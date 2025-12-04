ALTER PROC ResetData
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
  FROM [dbo].[tb_trades];
	DELETE FROM [dbo].[tb_trades];
	DELETE FROM [dbo].[tb_signals];
GO
