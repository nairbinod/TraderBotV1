select * from dbo.tb_signals 
where signal = 'buy' 

select * from dbo.tb_trades
where side = 'buy' and confidence > .7 and quality >= .7
order by quality desc


SELECT
   [id]
  ,[symbol]
  ,[signal_timestamp] SignalTimestamp
  ,[bar_date] BarDate 
  ,[side]
  ,[quantity]
  ,[price]
  ,[total_value] TotalValue
  ,[confidence]
  ,[quality]
  ,[created_at] CreatedAt
  ,[current_price] CurrentPrice
  ,[lastupdatedon]
FROM dbo.[tb_tradeshistory]
WHERE (isActive is null or isActive = 1) and current_price > 0
AND lower(side) = 'buy' and confidence >= .7 and quality >= .75
select * from tb_tradeshistory_details
select * from tb_prices
SELECT
   [id]
  ,[symbol]
  ,[signal_timestamp] SignalTimestamp
  ,[bar_date] BarDate 
  ,[side]
  ,[quantity]
  ,[price]
  ,[total_value] TotalValue
  ,[confidence]
  ,[quality]
  ,[created_at] CreatedAt
  ,[current_price] CurrentPrice
  ,[lastupdatedon]
FROM dbo.[tb_tradeshistory]
WHERE (isActive is null or isActive = 1) and current_price > 0
AND lower(side) = 'buy' and confidence >= .7 and quality >= .7
and [bar_date] BETWEEN '12/9/2025' AND '1/8/2026'
ORDER BY [bar_date] ASC,quality DESC,confidence DESC
/*
delete from dbo.tb_tradeshistory
delete from tb_signals
delete from dbo.tb_trades

delete from tb_tradeshistory_details

update dbo.tb_tradeshistory
set isActive = 0 
select * from dbo.tb_tradeshistory
where DATEDIFF(day,bar_date , lastupdatedon) > 12 and isActive is null 

*/

-- 
select DATEDIFF(day,bar_date , lastupdatedon), *  from 
dbo.tb_tradeshistory where isActive is null 
order by  DATEDIFF(day,bar_date , lastupdatedon) desc



select DATEDIFF(day,'10/20/2025' , getdate())
SELECT
                    [id],
                    [symbol],
                    [bar_date],
                    [quantity],
                    [price],
                    [total_value],
                    [confidence],
                    [quality],
                    [created_at]
                FROM dbo.[tb_tradeshistory]

SELECT
				d.[id],
				d.[tb_tradehistory_id] as TradeHistoryId,
				d.[bar_date] as BarDate,
				d.[signal_timestamp] as SignalTimestamp ,
				d.[price],
				d.[created_at] as CreatedAt,
				h.[symbol],
				h.[price] as RecommendedPrice,
				h.[quality],
				h.[confidence],
				h.[side]
FROM dbo.[tb_tradeshistory_details] d inner join dbo.[tb_tradeshistory] h on d.tb_tradehistory_id = h.id
WHERE d.[tb_tradehistory_id] = 7297 and abs(DATEDIFF(day,d.bar_date , getdate())) <= 20 
ORDER BY d.[bar_date] DESC

select *  from tb_subscribers

SELECT DISTINCT email FROM tb_subscribers;

SELECT STRING_AGG(email, ',') AS UniqueEmailList
						FROM (
							SELECT DISTINCT email
							FROM (select TOP 50  * from tb_subscribers order by created_at)a
						) x;
SELECT DISTINCT TOP 100 symbol 
                FROM dbo.tb_symbols 
                WHERE isActive = 1                                                                  
                ORDER BY symbol


				select * from tb_signals
				where signal = 'Buy'
