TurtleBotRain
=============

This is the source for TurtleBotRain, used on the [TurtleCoin](https://discord.gg/UKsNX6F) discord server.

## Setting Up

This bot uses .NET Core 2.0. To install, use the following link: https://www.microsoft.com/net/learn/get-started/ and select the instructions relevant to your OS.

For an IDE I recommend Visual Studio Code.

## Usage

Get a bot token by making an app (instructions [here](https://discord.foxbot.me/docs/guides/getting_started/intro.html))

Create a file called `config.json` with the following contents:
```json
{
	"token": "<your token here>",
	"walletapiServiceAddress": "localhost",
	"walletapiServicePort": 8070,
	"walletapiRPCPassword": "<RPC password here>",
	"rainChannelId": 012345678987654321,
	"rainCheckIntervalS": 10,
	"rainAnnounceDelayS": 10,
	"rainRegisterDelayS": 90,
	"rainBalanceThreshold": 100000,
	"rainWikiUrl": "<howto raindance wiki url>",
	"rainImageUrlAnnouncement": "<image url>",
	"rainImageUrlRegistration": "<image url>",
	"rainImageUrlTRTL": "<image url>",
	"networkFee": 6500,
	"nodeFee": 1000
}
```

You also need ``wallet-api`` and ``turtlecoind`` running on the localhost (or on another machine).
Set ``walletdServiceAddress``, ``walletdServicePort`` and ``walletdRPCPassword`` accordingly.

**DO NOT SHARE YOUR TOKEN WITH ANYBODY**

Further instructions vary by your IDE. Project should open up straight away and bre ready to go in VS Code.
