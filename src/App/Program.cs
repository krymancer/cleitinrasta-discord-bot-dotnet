using Lavalink4NET.InactivityTracking.Extensions;
using Lavalink4NET.InactivityTracking.Trackers.Idle;
using Lavalink4NET.InactivityTracking.Trackers.Users;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Lavalink4NET.Extensions;
using Lavalink4NET.NetCord;
using NetCord;
using NetCord.Gateway;
using NetCord.Hosting.Gateway;
using NetCord.Hosting.Services;
using NetCord.Hosting.Services.ApplicationCommands;
using NetCord.Hosting.Services.Commands;
using NetCord.Services.ApplicationCommands;
using NetCord.Services.Commands;

// Build a temporary configuration to read Lavalink settings
var tempConfig = new ConfigurationBuilder()
    .SetBasePath(Directory.GetCurrentDirectory())
    .AddJsonFile("appsettings.json", optional: false)
    .AddEnvironmentVariables()
    .Build();

var lavalinkBaseAddress = tempConfig["Lavalink:BaseAddress"] ?? "http://localhost:2333";
var lavalinkPassphrase = tempConfig["Lavalink:Passphrase"] ?? "youshallnotpass";
var inactivityTimeoutMinutes = tempConfig.GetValue<int>("Player:InactivityTimeoutMinutes", 5);

var builder = Host.CreateDefaultBuilder(args)
    .ConfigureServices((context, services) =>
    {
        services.AddDiscordGateway(options =>
        {
            options.Intents = GatewayIntents.Guilds 
                | GatewayIntents.GuildVoiceStates 
                | GatewayIntents.GuildMessages 
                | GatewayIntents.MessageContent;
        });

        // Add Lavalink
        services.AddLavalink();
        
        // Add inactivity tracking
        services.AddInactivityTracking();
        
        // Configure inactivity tracking options
        services.ConfigureInactivityTracking(options =>
        {
            options.DefaultTimeout = TimeSpan.FromMinutes(inactivityTimeoutMinutes);
            options.UseDefaultTrackers = true;
        });
        
        // Configure idle tracker options
        services.Configure<IdleInactivityTrackerOptions>(options =>
        {
            options.Timeout = TimeSpan.FromMinutes(inactivityTimeoutMinutes);
        });
        
        // Configure users tracker options
        services.Configure<UsersInactivityTrackerOptions>(options =>
        {
            options.Timeout = TimeSpan.FromMinutes(inactivityTimeoutMinutes);
            options.ExcludeBots = true;
            options.Threshold = 1;
        });
    })
    .UseLavalink(options =>
    {
        options.BaseAddress = new Uri(lavalinkBaseAddress);
        options.Passphrase = lavalinkPassphrase;
    })
    .UseApplicationCommands<SlashCommandInteraction, SlashCommandContext>()
    .UseApplicationCommands<UserCommandInteraction, UserCommandContext>()
    .UseApplicationCommands<MessageCommandInteraction, MessageCommandContext>();

var host = builder.Build()
    .AddModules(typeof(Program).Assembly);

await host.RunAsync();