using App.Configuration;
using Lavalink4NET.InactivityTracking.Extensions;
using Lavalink4NET.InactivityTracking.Trackers.Idle;
using Lavalink4NET.InactivityTracking.Trackers.Users;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
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

var builder = Host.CreateDefaultBuilder(args)
    .ConfigureServices((context, services) =>
    {
        // Bind configuration sections to strongly-typed options
        services.Configure<LavalinkOptions>(context.Configuration.GetSection(LavalinkOptions.SectionName));
        services.Configure<PlayerOptions>(context.Configuration.GetSection(PlayerOptions.SectionName));

        services.AddDiscordGateway(options =>
        {
            options.Intents = GatewayIntents.Guilds
                | GatewayIntents.GuildVoiceStates
                | GatewayIntents.GuildMessages
                | GatewayIntents.MessageContent;
        });

        // Add Lavalink
        services.AddLavalink();
        
        // Configure Lavalink connection settings
        services.ConfigureLavalink(config =>
        {
            var lavalinkOptions = context.Configuration.GetSection(LavalinkOptions.SectionName).Get<LavalinkOptions>()
                ?? throw new InvalidOperationException($"{LavalinkOptions.SectionName} configuration section is missing or invalid");
            
            config.BaseAddress = new Uri(lavalinkOptions.BaseAddress);
            config.Passphrase = lavalinkOptions.Passphrase;
        });
        
        // Add inactivity tracking
        services.AddInactivityTracking();
        
        // Get player options for configuring inactivity tracking
        var playerOptions = context.Configuration.GetSection(PlayerOptions.SectionName).Get<PlayerOptions>() 
            ?? new PlayerOptions();

        // Configure inactivity tracking options
        services.ConfigureInactivityTracking(options =>
        {
            options.DefaultTimeout = TimeSpan.FromMinutes(playerOptions.InactivityTimeoutMinutes);
            options.UseDefaultTrackers = true;
        });

        // Configure idle tracker options
        services.Configure<IdleInactivityTrackerOptions>(options =>
        {
            options.Timeout = TimeSpan.FromMinutes(playerOptions.InactivityTimeoutMinutes);
        });

        // Configure users tracker options
        services.Configure<UsersInactivityTrackerOptions>(options =>
        {
            options.Timeout = TimeSpan.FromMinutes(playerOptions.InactivityTimeoutMinutes);
            options.ExcludeBots = true;
            options.Threshold = 1;
        });
    })
    .UseApplicationCommands<SlashCommandInteraction, SlashCommandContext>()
    .UseApplicationCommands<UserCommandInteraction, UserCommandContext>()
    .UseApplicationCommands<MessageCommandInteraction, MessageCommandContext>();

var host = builder.Build()
    .AddModules(typeof(Program).Assembly);

await host.RunAsync();