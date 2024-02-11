using Newtonsoft.Json.Linq;
using QuantConnect.Brokerages;
using QuantConnect.Logging;
using QuantConnect.Orders;
using QuantConnect.Securities;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using static DWXConnect.Helpers;

namespace QuantConnect.MetatraderBrokerage.Api.FileBased
{
    internal class FileBasedMetatraderApi : MetatraderApiBase
    {
        public SymbolPropertiesDatabaseSymbolMapper SymbolMapper => throw new NotImplementedException();

        public SecurityPortfolioManager Portfolio => throw new NotImplementedException();

        public string AccountBaseCurrency { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }
        
        int _sleepDelay = 5;  // milliseconds
        int _maxRetryCommandSeconds = 10;
        bool _loadOrdersFromFile = true;
        bool _verbose = true;

        private string _metatraderFilesDirectory;

        private string _pathOrders;
        private string _pathMessages;
        private string _pathMarketData;
        private string _pathBarData;
        private string _pathHistoricData;
        private string _pathHistoricTrades;
        private string _pathOrdersStored;
        private string _pathMessagesStored;
        private string _pathCommandsPrefix;

        private int maxCommandFiles = 20;
        private int commandID = 0;
        private long lastMessagesMillis = 0;
        private string lastOpenOrdersStr = "";
        private string lastMessagesStr = "";
        private string lastMarketDataStr = "";
        private string lastBarDataStr = "";
        private string lastHistoricDataStr = "";
        private string lastHistoricTradesStr = "";

        public JObject openOrders = new JObject();
        public JObject accountInfo = new JObject();
        public JObject marketData = new JObject();
        public JObject barData = new JObject();
        public JObject historicData = new JObject();
        public JObject historicTrades = new JObject();

        private JObject lastBarData = new JObject();
        private JObject lastMarketData = new JObject();

        public bool ACTIVE = true;
        private bool START = false;

        private Thread openOrdersThread;
        private Thread messageThread;
        private Thread marketDataThread;
        private Thread barDataThread;
        private Thread historicDataThread;

        bool first = true;

        public FileBasedMetatraderApi(
            SymbolPropertiesDatabaseSymbolMapper symbolMapper,
            SecurityPortfolioManager portfolio,
            uint accountNumber,
            string filesDirectory)
            : base("Metatrader Brokerage")
        {
            _metatraderFilesDirectory = filesDirectory;

            if (!Directory.Exists(_metatraderFilesDirectory))
                throw new Exception($"Directory does not exist. metatrader-files-directory: {_metatraderFilesDirectory}");
            
            _pathOrders = Path.Join(_metatraderFilesDirectory, "DWX", "DWX_Orders.txt");
            _pathMessages = Path.Join(_metatraderFilesDirectory, "DWX", "DWX_Messages.txt");
            _pathMarketData = Path.Join(_metatraderFilesDirectory, "DWX", "DWX_Market_Data.txt");
            _pathBarData = Path.Join(_metatraderFilesDirectory, "DWX", "DWX_Bar_Data.txt");
            _pathHistoricData = Path.Join(_metatraderFilesDirectory, "DWX", "DWX_Historic_Data.txt");
            _pathHistoricTrades = Path.Join(_metatraderFilesDirectory, "DWX", "DWX_Historic_Trades.txt");
            _pathOrdersStored = Path.Join(_metatraderFilesDirectory, "DWX", "DWX_Orders_Stored.txt");
            _pathMessagesStored = Path.Join(_metatraderFilesDirectory, "DWX", "DWX_Messages_Stored.txt");
            _pathCommandsPrefix = Path.Join(_metatraderFilesDirectory, "DWX", "DWX_Commands_");

            UpdateLastMessageTimestamp();

            if (_loadOrdersFromFile)
                loadOrders();

            openOrdersThread = new Thread(() => checkOpenOrders());
            openOrdersThread.Start();

            messageThread = new Thread(() => checkMessages());
            messageThread.Start();

            marketDataThread = new Thread(() => checkMarketData());
            marketDataThread.Start();

            barDataThread = new Thread(() => checkBarData());
            barDataThread.Start();

            historicDataThread = new Thread(() => checkHistoricData());
            historicDataThread.Start();

            resetCommandIDs();

            Thread.Sleep(1000);
            start();
            onStart();
        }

        public override void Connect()
        {
            _isConnected = true;

            throw new NotImplementedException();
        }

        public override void Disconnect()
        {
            throw new NotImplementedException();
        }

        public void onStart()
        {
            // account information is stored in accountInfo.
            print("\nAccount info:\n" + accountInfo + "\n");

            // subscribe to tick data:
            string[] symbols = { "EURUSD", "GBPUSD" };
            subscribeSymbols(symbols);

            // subscribe to bar data:
            string[,] symbolsBarData = new string[,] { { "EURUSD", "M1" }, { "AUDCAD", "M5" }, { "GBPCAD", "M15" } };
            subscribeSymbolsBarData(symbolsBarData);

            // request historic data:
            long end = DateTimeOffset.Now.ToUnixTimeSeconds();
            long start = end - 10 * 24 * 60 * 60;  // last 10 days
            getHistoricData("EURUSD", "D1", start, end);

        }

        public void onTick(string symbol, double bid, double ask)
        {
            print("onTick: " + symbol + " | bid: " + bid + " | ask: " + ask);

            // print(accountInfo);
            // print(openOrders);

            // to open multiple orders:
            // if (first) {
            // 	first = false;
            // // closeAllOrders();
            // 	for (int i=0; i<5; i++) {
            // 		openOrder(symbol, "buystop", 0.05, ask+0.01, 0, 0, 77, "", 0);
            // 	}
            // }
        }

        public void onBarData(string symbol, string timeFrame, string time, double open, double high, double low, double close, int tickVolume)
        {
            print("onBarData: " + symbol + ", " + timeFrame + ", " + time + ", " + open + ", " + high + ", " + low + ", " + close + ", " + tickVolume);

            foreach (var x in historicData)
                print(x.Key + ": " + x.Value);
        }

        public void onHistoricData(String symbol, String timeFrame, JObject data)
        {

            // you can also access historic data via: historicData.keySet()
            print("onHistoricData: " + symbol + ", " + timeFrame + ", " + data);
        }

        public void onHistoricTrades()
        {
            print("onHistoricTrades: " + historicTrades);
        }


        public void onMessage(JObject message)
        {
            if (((string)message["type"]).Equals("ERROR"))
                print(message["type"] + " | " + message["error_type"] + " | " + message["description"]);
            else if (((string)message["type"]).Equals("INFO"))
                print(message["type"] + " | " + message["message"]);
        }

        public void onOrderEvent()
        {
            print("onOrderEvent: " + openOrders.Count + " open orders");

            // openOrders is a JSONObject, which can be accessed like this:
            // foreach (var x in openOrders)
            //     print(x.Key + ": " + x.Value);
        }

        /*START can be used to check if the client has been initialized.  
		*/
        public void start()
        {
            START = true;
        }


        /*Regularly checks the file for open orders and triggers
		the eventHandler.onOrderEvent() function.
		*/
        private void checkOpenOrders()
        {
            while (ACTIVE)
            {

                Thread.Sleep(_sleepDelay);

                if (!START)
                    continue;

                string text = tryReadFile(_pathOrders);

                if (text.Length == 0 || text.Equals(lastOpenOrdersStr))
                    continue;

                lastOpenOrdersStr = text;

                JObject data;

                try
                {
                    data = JObject.Parse(text);
                }
                catch
                {
                    continue;
                }

                if (data == null)
                    continue;

                JObject dataOrders = (JObject)data["orders"];

                bool newEvent = false;
                foreach (var x in openOrders)
                {
                    // JToken value = x.Value;
                    if (dataOrders[x.Key] == null)
                    {
                        newEvent = true;
                        if (_verbose)
                            print("Order removed: " + openOrders[x.Key].ToString());
                    }
                }
                foreach (var x in dataOrders)
                {
                    // JToken value = x.Value;
                    if (openOrders[x.Key] == null)
                    {
                        newEvent = true;
                        if (_verbose)
                            print("New order: " + dataOrders[x.Key].ToString());
                    }
                }

                openOrders = dataOrders;
                accountInfo = (JObject)data["account_info"];

                if (_loadOrdersFromFile)
                    tryWriteToFile(_pathOrdersStored, data.ToString());

                if (newEvent)
                    onOrderEvent();
            }
        }


        /*Regularly checks the file for messages and triggers
		the eventHandler.onMessage() function.
		*/
        private void checkMessages()
        {
            while (ACTIVE)
            {

                Thread.Sleep(_sleepDelay);

                if (!START)
                    continue;

                string text = tryReadFile(_pathMessages);

                if (text.Length == 0 || text.Equals(lastMessagesStr))
                    continue;

                lastMessagesStr = text;

                JObject data;

                try
                {
                    data = JObject.Parse(text);
                }
                catch
                {
                    continue;
                }

                if (data == null)
                    continue;

                // var sortedObj = new JObject(data.Properties().OrderByDescending(p => (int)p.Value));

                // make sure that the message are sorted so that we don't miss messages because of (millis > lastMessagesMillis).
                ArrayList millisList = new ArrayList();

                foreach (var x in data)
                {
                    if (data[x.Key] != null)
                    {
                        millisList.Add(x.Key);
                    }
                }
                millisList.Sort();
                foreach (string millisStr in millisList)
                {
                    if (data[millisStr] != null)
                    {
                        long millis = Int64.Parse(millisStr);
                        if (millis > lastMessagesMillis)
                        {
                            lastMessagesMillis = millis;
                            onMessage((JObject)data[millisStr]);
                        }
                    }
                }
                tryWriteToFile(_pathMessagesStored, data.ToString());
            }
        }


        /*Regularly checks the file for market data and triggers
		the eventHandler.onTick() function.
		*/
        private void checkMarketData()
        {
            while (ACTIVE)
            {

                Thread.Sleep(_sleepDelay);

                if (!START)
                    continue;

                string text = tryReadFile(_pathMarketData);

                if (text.Length == 0 || text.Equals(lastMarketDataStr))
                    continue;

                lastMarketDataStr = text;

                JObject data;

                try
                {
                    data = JObject.Parse(text);
                }
                catch
                {
                    continue;
                }

                if (data == null)
                    continue;

                marketData = data;

                foreach (var x in marketData)
                {
                    string symbol = x.Key;
                    if (lastMarketData[symbol] == null || !marketData[symbol].Equals(lastMarketData[symbol]))
                    {
                        // JObject jo = (JObject)marketData[symbol];
                        onTick(symbol,
                                            (double)marketData[symbol]["bid"],
                                            (double)marketData[symbol]["ask"]);
                    }
                }
                lastMarketData = data;
            }
        }


        /*Regularly checks the file for bar data and triggers
		the eventHandler.onBarData() function.
		*/
        private void checkBarData()
        {
            while (ACTIVE)
            {

                Thread.Sleep(_sleepDelay);

                if (!START)
                    continue;

                string text = tryReadFile(_pathBarData);

                if (text.Length == 0 || text.Equals(lastBarDataStr))
                    continue;

                lastBarDataStr = text;

                JObject data;

                try
                {
                    data = JObject.Parse(text);
                }
                catch
                {
                    continue;
                }

                if (data == null)
                    continue;

                barData = data;

                foreach (var x in barData)
                {
                    string st = x.Key;
                    if (lastBarData[st] == null || !barData[st].Equals(lastBarData[st]))
                    {
                        string[] stSplit = st.Split("_");
                        if (stSplit.Length != 2)
                            continue;
                        // JObject jo = (JObject)barData[symbol];
                        onBarData(stSplit[0], stSplit[1],
                            (String)barData[st]["time"],
                            (double)barData[st]["open"],
                            (double)barData[st]["high"],
                            (double)barData[st]["low"],
                            (double)barData[st]["close"],
                            (int)barData[st]["tick_volume"]);
                    }
                }
                lastBarData = data;
            }
        }


        /*Regularly checks the file for historic data and triggers
		the eventHandler.onHistoricData() function.
		*/
        private void checkHistoricData()
        {
            while (ACTIVE)
            {

                Thread.Sleep(_sleepDelay);

                if (!START)
                    continue;

                string text = tryReadFile(_pathHistoricData);

                if (text.Length > 0 && !text.Equals(lastHistoricDataStr))
                {
                    lastHistoricDataStr = text;

                    JObject data;

                    try
                    {
                        data = JObject.Parse(text);
                    }
                    catch
                    {
                        data = null;
                    }

                    if (data != null)
                    {
                        foreach (var x in data)
                        {
                            historicData[x.Key] = data[x.Key];
                        }

                        tryDeleteFile(_pathHistoricData);

                        foreach (var x in data)
                        {
                            string st = x.Key;
                            string[] stSplit = st.Split("_");
                            if (stSplit.Length != 2)
                                continue;
                            // JObject jo = (JObject)barData[symbol];
                            onHistoricData(stSplit[0], stSplit[1], (JObject)data[x.Key]);
                        }
                    }
                }

                // also check historic trades in the same thread. 
                text = tryReadFile(_pathHistoricTrades);

                if (text.Length > 0 && !text.Equals(lastHistoricTradesStr))
                {
                    lastHistoricTradesStr = text;

                    JObject data;

                    try
                    {
                        data = JObject.Parse(text);
                    }
                    catch
                    {
                        data = null;
                    }

                    if (data != null)
                    {
                        historicTrades = data;

                        tryDeleteFile(_pathHistoricTrades);

                        onHistoricTrades();
                    }
                }
            }
        }

        /*Loads stored orders from file (in case of a restart). 
		*/
        private void loadOrders()
        {

            string text = tryReadFile(_pathOrdersStored);

            if (text.Length == 0)
                return;

            JObject data;

            try
            {
                data = JObject.Parse(text);
            }
            catch
            {
                return;
            }

            if (data == null)
                return;

            lastOpenOrdersStr = text;
            openOrders = (JObject)data["orders"];
            accountInfo = (JObject)data["account_info"];
        }


        /*Loads stored messages from file (in case of a restart). 
		*/
        private void UpdateLastMessageTimestamp()
        {

            string text = tryReadFile(_pathMessagesStored);

            if (text.Length == 0)
                return;

            JObject data;

            try
            {
                data = JObject.Parse(text);
            }
            catch (Exception e)
            {
                string errorText = $"FileBasedMetatraderApi.loadMessages(): The contents of the file " + 
                    $"{_pathMessagesStored} could not be parsed as JSON object. Content: {text}";
                Log.Error(errorText);
                throw new BrokerageException(errorText);
            }

            if (data == null)
                return;

            lastMessagesStr = text;

            // here we don't have to sort because we just need the latest millis value. 
            foreach (var x in data)
            {
                long millis = Int64.Parse(x.Key);
                if (millis > lastMessagesMillis)
                    lastMessagesMillis = millis;
            }
        }


        /*Sends a SUBSCRIBE_SYMBOLS command to subscribe to market (tick) data.

		Args:
			symbols (String[]): List of symbols to subscribe to.
		
		Returns:
			null

			The data will be stored in marketData. 
			On receiving the data the eventHandler.onTick() 
			function will be triggered. 
		*/
        public void subscribeSymbols(string[] symbols)
        {
            sendCommand("SUBSCRIBE_SYMBOLS", String.Join(",", symbols));
        }


        /*Sends a SUBSCRIBE_SYMBOLS_BAR_DATA command to subscribe to bar data.

		Args:
			symbols (string[,]): List of lists containing symbol/time frame 
			combinations to subscribe to. For example:
			string[,] symbols = new string[,]{{"EURUSD", "M1"}, {"USDJPY", "H1"}};
		
		Returns:
			null

			The data will be stored in barData. 
			On receiving the data the eventHandler.onBarData() 
			function will be triggered. 
		*/
        public void subscribeSymbolsBarData(string[,] symbols)
        {
            string content = "";
            for (int i = 0; i < symbols.GetLength(0); i++)
            {
                if (i != 0) content += ",";
                content += symbols[i, 0] + "," + symbols[i, 1];
            }
            sendCommand("SUBSCRIBE_SYMBOLS_BAR_DATA", content);
        }


        /*Sends a GET_HISTORIC_DATA command to request historic data.
		
		Args:
			symbol (String): Symbol to get historic data.
			timeFrame (String): Time frame for the requested data.
			start (long): Start timestamp (seconds since epoch) of the requested data.
			end (long): End timestamp of the requested data.
		
		Returns:
			null

			The data will be stored in historicData. 
			On receiving the data the eventHandler.onHistoricData() 
			function will be triggered. 
		*/
        public void getHistoricData(String symbol, String timeFrame, long start, long end)
        {
            string content = symbol + "," + timeFrame + "," + start + "," + end;
            sendCommand("GET_HISTORIC_DATA", content);
        }



        /*Sends a GET_HISTORIC_TRADES command to request historic trades.
    
        Kwargs:
            lookbackDays (int): Days to look back into the trade history. 
		                        The history must also be visible in MT4. 
    
        Returns:
            None

            The data will be stored in historicTrades. 
            On receiving the data the eventHandler.onHistoricTrades() 
            function will be triggered. 
        */
        public void getHistoricTrades(int lookbackDays)
        {
            sendCommand("GET_HISTORIC_TRADES", lookbackDays.ToString());
        }


        /*Sends an OPEN_ORDER command to open an order.

		Args:
			symbol (String): Symbol for which an order should be opened. 
			order_type (String): Order type. Can be one of:
				'buy', 'sell', 'buylimit', 'selllimit', 'buystop', 'sellstop'
			lots (double): Volume in lots
			price (double): Price of the (pending) order. Can be zero 
				for market orders. 
			stop_loss (double): SL as absoute price. Can be zero 
				if the order should not have an SL. 
			take_profit (double): TP as absoute price. Can be zero 
				if the order should not have a TP.  
			magic (int): Magic number
			comment (String): Order comment
			expriation (long): Expiration time given as timestamp in seconds. 
				Can be zero if the order should not have an expiration time.  
		*/
        public void openOrder(string symbol, string orderType, double lots, double price, double stopLoss, double takeProfit, int magic, string comment, long expiration)
        {
            string content = symbol + "," + orderType + "," + format(lots) + "," + format(price) + "," + format(stopLoss) + "," + format(takeProfit) + "," + magic + "," + comment + "," + expiration;
            sendCommand("OPEN_ORDER", content);
        }


        /*Sends a MODIFY_ORDER command to modify an order.

		Args:
			ticket (int): Ticket of the order that should be modified.
			lots (double): Volume in lots
			price (double): Price of the (pending) order. Non-zero only 
				works for pending orders. 
			stop_loss (double): New stop loss price.
			take_profit (double): New take profit price. 
			expriation (long): New expiration time given as timestamp in seconds. 
				Can be zero if the order should not have an expiration time. 
		*/
        public void modifyOrder(int ticket, double lots, double price, double stopLoss, double takeProfit, long expiration)
        {
            string content = ticket + "," + format(lots) + "," + format(price) + "," + format(stopLoss) + "," + format(takeProfit) + "," + expiration;
            sendCommand("MODIFY_ORDER", content);
        }


        /*Sends a CLOSE_ORDER command to close an order.

		Args:
			ticket (int): Ticket of the order that should be closed.
			lots (double): Volume in lots. If lots=0 it will try to 
				close the complete position. 
		*/
        public void closeOrder(int ticket, double lots = 0)
        {
            string content = ticket + "," + format(lots);
            sendCommand("CLOSE_ORDER", content);
        }


        /*Sends a CLOSE_ALL_ORDERS command to close all orders
		with a given symbol.

		Args:
			symbol (str): Symbol for which all orders should be closed. 
		*/
        public void closeAllOrders()
        {
            sendCommand("CLOSE_ALL_ORDERS", "");
        }


        /*Sends a CLOSE_ORDERS_BY_SYMBOL command to close all orders
		with a given symbol.

		Args:
			symbol (str): Symbol for which all orders should be closed. 
		*/
        public void closeOrdersBySymbol(string symbol)
        {
            sendCommand("CLOSE_ORDERS_BY_SYMBOL", symbol);
        }


        /*Sends a CLOSE_ORDERS_BY_MAGIC command to close all orders
		with a given magic number.

		Args:
			magic (str): Magic number for which all orders should 
				be closed. 
		*/
        public void closeOrdersByMagic(int magic)
        {
            sendCommand("CLOSE_ORDERS_BY_MAGIC", magic.ToString());
        }

        /*Sends a RESET_COMMAND_IDS command to reset stored command IDs. 
        This should be used when restarting the java side without restarting 
        the mql side.
        */
        public void resetCommandIDs()
        {
            commandID = 0;

            sendCommand("RESET_COMMAND_IDS", "");

            // sleep to make sure it is read before other commands.
            Thread.Sleep(500);
        }


        /*Sends a command to the mql server by writing it to 
		one of the command files. 

		Multiple command files are used to allow for fast execution 
		of multiple commands in the correct chronological order. 
		*/
        void sendCommand(string command, string content)
        {
            // Need lock so that different threads do not use the same 
            // commandID or write at the same time.
            lock (this)
            {
                commandID = (commandID + 1) % 100000;

                string text = "<:" + commandID + "|" + command + "|" + content + ":>";

                DateTime now = DateTime.UtcNow;
                DateTime endTime = DateTime.UtcNow + new TimeSpan(0, 0, _maxRetryCommandSeconds);

                // trying again for X seconds in case all files exist or are 
                // currently read from mql side. 
                while (now < endTime)
                {
                    // using 10 different files to increase the execution speed 
                    // for muliple commands. 
                    bool success = false;
                    for (int i = 0; i < maxCommandFiles; i++)
                    {
                        string filePath = _pathCommandsPrefix + i + ".txt";
                        if (!File.Exists(filePath) && tryWriteToFile(filePath, text))
                        {
                            success = true;
                            break;
                        }
                    }
                    if (success) break;
                    Thread.Sleep(_sleepDelay);
                    now = DateTime.UtcNow;
                }
            }
        }

        public override bool PlaceOrder(Order order)
        {
            throw new NotImplementedException();
        }

        public override bool UpdateOrder(Order order)
        {
            throw new NotImplementedException();
        }

        public override bool CancelOrder(Order order)
        {
            throw new NotImplementedException();
        }

        public override List<Order> GetOpenOrders()
        {
            throw new NotImplementedException();
        }

        public override List<Holding> GetAccountHoldings()
        {
            throw new NotImplementedException();
        }

        public override List<CashAmount> GetCashBalance()
        {
            throw new NotImplementedException();
        }
    }
}
