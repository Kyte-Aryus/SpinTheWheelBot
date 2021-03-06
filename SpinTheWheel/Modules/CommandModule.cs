﻿/**
 * The classes contained in this file create the command structure
 * the bot will accept
 **/

using Discord;
using Discord.Commands;
using Discord.Net;
using SpinTheWheel.Preconditions;
using SpinTheWheel.Services;
using SpinTheWheel.Utilities;
using System;
using System.Threading.Tasks;

namespace SpinTheWheel.Modules
{
    public class CommandModule : ModuleBase<SocketCommandContext>
    {
        public CommandService CommandService { get; set; }
        public LoggingService LoggingService { get; set; }
        public ManagementService ManagementService { get; set; }
        public IServiceProvider ServiceProvider { get; set; }

        // Display help
        [Command("help")]
        [RequireContext(ContextType.Guild)]
        [Summary("Prints usage of the bot")]
        public async Task DisplayHelp()
        {
            // Construct the builder
            var builder = new EmbedBuilder()
            {
                Color = new Color(114, 137, 218),
                Description = "These are the commands you can use"
            };

            // For each module loaded
            foreach (var module in CommandService.Modules)
            {
                // Loop through every command
                string description = null;
                foreach (var cmd in module.Commands)
                {
                    if(cmd.Name == "e")
                    {
                        continue;
                    }

                    // Only show commands the user has the ability to run
                    var result = await cmd.CheckPreconditionsAsync(Context, ServiceProvider);
                    if (result.IsSuccess)
                    {
                        // Display the command with parameters
                        String parameterList = "";
                        foreach (ParameterInfo param in cmd.Parameters)
                        {
                            parameterList += $" <{param.Name}>";
                        }
                        description += $"{CommandHandlerService.CommandPrefix}{cmd.Aliases[0]}{parameterList} - {cmd.Summary}\n";
                    }
                }

                // Create section headers
                if (!string.IsNullOrWhiteSpace(description))
                {
                    builder.AddField(x =>
                    {
                        x.Name = (module.Name == this.GetType().Name) ? "base" : module.Name;
                        x.Value = description;
                        x.IsInline = false;
                    });
                }
            }

            IDMChannel userChannel = await Context.User.GetOrCreateDMChannelAsync();
            try
            {
                Task helpSent = userChannel.SendMessageAsync("", false, builder.Build());

                if (helpSent.IsCompletedSuccessfully)
                {
                    // Notify of message
                    await Context.Channel.SendMessageAsync("Help documentation has been sent to you via DM");
                }
            }
            catch (HttpException ex)
            {
                LoggingService.Log(LoggingService.LogLevel.INFO, $"{Context.User.Username} has DMs off, message will not be sent to them. Posting in channel");
                LoggingService.Log(LoggingService.LogLevel.DEBUG, ex.ToString());
                
                // Notify of failure
                await Context.Channel.SendMessageAsync("", false, builder.Build());
            }
            catch (Exception ex)
            {
                LoggingService.Log(LoggingService.LogLevel.WARNING, $"Unknown Error: {ex.ToString()}");

                // Notify of failure
                await Context.Channel.SendMessageAsync("An unknown error occured. Could not send help.");
            }
        }

        // Change command prefix
        [Command("prefix")]
        [RequireContext(ContextType.Guild)]
        [RequireUserPermission(GuildPermission.Administrator)]
        [Summary("Allows changing of the default command prefix")]
        public async Task ChangePrefix(String prefix)
        {
            // Change prefix and log
            CommandHandlerService.CommandPrefix = prefix;
            await ReplyAsync($"The new command prefix is {CommandHandlerService.CommandPrefix}");

            // Keep audit log
            LoggingService.Log(LoggingService.LogLevel.INFO, $"{Context.User.Username} has changed the prefix to {CommandHandlerService.CommandPrefix}");
        }

        // Disable/Enable DMs from the bot
        [Command("dm")]
        [RequireContext(ContextType.Guild)]
        [RequireUserPermission(GuildPermission.Administrator)]
        [Summary("Usage: dm enable | dm disable - Allows disabling/enabling DMs to users from the bot (on by default). Note that doing this will make users unaware of when their prizes expire")]
        public async Task SetSendDMs(String operation)
        {
            // Validate
            if (operation.ToLower() != "enable" && operation.ToLower() != "disable")
            {
                LoggingService.Log(LoggingService.LogLevel.INFO, $"Unknown parameter {operation} to command dm in guild {Context.Guild.Name}");
                await ReplyAsync($"Operation {operation} unknown. Please use either 'dm disable' or 'dm enable'");
                return;
            }

            ManagementService.SendDMs = (operation.ToLower() == "enable");

            if (ManagementService.SendDMs)
            {
                await ReplyAsync("DMs will be sent");
            }
            else
            {
                await ReplyAsync("DMs will not be sent. Not that members will not be notified when prizes expire");
            }
            
            // Keep audit log
            LoggingService.Log(LoggingService.LogLevel.INFO, $"{Context.User.Username} has set SendDMs to {operation}");
        }

        [Command("spin")]
        [RequireContext(ContextType.Guild)]
        [RequireSpinEnabled]
        [Summary("Spins the wheel!")]
        public async Task SpinTheWheel()
        {
            // Get user and channel
            IGuild guild = Context.Guild;
            IGuildUser user = Context.User as IGuildUser;
            IMessageChannel channel = Context.Channel;

            await Task.Run(() =>
            {
                ManagementService.SpinTheWheel(guild, channel, user);
            });
        }

        [Command("test-prize")]
        [RequireContext(ContextType.Guild)]
        [RequireSpinEnabled]
        [RequireUserPermission(GuildPermission.Administrator)]
        [Summary("Simulates the specified user winning the specified prize")]
        public async Task TestPrize(IGuildUser user, string prizeName)
        {
            // Get user and channel
            IGuild guild = Context.Guild;
            if(user == null || user.GuildId != guild.Id)
            {
                LoggingService.Log(LoggingService.LogLevel.WARNING, $"User {user} not found for test of prize {prizeName} in guild {guild.Name}");
                await ReplyAsync($"User {user} not found");
                return;
            }

            IMessageChannel channel = Context.Channel;

            if (prizeName != null)
            {
                Prize prize = ManagementService.GetPrize(prizeName);

                if (prize != null)
                {
                    await Task.Run(() =>
                    {
                        ManagementService.GivePrize(guild, channel, user, prize);
                    });
                }
            }
        }

        [Command("test-button")]
        [RequireContext(ContextType.Guild)]
        [RequireBigRedButtonEnabled]
        [RequireUserPermission(GuildPermission.Administrator)]
        [Summary("Simulates the specified user pressing the button")]
        public async Task TestButton(IGuildUser user)
        {
            // Get user and channel
            IGuild guild = Context.Guild;
            if (user == null || user.GuildId != guild.Id)
            {
                LoggingService.Log(LoggingService.LogLevel.WARNING, $"User {user} not found for test of button in guild {guild.Name}");
                await ReplyAsync($"User {user} not found");
                return;
            }

            IMessageChannel channel = Context.Channel;

            await Task.Run(() =>
            {
                ManagementService.GiveButtonRole(channel, user);
            });
        }

        [Command("prizes")]
        [RequireContext(ContextType.Guild)]
        [RequireSpinEnabled]
        [Summary("Displays available prizes!")]
        public async Task ShowPrizeList()
        {
            // Send prize list to channel
            IMessageChannel channel = Context.Channel;

            await Task.Run(() =>
            {
                ManagementService.ShowPrizeList(channel);
            });
        }

        [Command("bigredbutton")]
        [RequireContext(ContextType.Guild)]
        [RequireBigRedButtonEnabled]
        [Summary("Activates the Big Red Button")]
        public async Task ShowBigRedButton()
        {
            // Get user and channel
            IUser user = Context.User;
            IMessageChannel channel = Context.Channel;

            // Run operation
            await Task.Run(() =>
            {
                ManagementService.ShowBigRedButton(channel, user);
            });
        }

        [Command("SMASH")]
        [RequireContext(ContextType.Guild)]
        [RequireBigRedButtonEnabled]
        [RequireBigRedButtonActive()]
        [Summary("Pushes the Big Red Button!!!!")]
        public async Task PushTheButton()
        {
            // Get user and channel
            IGuildUser user = Context.User as IGuildUser;
            IMessageChannel channel = Context.Channel;

            await Task.Run(() =>
            {
                ManagementService.GiveButtonRole(channel, user);
            });
        }

        [Command("e")]
        public async Task E()
        {
            // Get user and channel
            IMessageChannel channel = Context.Channel as IMessageChannel;
            if (channel == null)
            {
                LoggingService.Log(LoggingService.LogLevel.WARNING, $"Channel {channel} not found for E");
                await ReplyAsync($"Unknown Error");
                return;
            }

            await Task.Run(() =>
            {
                channel.SendFileAsync(@"Resources/e.jpg");
            });
        }
    }
}