/**
 * This is the main class of the bot. It facilitates the connection to Discord through the token,
 * defining the required services through the IServiceProvider, and forwarding chat messages to the Chat Service
 **/

using Discord;
using Discord.Commands;
using Discord.WebSocket;
using Microsoft.Extensions.DependencyInjection;
using SpinTheWheel.Services;
using SpinTheWheel.Utilities;
using System;
using System.Threading.Tasks;

namespace SpinTheWheel
{
    class SpinTheWheelBot
    {
        private readonly LoggingService _logger;
        private readonly DiscordSocketClient _client;
        private readonly CommandHandlerService _commandHandlerService;
        private readonly ManagementService _managementService;
        private readonly IServiceProvider _services;

        public SpinTheWheelBot(CommandLineOptions options)
        {
            _services = CreateServiceProvider(options);
            _logger = _services.GetRequiredService<LoggingService>();
            _managementService = _services.GetRequiredService<ManagementService>();

            // Log the starting message
            _logger.Log(LoggingService.LogLevel.INFO, "Starting SpinTheWheelBot...");

            // Load services
            _client = _services.GetRequiredService<DiscordSocketClient>();
            _commandHandlerService = _services.GetRequiredService<CommandHandlerService>();
            
            // Load cfg
            _managementService.LoadConfigFile(options.CfgFile);

            _logger.RegisterClient(_client);
            _client.Ready += Ready;
        }

        // Just meant to log a message when the Discord client is ready
        private Task Ready()
        {
            _logger.Log(LoggingService.LogLevel.INFO, $"{_client.CurrentUser} connected");
            _logger.Log(LoggingService.LogLevel.INFO, $"Initialization complete! Accepting commands...");

            // Avoid compiler warnings
            return Task.CompletedTask;
        }

        // Logs in and initializes the bot, then waits for exit
        public async Task Start()
        {
            // Connect to the client
            await _client.LoginAsync(TokenType.Bot, _managementService.GetBotToken());
            await _client.StartAsync();

            // Initialize the command service
            await _commandHandlerService.InitializeAsync();

            // Block the program until it is closed.
            await Task.Delay(-1);
        }

        // Defines the required services for dependency injection
        private ServiceProvider CreateServiceProvider(CommandLineOptions options)
        {
            return new ServiceCollection()
                .AddSingleton<DiscordSocketClient>()
                .AddSingleton<CommandService>()
                .AddSingleton<CommandHandlerService>()
                .AddSingleton<LoggingService>(x => new LoggingService(options))
                .AddSingleton<ManagementService>()
                .BuildServiceProvider();
        }
    }
}
