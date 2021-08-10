using System;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Microsoft.Extensions.Logging;
using System.Collections.Generic;
using System.Linq;

namespace TurtleBot.Services
{
    public class WalletService
    {
        private readonly ILogger _logger;
        private readonly string _rpcPassword;
        private HttpClient _client;
        private string _address;
        private string _walletEndpoint;
        private string _targetEndpoint;
        private string _prepareEndpoint;
        private int _requestId;
        private long _unlocked;
        private int _code;
        private long _fee;
        ConfigModule config;
        
        public WalletService(ILoggerFactory loggerFactory, ConfigModule config)
        {
            _logger = loggerFactory.CreateLogger("wallet");
            _walletEndpoint = $"http://{config["walletdServiceAddress"]}:{config["walletdServicePort"]}";
            _rpcPassword = config["walletdRPCPassword"];
            
            _client = new HttpClient();
            _client.BaseAddress = new Uri(_walletEndpoint);
            _client.DefaultRequestHeaders.Add("X-API-KEY", _rpcPassword);
            _client.DefaultRequestHeaders.Accept.Add(
                new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));
                
            _requestId = 0;
        }

        public async Task<bool> CheckAddress(string address)
        {
            HttpResponseMessage response = await _client.GetAsync(_client.BaseAddress + "balance");
            try 
            {
                response.EnsureSuccessStatusCode();
                var resp = await response.Content.ReadAsStringAsync();
                dynamic jsonObject = JObject.Parse(resp);
                string _address = jsonObject.address;
            }
            catch (HttpRequestException)    
            {
            var _code = 7;
            }
            // Application code 7 means bad address.
            return _code != 7;
        }
        public async Task<TurtleWallet> GetFirstAddress()
        {
            HttpResponseMessage response = await _client.GetAsync(_client.BaseAddress + "balances");
            response.EnsureSuccessStatusCode();
            var resp = await response.Content.ReadAsStringAsync();
            JArray jsonArray = JArray.Parse(resp);
            dynamic response_obj= JObject.Parse(jsonArray[0].ToString());
            string _address = response_obj.address;
            return await TurtleWallet.FromString(this, _address);
        }
        public async Task<long> GetBalance(TurtleWallet wallet)
        {
            HttpResponseMessage response = await _client.GetAsync(_client.BaseAddress + "balance/" + wallet.Address);
            response.EnsureSuccessStatusCode();
            var resp = await response.Content.ReadAsStringAsync();
            dynamic jsonObject = JObject.Parse(resp);
            long _unlocked = jsonObject.unlocked;
            return (long) _unlocked;
        }
        public async Task<string> SendToMany(long amountPerWallet, long fee, IEnumerable<TurtleWallet> wallets)
        {
            var transfersString = "{ \"destinations\": [";
            transfersString += wallets.Aggregate(" ", (current, wallet) => current + $" {{ \"address\": \"{wallet.Address}\", \"amount\": {amountPerWallet} }} ,");
            transfersString = transfersString.Remove(transfersString.Length - 1);
            transfersString += " ] }";

            _targetEndpoint = _client.BaseAddress + "transactions/send/advanced";
            var requestMessage = new HttpRequestMessage(HttpMethod.Post, _targetEndpoint);
            var content = transfersString;
            requestMessage.Content = new StringContent(content, Encoding.UTF8, "application/json");
            var response = await _client.SendAsync(requestMessage);
            response.EnsureSuccessStatusCode();
            
            if (!response.IsSuccessStatusCode)
            {
                throw new Exception($"{(int) response.StatusCode} {response.ReasonPhrase}");
            }
                      
            response.EnsureSuccessStatusCode();
            var resp = await response.Content.ReadAsStringAsync();
            dynamic jsonObject = JObject.Parse(resp);
            string _transactionHash = jsonObject.transactionHash;

            return (string) _transactionHash;
        }
    }
}
