select * from dbo.tb_signals 
where signal = 'buy' 

select * from dbo.tb_trades
where side = 'buy' and confidence > .7 and quality > .7  
order by quality desc

select * from dbo.tb_tradeshistory

select * from tb_prices

--delete from dbo.tb_tradeshistory
--delete from dbo.tb_trades

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