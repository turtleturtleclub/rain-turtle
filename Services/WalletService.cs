using System;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using System.Collections.Generic;

namespace TurtleBot.Services
{
    public class WalletService
    {
        private readonly ILogger _logger;
        private readonly HttpClient _client;
        private readonly string _walletEndpoint;

        private int _requestId;

        public WalletService(ILoggerFactory loggerFactory, IConfiguration config)
        {
            _logger = loggerFactory.CreateLogger("wallet");
            _client = new HttpClient();
            _walletEndpoint = $"http://{config["walletdServiceAddress"]}:{config["walletdServicePort"]}/json_rpc";

            _requestId = 0;
        }

        public async Task<bool> CheckAddress(string address)
        {
            JObject response = await SendRPCRequest("getBalance", $"{{\"address\":\"{address}\"}}");
            JToken errorToken;

            // If there is no |error| value, the address is a bot address, so it is valid.
            if (!response.TryGetValue("error", out errorToken)) return true;

            int applicationCode = (int)errorToken["data"]["application_code"];

            // Application code 7 means bad address.
            return applicationCode != 7;
        }

        public async Task<TurtleWallet> GetFirstAddress()
        {
            JObject response = await SendRPCRequest("getAddresses");
            string firstAddress = (string)response["result"]["addresses"][0];

            return await TurtleWallet.FromString(this, firstAddress);
        }

        public async Task<long> GetBalance(TurtleWallet address)
        {
            JObject response = await SendRPCRequest("getBalance", $"{{\"address\":\"{address.Address}\"}}");

            return (long)response["result"]["availableBalance"];
        }

        public async Task<string> SendToMany(long amountPerWallet, long fee, IEnumerable<TurtleWallet> wallets)
        {
            string transfersString = "[";
            foreach (var wallet in wallets)
            {
                transfersString += $"{{\"amount\":{amountPerWallet}, \"address\":\"{wallet.Address}\"}},";
            }
            transfersString = transfersString.Remove(transfersString.Length - 1);
            transfersString += "]";
            
            JObject response = await SendRPCRequest("sendTransaction", $"{{\"fee\":{fee}, \"anonymity\":{0}, \"transfers\":{transfersString}}}");

            return (string)response["result"]["transactionHash"];
        }

        private async Task<JObject> SendRPCRequest(string method, string parameters = "{}")
        {
            HttpRequestMessage requestMessage = new HttpRequestMessage(HttpMethod.Post, _walletEndpoint);
            string content = $"{{ \"jsonrpc\":\"2.0\", \"method\":\"{method}\", \"params\":{parameters}, \"id\":{_requestId++} }}";
            requestMessage.Content = new StringContent(content, Encoding.UTF8, "application/json");
            HttpResponseMessage response = await _client.SendAsync(requestMessage);

            if (response.IsSuccessStatusCode)
            {
                string responseString = await response.Content.ReadAsStringAsync();
                return JObject.Parse(responseString);
            }
            else
            {
                throw new Exception($"{(int)response.StatusCode} {response.ReasonPhrase}");
            }
        }
    }
}