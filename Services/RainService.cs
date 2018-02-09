using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Discord.WebSocket;
using Discord;
using System.Collections.Concurrent;
using Microsoft.Extensions.Configuration;
using System.Linq;
using System.Collections.Generic;
using Discord.Rest;
using TurtleBot.Utilities;
using System.Collections;

namespace TurtleBot.Services
{
    public class RainService
    {
        private readonly ILogger _logger;
        private readonly DiscordSocketClient _discord;
        private readonly WalletService _walletService;
        private readonly IConfiguration _config;

        private Random _random;
        private TimeSpan _checkInterval;
        private TimeSpan _announceDelay;
        private TimeSpan _registerDelay;
        private Task _checkTask;
        private CancellationTokenSource _checkCanellationTokenSource;
        private ConcurrentDictionary<SocketUser, TurtleWallet> _wallets;
        private ConcurrentDictionary<ulong, Emote> _requiredReactions;

        private ulong _channelId;
        private ulong _exiledRoleId;
        private ulong _guildId;
        private IGuild _guild;
        private IReadOnlyCollection<IGuildUser> _guildUsers;

        public RainServiceState State { get; private set; }
        public long BalanceThreshold { get; private set; }
        public TurtleWallet BotWallet { get; private set; }
        public List<Emote> ReactionEmotes { get; private set; }

        public RainService(ILoggerFactory loggerFactory, DiscordSocketClient discord, WalletService walletService, IConfiguration config)
        {
            _logger = loggerFactory.CreateLogger("rain");
            _discord = discord;
            _walletService = walletService;
            _config = config;

            _random = new Random();
            _checkInterval = new TimeSpan(0, 0, Convert.ToInt32(config["rainCheckIntervalS"]));
            _announceDelay = new TimeSpan(0, 0, Convert.ToInt32(config["rainAnnounceDelayS"]));
            _registerDelay = new TimeSpan(0, 0, Convert.ToInt32(config["rainRegisterDelayS"]));
            _checkCanellationTokenSource = new CancellationTokenSource();
            _wallets = new ConcurrentDictionary<SocketUser, TurtleWallet>();
            _requiredReactions = new ConcurrentDictionary<ulong, Emote>();

            _guildId = 388915017187328002;
            _channelId = Convert.ToUInt64(config["rainChannelId"]);

            // The rain service has to be started explicitly (with ``!rain start``)
            State = RainServiceState.Stopped;
            BalanceThreshold = Convert.ToInt64(config["rainBalanceThreshold"]);
            ReactionEmotes = new List<Emote>();

            _discord.MessageReceived += MessageReceived;
        }

        public async void Start()
        {
            try
            {
                if (State == RainServiceState.Stopped)
                {
                    _guild = _discord.GetGuild(_guildId);
                    _exiledRoleId = _guild.Roles.Where(r => r.Name == "exiled").First().Id;
                    _guildUsers = await _guild.GetUsersAsync();
                    BotWallet = await _walletService.GetFirstAddress();
                    _checkCanellationTokenSource = new CancellationTokenSource();
                    _checkTask = Task.Run(() => CheckBalanceLoop(_checkInterval, _checkCanellationTokenSource.Token));
                }
            }
            catch (Exception e)
            {
                State = RainServiceState.Stopped;
                _logger.LogCritical(e, "Exception in Start");
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
            // Ignore system messages and messages from bots and exiled users
            if (!(rawMessage is SocketUserMessage message)) return;
            if (message.Source != MessageSource.User) return;
            if (!(message.Channel is SocketDMChannel)) return;
            var guildUser = _guildUsers.Where(u => u.Id == message.Author.Id).FirstOrDefault();
            if (guildUser == null || guildUser.RoleIds.Contains(_exiledRoleId)) return;

            string address = message.ToString();
            _logger.LogInformation(message.Author.Username + ": " + address);

            if (State != RainServiceState.AcceptingRegistrations)
            {
                if (State == RainServiceState.BalanceExceeded)
                {
                    await rawMessage.Author.SendMessageAsync("You are too early, little turtle, please wait for the registration!");
                    return;
                }
                else
                {
                    await rawMessage.Author.SendMessageAsync("Huh, it doesn't look like it is raining soon...");
                    return;
                }
            }

            if (_wallets.ContainsKey(message.Author))
            {
                await rawMessage.Author.SendMessageAsync("You are already registered, little turtle.");
                return;
            }

            TurtleWallet wallet = await TurtleWallet.FromString(_walletService, address);

            if (wallet == null)
            {
                await rawMessage.Author.SendMessageAsync("Your wallet address is malformed, little turtle.");
                return;
            }

            _wallets[message.Author] = wallet;
            _requiredReactions[message.Author.Id] = ReactionEmotes.RandomElement(_random);

            await message.Author.SendMessageAsync($"React to the announcement message with {_requiredReactions[message.Author.Id]} (and **ONLY** with {_requiredReactions[message.Author.Id]}) to catch some shells!");
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

                    _logger.LogInformation("=== BALANCE EXCEEDED ===");
                    State = RainServiceState.BalanceExceeded;

                    var message = await AnnounceTeaser(channel);
                    await Task.Delay(_announceDelay, cancellationToken);

                    int registeredWallets = 0;
                    string txId = "";
                    while (registeredWallets == 0)
                    {
                        ReactionEmotes.Clear();
                        ReactionEmotes.AddRange(channel.Guild.Emotes
                            .Where(e => !e.Name.StartsWith("t_"))
                            .OrderBy(x => _random.Next())
                            .Take(10));
                        await AnnounceRegistration(channel, message);
                        await Task.Delay(_registerDelay, cancellationToken);

                        _logger.LogInformation("=== RAINING ===");
                        State = RainServiceState.Raining;

                        await FilterWalletsByReactions(message);
                        _logger.LogInformation("=== END FILTERING ===");
                        txId = await MakeItRain(currentBalance);
                        _logger.LogInformation("=== SENT TRANSACTION ===");

                        registeredWallets = _wallets.Count;
                        _wallets.Clear();
                        _requiredReactions.Clear();
                    }

                    await AnnounceRain(channel, message, cancellationToken, currentBalance, registeredWallets, txId);
                    _logger.LogInformation("=== CHECKING BALANCE ===");
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

        public async Task<RestUserMessage> AnnounceTeaser(SocketTextChannel channel)
        {
            var embed = new EmbedBuilder()
                .WithColor(new Color(114, 137, 218))
                .WithTitle("TUT TUT, IT LOOKS LIKE RAIN...")
                .WithImageUrl(_config["rainImageUrlAnnouncement"])
                .Build();

            return await channel.SendMessageAsync("", false, embed);
        }

        public async Task AnnounceRegistration(SocketTextChannel channel, RestUserMessage message)
        {
            var embed1 = new EmbedBuilder()
                .WithColor(new Color(114, 137, 218))
                .WithTitle("IT BEGINS TO RAIN!")
                .WithImageUrl(_config["rainImageUrlAnnouncement"])
                .Build();
            var embed2 = new EmbedBuilder()
                .WithColor(new Color(114, 137, 218))
                .WithTitle("QUICK, SEND ME YOUR WALLET ADDRESSES!")
                .WithDescription($"YOU HAVE ``{_registerDelay.TotalSeconds}`` SECONDS TO SEND ME YOUR WALLET ADDRESS.\nDON'T FORGET TO ADD THE REACTION I WILL SEND YOU BACK.")
                .WithImageUrl(_config["rainImageUrlRegistration"])
                .Build();

            await message.ModifyAsync(m => m.Embed = embed1);

            foreach (var emote in ReactionEmotes)
            {
                await message.AddReactionAsync(emote);
                await Task.Delay(2500);
            }

            _logger.LogInformation("=== ACCEPT REGISTRATION ===");
            _guildUsers = await _guild.GetUsersAsync();
            State = RainServiceState.AcceptingRegistrations;

            await message.ModifyAsync(m => m.Embed = embed2);
        }

        public async Task AnnounceRain(SocketTextChannel channel, RestUserMessage message, CancellationToken cancellationToken, long balance, int wallets, string txId)
        {
            long currentBalance = await _walletService.GetBalance(BotWallet);
            decimal missing = (BalanceThreshold - currentBalance) / 100.0M;
            string desc = missing > 0
                ? $"Donate {missing} TRTL to make it rain again! ```\n{BotWallet.Address}```"
                : "Wait, it is still raining?!";

            var embed = new EmbedBuilder()
                .WithColor(new Color(114, 137, 218))
                .WithTitle($"{balance / 100.0M} TRTL WAS GIVEN TO {wallets} TURTLES")
                .WithUrl($"http://turtle-coin.com/?hash={txId}#blockchain_transaction")
                .WithDescription(desc)
                .WithThumbnailUrl(_config["rainImageUrlTRTL"])
                .Build();

            await message.ModifyAsync(m => m.Embed = embed);
        }

        private async Task FilterWalletsByReactions(RestUserMessage message)
        {
            Dictionary<ulong, List<IEmote>> reactionsPerUser = new Dictionary<ulong, List<IEmote>>();
            List<ulong> invalidUsers = new List<ulong>();

            foreach (var emote in message.Reactions.Keys)
            {
                Emote guildEmote = emote as Emote;
                try
                {
                    var usersReacted = await message.GetReactionUsersAsync(emote.Name + ":" + guildEmote.Id);

                    foreach (IUser user in usersReacted)
                    {
                        if (!reactionsPerUser.ContainsKey(user.Id))
                        {
                            reactionsPerUser[user.Id] = new List<IEmote>();
                        }

                        reactionsPerUser[user.Id].Add(emote);
                    }

                }
                catch (Exception e)
                {
                    _logger.LogCritical(e.Message, e);
                }
            }

            foreach (var walletPair in _wallets)
            {
                var user = walletPair.Key;
                if (!reactionsPerUser.ContainsKey(user.Id))
                {
                    await user.SendMessageAsync($"Oh my, you did not react to the rain announcement post with {_requiredReactions[user.Id]}! No shells for you...");
                    invalidUsers.Add(user.Id);
                    _logger.LogWarning($"Filtered {user.Username} - {walletPair.Value.Address}: NO REACTION");
                }
                else if (!reactionsPerUser[user.Id].Any(e => e.Name == _requiredReactions[user.Id].Name))
                {
                    await user.SendMessageAsync($"Oh my, you did not react to the rain announcement post with {_requiredReactions[user.Id]}! No shells for you...");
                    invalidUsers.Add(user.Id);
                    _logger.LogWarning($"Filtered {user.Username} - {walletPair.Value.Address}: WRONG REACTION");
                }
                else if (reactionsPerUser[user.Id].Count > 1)
                {
                    await user.SendMessageAsync($"Oh my, you reacted to the rain announcement post with **more** than {_requiredReactions[user.Id]}! No shells for you...");
                    invalidUsers.Add(user.Id);
                    _logger.LogWarning($"Filtered {user.Username} - {walletPair.Value.Address}: TOO MANY REACTIONS");
                }
                else
                {
                    _logger.LogInformation($"Wallet OK: {user.Username} - {walletPair.Value.Address}");
                }
            }

            foreach (var invalidUser in invalidUsers)
            {
                TurtleWallet unused;
                _wallets.TryRemove(_discord.GetUser(invalidUser), out unused);
            }
        }

        private async Task<string> MakeItRain(long balance)
        {
            const long fee = 10;
            int walletCount = _wallets.Count;

            if (walletCount == 0) return string.Empty;

            long availableBalance = balance - fee;
            long amountPerWallet = availableBalance / walletCount;
            long trtlPerWallet = amountPerWallet / 100.0M;
            long actualFee = balance - (amountPerWallet * walletCount);

            foreach (var walletPair in _wallets)
            {
                var user = walletPair.Key;
                await user.SendMessageAsync($"The rain fell on you little turtle! " + trtlPerWallet + " TRTL is on it's way to your wallet!");
            }

            return await _walletService.SendToMany(amountPerWallet, actualFee, _wallets.Values);
        }
    }
}
