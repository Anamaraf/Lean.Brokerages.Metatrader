/*
 * QUANTCONNECT.COM - Democratizing Finance, Empowering Individuals.
 * Lean Algorithmic Trading Engine v2.0. Copyright 2014 QuantConnect Corporation.
 *
 * Licensed under the Apache License, Version 2.0 (the "License");
 * you may not use this file except in compliance with the License.
 * You may obtain a copy of the License at http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
*/

using System;
using System.Linq;
using QuantConnect.Data;
using QuantConnect.Util;
using QuantConnect.Orders;
using QuantConnect.Packets;
using QuantConnect.Interfaces;
using QuantConnect.Securities;
using QuantConnect.Brokerages;
using System.Collections.Generic;
using QuantConnect.MetatraderBrokerage;
using QuantConnect.MetatraderBrokerage.Api;
using QuantConnect.Api;

namespace QuantConnect.MetatraderBrokerage
{
    [BrokerageFactory(typeof(MetatraderBrokerageFactory))]
    public class MetatraderBrokerage : Brokerage, IDataQueueHandler, IDataQueueUniverseProvider
    {
        private IDataAggregator _aggregator;
        
        /// <summary>
        /// Count subscribers for each (symbol, tickType) combination
        /// </summary>
        private EventBasedDataQueueHandlerSubscriptionManager _subscriptionManager;

        /// <summary>
        /// Live job task packet: container for any live specific job variables
        /// </summary>
        private LiveNodePacket _job;

        /// <summary>
        /// Provide data from external algorithm
        /// </summary>
        private IAlgorithm _algorithm;

        /// <summary>
        /// Represents an instance of the Metatrader API.
        /// </summary>
        private MetatraderApiBase _api;

        /// <summary>
        ///  Provides the mapping between Lean symbols and brokerage symbols
        /// </summary>
        private SymbolPropertiesDatabaseSymbolMapper _symbolMapper;

        /// <summary>
        /// Represents the name of the market associated with the application.
        /// </summary>
        private static string MarketName;
        
        /// <summary>
        /// Order provider
        /// </summary>
        protected IOrderProvider OrderProvider { get; private set; }

        ///// <summary>
        ///// Initializes a new instance of the <see cref="MetatraderBrokerage"/> class.
        ///// </summary>
        //public MetatraderBrokerage() : base("Metatrader Brokerage")
        //{
        //}

        /// <summary>
        /// Initializes a new instance of the <see cref="MetatraderBrokerage"/> class.
        /// </summary>
        /// <param name="job"></param>
        /// <param name="algorithm"></param>
        /// <param name="aggregator"></param>
        /// <param name="marketName"></param>
        /// <param name="apiName"></param>
        /// <param name="apiParameters"></param>
        public MetatraderBrokerage(
            LiveNodePacket job,
            IAlgorithm algorithm, 
            IDataAggregator aggregator,
            string marketName,
            string apiName,
            Dictionary<string, object> apiParameters)
            : base("Metatrader Brokerage")
        {
            _job = job;
            _algorithm = algorithm;
            _aggregator = aggregator;
            _symbolMapper = new SymbolPropertiesDatabaseSymbolMapper(marketName);
            OrderProvider = algorithm.Transactions;

            _subscriptionManager = new EventBasedDataQueueHandlerSubscriptionManager();
            _subscriptionManager.SubscribeImpl += (s, t) => Subscribe(s);
            _subscriptionManager.UnsubscribeImpl += (s, t) => Unsubscribe(s);

            //_api = new OandaRestApiV20(_symbolMapper, orderProvider, securityProvider, aggregator, environment, accessToken, accountId, agent);
            _api = MetatraderApiBase.Create(apiName, _symbolMapper, algorithm?.Portfolio, apiParameters);
            
            // forward events received from API
            _api.OrdersStatusChanged += (sender, orderEvents) => OnOrderEvents(orderEvents);
            _api.AccountChanged += (sender, accountEvent) => OnAccountChanged(accountEvent);
            _api.Message += (sender, messageEvent) => OnMessage(messageEvent);

        }

        #region IDataQueueHandler

        /// <summary>
        /// Subscribe to the specified configuration
        /// </summary>
        /// <param name="dataConfig">defines the parameters to subscribe to a data feed</param>
        /// <param name="newDataAvailableHandler">handler to be fired on new data available</param>
        /// <returns>The new enumerator for this subscription request</returns>
        public IEnumerator<BaseData> Subscribe(SubscriptionDataConfig dataConfig, EventHandler newDataAvailableHandler)
        {
            if (!CanSubscribe(dataConfig.Symbol))
            {
                return null;
            }

            var enumerator = _aggregator.Add(dataConfig, newDataAvailableHandler);
            _subscriptionManager.Subscribe(dataConfig);

            return enumerator;
        }

        /// <summary>
        /// Removes the specified configuration
        /// </summary>
        /// <param name="dataConfig">Subscription config to be removed</param>
        public void Unsubscribe(SubscriptionDataConfig dataConfig)
        {
            _subscriptionManager.Unsubscribe(dataConfig);
            _aggregator.Remove(dataConfig);
        }

        /// <summary>
        /// Sets the job we're subscribing for
        /// </summary>
        /// <param name="job">Job we're subscribing for</param>
        public void SetJob(LiveNodePacket job)
        {
            throw new NotImplementedException();
        }

        #endregion

        #region Brokerage

        /// <summary>
        /// Gets all open orders on the account.
        /// NOTE: The order objects returned do not have QC order IDs.
        /// </summary>
        /// <returns>The open orders returned from IB</returns>
        public override List<Order> GetOpenOrders()
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// Gets all holdings for the account
        /// </summary>
        /// <returns>The current holdings from the account</returns>
        public override List<Holding> GetAccountHoldings()
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// Gets the current cash balance for each currency held in the brokerage account
        /// </summary>
        /// <returns>The current cash balance for each currency available for trading</returns>
        public override List<CashAmount> GetCashBalance()
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// Places a new order and assigns a new broker ID to the order
        /// </summary>
        /// <param name="order">The order to be placed</param>
        /// <returns>True if the request for a new order has been placed, false otherwise</returns>
        public override bool PlaceOrder(Order order)
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// Updates the order with the same id
        /// </summary>
        /// <param name="order">The new order information</param>
        /// <returns>True if the request was made for the order to be updated, false otherwise</returns>
        public override bool UpdateOrder(Order order)
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// Cancels the order with the specified ID
        /// </summary>
        /// <param name="order">The order to cancel</param>
        /// <returns>True if the request was made for the order to be canceled, false otherwise</returns>
        public override bool CancelOrder(Order order)
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// Returns true if we're currently connected to the broker
        /// </summary>
        public override bool IsConnected => _api.IsConnected;
        
        /// <summary>
        /// Returns the brokerage account's base currency
        /// </summary>
        public override string AccountBaseCurrency => _api.AccountBaseCurrency;

        /// <summary>
        /// Connects the client to the broker's Api
        /// </summary>
        public override void Connect()
        {
            if (IsConnected) return;

            _api.Connect();
        }

        /// <summary>
        /// Disconnects the client from the broker's Api
        /// </summary>
        public override void Disconnect()
        {
            _api.Disconnect();
        }
        #endregion

        #region IDataQueueUniverseProvider

        /// <summary>
        /// Method returns a collection of Symbols that are available at the data source.
        /// </summary>
        /// <param name="symbol">Symbol to lookup</param>
        /// <param name="includeExpired">Include expired contracts</param>
        /// <param name="securityCurrency">Expected security currency(if any)</param>
        /// <returns>Enumerable of Symbols, that are associated with the provided Symbol</returns>
        public IEnumerable<Symbol> LookupSymbols(Symbol symbol, bool includeExpired, string securityCurrency = null)
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// Returns whether selection can take place or not.
        /// </summary>
        /// <remarks>This is useful to avoid a selection taking place during invalid times, for example IB reset times or when not connected,
        /// because if allowed selection would fail since IB isn't running and would kill the algorithm</remarks>
        /// <returns>True if selection can take place</returns>
        public bool CanPerformSelection()
        {
            throw new NotImplementedException();
        }

        #endregion

        private bool CanSubscribe(Symbol symbol)
        {
            if (symbol.Value.IndexOfInvariant("universe", true) != -1 || symbol.IsCanonical())
            {
                return false;
            }

            throw new NotImplementedException();
        }

        /// <summary>
        /// Adds the specified symbols to the subscription
        /// </summary>
        /// <param name="symbols">The symbols to be added keyed by SecurityType</param>
        private bool Subscribe(IEnumerable<Symbol> symbols)
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// Removes the specified symbols to the subscription
        /// </summary>
        /// <param name="symbols">The symbols to be removed keyed by SecurityType</param>
        private bool Unsubscribe(IEnumerable<Symbol> symbols)
        {
            throw new NotImplementedException();
        }
    }
}
