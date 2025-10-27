SELECT Id,
       Symbol,
       Timestamp,
       Side,
       Quantity,
       Price
  FROM Trades;
  
select * from signals 
where signal = 'Buy'
and 
 symbol in 
(
select symbol from Trades
)

select * from prices