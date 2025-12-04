CREATE TABLE dbo.tb_symbols(
    symbol varchar(50) PRIMARY KEY,
    description varchar(1000),
    sector varchar(1000),
	isactive bit
  )
  go
  CREATE TABLE dbo.tb_prices (
                    id INTEGER PRIMARY KEY IDENTITY(1,1),
                    symbol varchar(50) NOT NULL,
                    timestamp datetime NOT NULL,
                    [open] float NOT NULL,
                    high float NOT NULL,
                    [low] float NOT NULL,
                    [close] float NOT NULL,
                    volume int NOT NULL,
                    created_at datetime default getdate()
                )
				go
				CREATE TABLE dbo.tb_signals (
                    id INTEGER PRIMARY KEY IDENTITY(1,1),
                    symbol varchar(50) NOT NULL,
                    timestamp datetime NOT NULL,
                    strategy varchar(1000) NOT NULL,
                    signal varchar(50)  NOT NULL,
                    reason varchar(1000),
                    created_at datetime default getdate()
                )
				go

				CREATE TABLE dbo.tb_trades (
                    id INTEGER PRIMARY KEY IDENTITY(1,1),
                    symbol varchar(50) NOT NULL,
                    signal_timestamp datetime NOT NULL,
                    bar_date datetime,
                    side varchar(50)  NOT NULL,
                    quantity INTEGER NOT NULL,
                    price float NOT NULL,
                    total_value float,
					confidence float,
					quality float,
                    created_at datetime default getdate(),
					current_price float,
					lastupdatedon DATETIME,
					isActive BIT null
                )
				go

--DROP TABLE  [dbo].[tb_tradeshistory]				
CREATE TABLE [dbo].[tb_tradeshistory](
	[id] [int] NOT NULL PRIMARY KEY  ,
	[symbol] [varchar](50) NOT NULL,
	[signal_timestamp] [datetime] NOT NULL,
	[bar_date] [datetime] NULL,
	[side] [varchar](50) NOT NULL,
	[quantity] [int] NOT NULL,
	[price] [float] NOT NULL,
	[total_value] [float] NULL,
	[confidence] [float] NULL,
	[quality] [float] NULL,
	[created_at] [datetime] NULL,
	[current_price] [float] NULL,
	[lastupdatedon] [datetime] NULL,
	isActive BIT null
) ON [PRIMARY]
GO



CREATE TABLE dbo.tb_tradeshistory_details (
    id INTEGER PRIMARY KEY IDENTITY(1,1),
    tb_tradehistory_id int,
	symbol varchar(50) NOT NULL,
    signal_timestamp datetime NOT NULL,
    bar_date datetime,
    price float NOT NULL,
    created_at datetime default getdate()
	)