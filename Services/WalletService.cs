using System;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using System.Collections.Generic;
using System.Linq;

namespace TurtleBot.Services
{
    public class WalletService
    {
        private readonly ILogger _logger;
        private readonly string _rpcPassword;
        private HttpClient _client;       
        private string _walletEndpoint;
        private string _targetEndpoint;
        
        public WalletService(ILoggerFactory loggerFactory, ConfigModule config)
        {
            _logger = loggerFactory.CreateLogger("wallet");
            _walletEndpoint = $"http://{config["walletapiServiceAddress"]}:{config["walletapiServicePort"]}";
            _rpcPassword = config["walletapiRPCPassword"];
            
            _client = new HttpClient();
            _client.BaseAddress = new Uri(_walletEndpoint);
            _client.DefaultRequestHeaders.Add("X-API-KEY", _rpcPassword);
            _client.DefaultRequestHeaders.Accept.Add(
                new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));
                
        }

       public async Task<bool> CheckAddress(string address)
        {
            int return_code = 1;
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
             return_code = 7;
            }
            // Application code 7 means bad address.
            return return_code != 7;
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
            Console.WriteLine(content);
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
