select * from dbo.tb_signals 
where signal = 'buy' 

select * from dbo.tb_trades
where side = 'buy' and confidence > .7 and quality >= .7  
order by quality desc

select * from dbo.tb_tradeshistory
select * from tb_tradeshistory_details
select * from tb_prices
/*
delete from dbo.tb_tradeshistory
delete from tb_signals
delete from dbo.tb_trades

delete from tb_tradeshistory_details

update dbo.tb_tradeshistory
set isActive = 0 
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

select * from [dbo].[tb_tradeshistory_details]

select *  from tb_subscribers

SELECT STRING_AGG(email, ',') AS UniqueEmailList
						FROM (
							SELECT DISTINCT email
							FROM tb_subscribers
						) x;

SELECT DISTINCT TOP 100 symbol 
                FROM dbo.tb_symbols 
                WHERE isActive = 1
                ORDER BY symbol
