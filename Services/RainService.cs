using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Discord.WebSocket;
using Discord;
using System.Collections.Concurrent;
using Microsoft.Extensions.Configuration;

namespace TurtleBot.Services
{
    public class RainService
    {
        private readonly ILogger _logger;
        private readonly DiscordSocketClient _discord;
        private readonly WalletService _walletService;
        private readonly IConfiguration _config;
        
        private ulong _channelId;
        private TimeSpan _checkInterval;
        private TimeSpan _announceDelay;
        private TimeSpan _registerDelay;
        private Task _checkTask;
        private CancellationTokenSource _checkCanellationTokenSource;
        private ConcurrentDictionary<ulong, TurtleWallet> _wallets;

        public RainServiceState State { get; private set; }
        public long BalanceThreshold { get; private set; }
        public TurtleWallet BotWallet { get; private set; }

        public RainService(ILoggerFactory loggerFactory, DiscordSocketClient discord, WalletService walletService, IConfiguration config)
        {
            _logger = loggerFactory.CreateLogger("rain");
            _discord = discord;
            _walletService = walletService;
            _config = config;

            _channelId = Convert.ToUInt64(config["rainChannelId"]);
            _checkInterval = new TimeSpan(0, 0, Convert.ToInt32(config["rainCheckIntervalS"]));
            _announceDelay = new TimeSpan(0, 0, Convert.ToInt32(config["rainAnnounceDelayS"]));
            _registerDelay = new TimeSpan(0, 0, Convert.ToInt32(config["rainRegisterDelayS"]));
            _checkCanellationTokenSource = new CancellationTokenSource();
            _wallets = new ConcurrentDictionary<ulong, TurtleWallet>();

            // The rain service has to be started explicitly (with ``!rain start``)
            State = RainServiceState.Stopped;
            BalanceThreshold = Convert.ToInt64(config["rainBalanceThreshold"]);

            _discord.MessageReceived += MessageReceived;
        }

        public async void Start()
        {
            try {
                if (State == RainServiceState.Stopped)
                {
                    BotWallet = await _walletService.GetFirstAddress();
                    _checkCanellationTokenSource = new CancellationTokenSource();
                    _checkTask = Task.Run(() => CheckBalanceLoop(_checkInterval, _checkCanellationTokenSource.Token));
                }
            } catch (Exception e) {
                State = RainServiceState.Stopped;
                _logger.LogCritical(e, "Exception in Start (probably in the check loop)");
            }
        }

        public async void Stop()
        {
            if (State == RainServiceState.CheckingBalance)
            {
                _checkCanellationTokenSource.Cancel();
                await _checkTask;
            }
        }

        private async Task MessageReceived(SocketMessage rawMessage)
        {
            // Ignore system messages and messages from bots
            if (!(rawMessage is SocketUserMessage message)) return;
            if (message.Source != MessageSource.User) return;
            if (!(message.Channel is SocketDMChannel)) return;

            if (State != RainServiceState.AcceptingRegistrations)
            {
                await rawMessage.Author.SendMessageAsync("Huh, it doesn't look like it is raining soon...");
                return;
            }

            if (_wallets.ContainsKey(message.Author.Id))
            {
                await rawMessage.Author.SendMessageAsync("You are already registered, little turtle.");
                return;
            }

            string address = message.ToString();
            TurtleWallet wallet = await TurtleWallet.FromString(_walletService, address);

            if (wallet == null)
            {
                await rawMessage.Author.SendMessageAsync("Your wallet address is malformed, little turle.");
                return;
            }

            _wallets[message.Author.Id] = wallet;
            await message.Author.SendMessageAsync("Your wallet is ready to catch shells in the upcoming rain!");
        }

        public async Task CheckBalanceLoop(TimeSpan interval, CancellationToken cancellationToken)
        {
            State = RainServiceState.CheckingBalance;

            while (!cancellationToken.IsCancellationRequested)
            {
                long currentBalance = await _walletService.GetBalance(BotWallet);

                if (currentBalance >= BalanceThreshold)
                {
                    var channel = _discord.GetChannel(_channelId) as SocketTextChannel;
                    if (channel == null)
                    {
                        // Should not happen, stop the bot if it does.
                        break;
                    }

                    State = RainServiceState.BalanceExceeded;
                    await AnnounceTeaser(channel, cancellationToken);

                    int registeredWallets = 0;
                    while (registeredWallets == 0)
                    {
                        State = RainServiceState.AcceptingRegistrations;
                        await AnnounceRegistration(channel, cancellationToken);
                        State = RainServiceState.Raining;
                        registeredWallets = await MakeItRain(currentBalance);
                    }

                    await AnnounceRain(channel, cancellationToken, currentBalance, registeredWallets);
                    State = RainServiceState.CheckingBalance;
                }

                try
                {
                    await Task.Delay(interval, cancellationToken);
                }
                catch (TaskCanceledException)
                {
                    break;
                }
            }

            State = RainServiceState.Stopped;
        }

        public async Task AnnounceTeaser(SocketTextChannel channel, CancellationToken cancellationToken)
        {
            var embed = new EmbedBuilder()
                .WithColor(new Color(114, 137, 218))
                .WithTitle("TUT TUT, IT LOOKS LIKE RAIN...")
                .WithImageUrl(_config["rainImageUrlAnnouncement"])
                .Build();

            await channel.SendMessageAsync("", false, embed);
            await Task.Delay(_announceDelay, cancellationToken);
        }

        public async Task AnnounceRegistration(SocketTextChannel channel, CancellationToken cancellationToken)
        {
            var embed = new EmbedBuilder()
                .WithColor(new Color(114, 137, 218))
                .WithTitle("IT BEGINS TO RAIN!")
                .WithDescription($"YOU HAVE ``{_registerDelay.TotalSeconds}`` SECONDS TO SEND ME YOUR WALLET ADDRESS")
                .WithImageUrl(_config["rainImageUrlRegistration"])
                .Build();

            await channel.SendMessageAsync("", false, embed);
            await Task.Delay(_registerDelay, cancellationToken);
        }

        public async Task AnnounceRain(SocketTextChannel channel, CancellationToken cancellationToken, long balance, int wallets)
        {
            var embed = new EmbedBuilder()
                .WithColor(new Color(114, 137, 218))
                .WithTitle($"``{balance / 100.0M}`` TRTL WAS GIVEN TO ``{wallets}`` TURTLES")
                .WithDescription($"Donate TRTL to make it rain again! ```\n{BotWallet.Address}```")
                .WithThumbnailUrl(_config["rainImageUrlTRTL"])
                .Build();

            await channel.SendMessageAsync("", false, embed);
        }

        private async Task<int> MakeItRain(long balance)
        {
            const long fee = 10;
            int walletCount = _wallets.Count;

            if (walletCount == 0) return 0;

            long availableBalance = balance - fee;
            long amountPerWallet = availableBalance / walletCount;
            long actualFee = balance - (amountPerWallet * walletCount);

            await _walletService.SendToMany(amountPerWallet, actualFee, _wallets.Values);
            _wallets.Clear();

            return walletCount;
        }
    }
}