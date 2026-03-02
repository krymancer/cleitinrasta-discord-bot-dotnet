using Microsoft.Extensions.Hosting;
using NetCord;
using NetCord.Gateway;
using NetCord.Hosting.Gateway;
using NetCord.Hosting.Services;
using NetCord.Hosting.Services.ApplicationCommands;
using NetCord.Hosting.Services.Commands;
using NetCord.Services.ApplicationCommands;
using NetCord.Services.Commands;
using Lavalink4NET.NetCord;

var builder = Host.CreateDefaultBuilder(args)
    .UseDiscordGateway()
    .ConfigureServices((context, services) =>
    {
        services.Configure<LavalinkSocketOptions>(options =>
        {
            options.BaseAddress = new Uri(context.Configuration["Lavalink:BaseAddress"] ?? "http://lavalink:2333/");
            options.Passphrase = context.Configuration["Lavalink:Passphrase"] ?? "youshallnotpass";
        });
    })
    .UseLavalink()
    .UseApplicationCommands<SlashCommandInteraction, SlashCommandContext>()
    .UseApplicationCommands<UserCommandInteraction, UserCommandContext>()
    .UseApplicationCommands<MessageCommandInteraction, MessageCommandContext>();

var host = builder.Build()
    .AddModules(typeof(Program).Assembly)
    .UseGatewayEventHandlers();

await host.RunAsync();