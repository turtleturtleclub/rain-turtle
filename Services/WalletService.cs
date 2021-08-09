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
        private HttpClient _client;
        private readonly string _rpcPassword;
        private string _address;
        private string _walletEndpoint;
        private int _requestId;
        private long _unlocked;
        private int _code;

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
            HttpResponseMessage response = await _client.GetAsync(_client.BaseAddress + "balance");
            response.EnsureSuccessStatusCode();
            var resp = await response.Content.ReadAsStringAsync();
            Console.WriteLine(resp);
            dynamic jsonObject = JObject.Parse(resp);
            string _address = jsonObject.address;
            return await TurtleWallet.FromString(this, _address);
        }
        public async Task<long> GetBalance(TurtleWallet wallet)
        {
            HttpResponseMessage response = await _client.GetAsync(_client.BaseAddress + "balance/" + wallet.Address);
            response.EnsureSuccessStatusCode();
            var resp = await response.Content.ReadAsStringAsync();
            Console.WriteLine(resp);
            dynamic jsonObject = JObject.Parse(resp);
            long _unlocked = jsonObject.unlocked;
            return (long) _unlocked;
        }

        public async Task<string> SendToMany(long amountPerWallet, long fee, IEnumerable<TurtleWallet> wallets)
        {
            var transfersString = wallets.Aggregate("[", (current, wallet) => current + $"{{\"amount\":{amountPerWallet}, \"address\":\"{wallet.Address}\"}},");
            transfersString = transfersString.Remove(transfersString.Length - 1);
            transfersString += "]";
            
            var response = await SendRPCRequest("sendTransaction", $"{{\"fee\":{fee}, \"anonymity\":{0}, \"transfers\":{transfersString}}}");
            return (string)response["result"]["transactionHash"];
        }
        
        private async Task<JObject> SendRPCRequest(string method, string parameters = "{}")
        {
            var requestMessage = new HttpRequestMessage(HttpMethod.Post, _walletEndpoint);
            var content = $"{{ \"jsonrpc\":\"2.0\", \"method\":\"{method}\", \"params\":{parameters}, \"password\":\"{_rpcPassword}\", \"id\":{_requestId++} }}";
            requestMessage.Content = new StringContent(content, Encoding.UTF8, "application/json");
            Console.WriteLine(requestMessage);
            var response = await _client.SendAsync(requestMessage);
            Console.WriteLine(response);
            if (!response.IsSuccessStatusCode)
            {
                throw new Exception($"{(int) response.StatusCode} {response.ReasonPhrase}");
            }

            var responseString = await response.Content.ReadAsStringAsync();
            return JObject.Parse(responseString);
        }
    }
}
