using QuantConnect.Brokerages;
using QuantConnect.Data;
using QuantConnect.Interfaces;
using QuantConnect.Packets;
using QuantConnect.Securities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace QuantConnect.MetatraderBrokerage.Api
{
    public abstract class MetatraderApiBase : Brokerage, IDataQueueHandler
    {
        public SymbolPropertiesDatabaseSymbolMapper SymbolMapper { get; }
        public SecurityPortfolioManager Portfolio { get; }

        /// <summary>
        /// Returns true if we're currently connected to the broker
        /// </summary>
        public override bool IsConnected => _isConnected; //&&
            //!TransactionsConnectionHandler.IsConnectionLost &&
            //!PricingConnectionHandler.IsConnectionLost;

        protected bool _isConnected;

        public override string AccountBaseCurrency { get; protected set; }
        //public override Action<object, BrokerageMessageEvent> Message { get; set; }
        //Action<object, object> OrdersStatusChanged { get; set; }
        //Action<object, object> AccountChanged { get; set; }

        public MetatraderApiBase(string name) : base(name)
        {
        }

        public static MetatraderApiBase Create(
            string apiName, 
            SymbolPropertiesDatabaseSymbolMapper symbolMapper, 
            SecurityPortfolioManager portfolio,
            Dictionary<string, object> apiParameters)
        {
            var accountID = (uint)apiParameters["metatrader-account-id"];

            switch (apiName)
            {
                case "FileBasedMetatraderApi":
                    string exchangeDirectory = (string)apiParameters["metatrader-exchange-directory"];
                    return new FileBased.FileBasedMetatraderApi(symbolMapper, portfolio, accountID, exchangeDirectory);
                default:
                    throw new NotImplementedException();
            }
        }

        public IEnumerator<BaseData> Subscribe(SubscriptionDataConfig dataConfig, EventHandler newDataAvailableHandler)
        {
            throw new NotImplementedException();
        }

        public void Unsubscribe(SubscriptionDataConfig dataConfig)
        {
            throw new NotImplementedException();
        }

        public void SetJob(LiveNodePacket job)
        {
            throw new NotImplementedException();
        }
    }
}
