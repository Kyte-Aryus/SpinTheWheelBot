/**
 * Responsible for capturing and sending commands to the
 * CommandModule for processing. Handles any errors that occur
 * when executing commands
 **/

using Discord;
using Discord.Commands;
using Discord.WebSocket;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Reflection;
using System.Threading.Tasks;

namespace SpinTheWheel.Services
{
    class CommandHandlerService
    {
        private readonly LoggingService _logger;
        private readonly CommandService _commandService;
        private readonly DiscordSocketClient _client;
        private readonly IServiceProvider _services;

        private const String DEFAULT_COMMAND_PREFIX = "&";
        public static String CommandPrefix { get; set; }

        public CommandHandlerService(IServiceProvider provider)
        {
            CommandPrefix = DEFAULT_COMMAND_PREFIX;

            // Initialize required clients and services
            _services = provider;
            _client = provider.GetRequiredService<DiscordSocketClient>();
            _commandService = provider.GetRequiredService<CommandService>();
            _logger = provider.GetRequiredService<LoggingService>();

            _logger.RegisterClient(_commandService);

            _commandService.CommandExecuted += CommandExecuted;
            _client.MessageReceived += MessageReceived;

            _logger.Log(LoggingService.LogLevel.DEBUG, "Command handler service configured");
        }

        // Registers the command module
        public async Task InitializeAsync()
        {
            await _commandService.AddModulesAsync(Assembly.GetEntryAssembly(), _services);
            _logger.Log(LoggingService.LogLevel.DEBUG, "Command processor initialized");
        }

        // Checks for commands and forwards them to the CommandService
        public async Task MessageReceived(SocketMessage message)
        {
            // Only process user messages not bot or system messages
            if(message is SocketUserMessage && message.Source == MessageSource.User)
            {
                _logger.Log(LoggingService.LogLevel.DEBUG, $"Received user message from {message.Author}");

                SocketUserMessage userMessage = message as SocketUserMessage;

                // Check if it's a command
                int argsStart = 0;
                if(userMessage.HasStringPrefix(CommandPrefix, ref argsStart))
                {
                    _logger.Log(LoggingService.LogLevel.DEBUG, $"Command {userMessage.Content} detected from {userMessage.Author} in {userMessage.Channel}");

                    // Command is for us, execute the command
                    SocketCommandContext context = new SocketCommandContext(_client, userMessage);

                    // Catch the exception to display message on failure
                    try
                    { 
                        await _commandService.ExecuteAsync(context, argsStart, _services);
                    }
                    catch (InvalidOperationException)
                    {
                        await context.Channel.SendMessageAsync($"Unknown command {userMessage.Content}");

                        // Invalid commands could cause log spam for INFO level. Also the bot already responds in the channel
                        // which provides a level of debugging at the INFO level. This should remain DEBUG
                        _logger.Log(LoggingService.LogLevel.DEBUG, $"{context.User} attempted invalid command {userMessage.Content} in {context.Channel}");
                        return;
                    }
                }
            }
        }

        // Called after the command executes, keeps and audit log of successful commands
        // and logs errors for non succesful commands
        public async Task CommandExecuted(Optional<CommandInfo> command, ICommandContext context, IResult result)
        {
            // Check if unknown command but exception wasn't thrown
            if (command.Value == null || !command.IsSpecified)
            {
                await context.Channel.SendMessageAsync($"Unknown command {command.Value}");

                // Invalid commands could cause log spam for INFO level. Also the bot already responds in the channel
                // which provides a level of debugging at the INFO level. This should remain DEBUG
                _logger.Log(LoggingService.LogLevel.DEBUG, $"{context.User} attempted invalid command {command.Value.Name} in {context.Channel}");
                return;
            }

            // Keep audit logs of user commands
            if (result.IsSuccess)
            {
                _logger.Log(LoggingService.LogLevel.INFO, $"{context.User} performed command {command.Value.Name} in {context.Channel}");
            }
            else
            {
                // Log failure
                _logger.Log(LoggingService.LogLevel.WARNING, $"Failed to exeucte command {command.Value.Name} from {context.User} in {context.Channel}");
                _logger.Log(LoggingService.LogLevel.WARNING, $"Error: {result.Error} - {result.ErrorReason}");
                
                /* Now find specific error reason and log to channel */

                // Some reference value wasn't found
                if (result.Error == CommandError.ObjectNotFound)
                {
                    await context.Channel.SendMessageAsync("Command failed. User/Channel not found.");
                }

                // Some permissions were wrong
                else if(result.Error == CommandError.UnmetPrecondition && result.ErrorReason.Contains("permission"))
                {
                    await context.Channel.SendMessageAsync("You do not have permission to use this command!");
                }

                // User is not in a guild
                else if (result.Error == CommandError.UnmetPrecondition && result.ErrorReason.Contains("must be in a guild"))
                {
                    await context.Channel.SendMessageAsync("You must be in a guild to use this command!");
                }

                // Wrong number of arguments
                else if (result.Error == CommandError.BadArgCount)
                {
                    await context.Channel.SendMessageAsync("Invalid syntax for command!");
                }

                // Cannot send message to the user, they have DMs turned off
                else if (result.Error == CommandError.Exception && result.ErrorReason.Contains("Cannot send messages to this user"))
                {
                    await context.Channel.SendMessageAsync("You must enable DMs in your profile to run this command!");
                }

                // Big red button is not active
                else if (result.Error == CommandError.UnmetPrecondition && result.ErrorReason.Contains("Big Red Button is not active"))
                {
                    await context.Channel.SendMessageAsync($"The Big Red Button is not active! Use {CommandPrefix}bigredbutton to activate");
                }

                // Big red button is not active
                else if (result.Error == CommandError.UnmetPrecondition && result.ErrorReason.Contains("Big Red Button is not enabled"))
                {
                    await context.Channel.SendMessageAsync($"The Big Red Button is not enabled!");
                }

                // Spin function is not enabled
                else if (result.Error == CommandError.UnmetPrecondition && result.ErrorReason.Contains("Spin function is not enabled is not enabled"))
                {
                    await context.Channel.SendMessageAsync($"The Spin function is not enabled!");
                }
            }
        }
    }
}
