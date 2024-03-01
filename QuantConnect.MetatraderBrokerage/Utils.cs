using System;
using QuantConnect.Logging;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using QuantConnect.Brokerages;
using static System.Net.Mime.MediaTypeNames;

namespace QuantConnect.MetatraderBrokerage
{
    internal class Utils
    {
        public static void ThrowException(
            string typeName,
            string methodName,
            string message)
        {
            string text = $"{typeName}.{methodName}(): {message}";
            Log.Error(text);
            throw new BrokerageException(text);
        }
    }
}
