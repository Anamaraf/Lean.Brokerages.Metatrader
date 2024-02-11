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
using QuantConnect.Packets;
using QuantConnect;
using QuantConnect.Brokerages;
using QuantConnect.Configuration;
using QuantConnect.Interfaces;
using QuantConnect.Securities;
using System.Collections.Generic;
using QuantConnect.Data;
using QuantConnect.Util;

namespace QuantConnect.MetatraderBrokerage
{
    /// <summary>
    /// Provides a template implementation of BrokerageFactory
    /// </summary>
    public class MetatraderBrokerageFactory : BrokerageFactory
    {
        /// <summary>
        /// Gets the brokerage data required to run the brokerage from configuration/disk
        /// </summary>
        /// <remarks>
        /// The implementation of this property will create the brokerage data dictionary required for
        /// running live jobs. See <see cref="IJobQueueHandler.NextJob"/>
        /// </remarks>
        public override Dictionary<string, string> BrokerageData => new Dictionary<string, string>
        {
            { "metatrader-market", Config.Get("metatrader-market") },
            { "metatrader-api", Config.Get("metatrader-api") },
            { "metatrader-account-id", Config.Get("metatrader-account-id") },
            { "metatrader-files-directory", Config.Get("metatrader-files-directory") },
        };

        /// <summary>
        /// Initializes a new instance of the <see cref="MetatraderBrokerageFactory"/> class
        /// </summary>
        public MetatraderBrokerageFactory() : base(typeof(MetatraderBrokerage))
        {
        }

        /// <summary>
        /// Gets a brokerage model that can be used to model this brokerage's unique behaviors
        /// </summary>
        /// <param name="orderProvider">The order provider</param>
        public override IBrokerageModel GetBrokerageModel(IOrderProvider orderProvider)
        {
            return new MetatraderBrokerageModel();
        }

        /// <summary>
        /// Creates a new IBrokerage instance
        /// </summary>
        /// <param name="job">The job packet to create the brokerage for</param>
        /// <param name="algorithm">The algorithm instance</param>
        /// <returns>A new brokerage instance</returns>
        public override IBrokerage CreateBrokerage(LiveNodePacket job, IAlgorithm algorithm)
        {
            var errors = new List<string>();
            var parameters = new Dictionary<string, object>();

            // read values from the brokerage data
            var market = Read<string>(job.BrokerageData, "metatrader-market", errors);
            var apiName = Read<string>(job.BrokerageData, "metatrader-api", errors);
            parameters["metatrader-account-id"] = Read<uint>(job.BrokerageData, "metatrader-account-id", errors);
            parameters["metatrader-files-directory"] = Read<string>(job.BrokerageData, "metatrader-files-directory", errors);
            
            if (errors.Count != 0)
            {
                // if we had errors then we can't create the instance
                throw new Exception(string.Join(System.Environment.NewLine, errors));
            }

            var brokerage = new MetatraderBrokerage(
                job,
                algorithm,
                Composer.Instance.GetExportedValueByTypeName<IDataAggregator>(Config.Get("data-aggregator", "QuantConnect.Lean.Engine.DataFeeds.AggregationManager"), forceTypeNameOnExisting: false),
                market,
                apiName,
                parameters);
            Composer.Instance.AddPart<IDataQueueHandler>(brokerage);

            return brokerage;
        }

        /// <summary>
        /// Performs application-defined tasks associated with freeing, releasing, or resetting unmanaged resources.
        /// </summary>
        public override void Dispose()
        {
            throw new NotImplementedException();
        }
    }
}