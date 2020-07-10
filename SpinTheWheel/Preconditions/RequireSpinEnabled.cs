/**
 * Provides an Attribute-syntax Precondition for verifying
 * a user has administrative rights to the bot
 * 
 * Logging is handled in CommandHandlerService.cs. If the precondition fails
 * the text in PreconditionResult.FromError() will be based to the 
 * CommandHandlerService and logged appropriately
 */

using Discord.Commands;
using Discord.WebSocket;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Threading.Tasks;

namespace SpinTheWheel.Preconditions
{
    public class RequireSpinEnabled : PreconditionAttribute
    {
        // Override the CheckPermissions method
        public override Task<PreconditionResult> CheckPermissionsAsync(ICommandContext context, CommandInfo command, IServiceProvider services)
        {
            // Get management service
            ManagementService managementService = services.GetRequiredService<ManagementService>();

            // Check if this user is a Guild User, they can't use the bot if not in the guild
            if (context.User is SocketGuildUser guildUser)
            {
                // If this command was exectuable
                if (managementService.IsSpinEnabled())
                {
                    // Since no async work is done, the result has to be wrapped with `Task.FromResult` to avoid compiler errors
                    return Task.FromResult(PreconditionResult.FromSuccess());
                }

                // Was not executed by a bot admin
                else
                {
                    return Task.FromResult(PreconditionResult.FromError("Spin function is not enabled is not enabled!"));
                }
            }
            else
            {
                return Task.FromResult(PreconditionResult.FromError("You must be in a guild to run this command!"));
            }
        }
    }
}
