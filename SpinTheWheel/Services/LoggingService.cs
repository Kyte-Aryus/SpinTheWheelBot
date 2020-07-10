/**
 * Facilitates console logging for the bot. DEBUG level info
 * will only be logged if the config file specifies
 * Debug=True
 **/

using Discord;
using Discord.Commands;
using Discord.WebSocket;
using SpinTheWheel.Utilities;
using System;
using System.IO;
using System.Threading.Tasks;

namespace SpinTheWheel
{
    public class LoggingService
    {
        public enum LogLevel
        {
            DEBUG,
            INFO,
            WARNING,
            ERROR
        };

        const String TIME_FORMAT = "MMM dd HH:mm:ss";

        private readonly Boolean _isDebug = false;
        private readonly Boolean _logToFile = false;
        private readonly Boolean _logToConsole = false;
        private readonly StreamWriter _fileWriter = null;


        public LoggingService(CommandLineOptions options)
        {
            String errorMsg = null;

            _isDebug = options.Debug;
            _logToConsole = !options.SilentMode;

            // Check console logging
            String logfile = options.LogFile;
            if(logfile != null && logfile != "")
            {
                try
                {
                    // If there's a path verify it exists, if there isn't a path
                    // then make file in current directory
                    String logpath = Path.GetDirectoryName(logfile);
                    if (Directory.Exists(logpath) || logpath == "")
                    {
                        _fileWriter = new StreamWriter(logfile, true);
                        _logToFile = true;
                    }
                    else
                    {
                        errorMsg = $"The requested logfile path {logpath} does not exist";
                    }
                }
                catch (Exception)
                {
                    errorMsg = $"Request logfile {logfile} has a malformed path";
                }
            }

            // If there was an error, log now that logging is turned on
            if (errorMsg != null)
            {
                Log(LogLevel.WARNING, errorMsg);
            }
        }

        // Register discord client to print log messages to console
        public void RegisterClient(DiscordSocketClient client)
        {
            client.Log += LogClientMessage;
        }

        // Register the command service to print log messages to console
        public void RegisterClient(CommandService service)
        {
            service.Log += LogClientMessage;
        }

        // For a client, prefix with Command or System depending on where it came from
        private Task LogClientMessage(LogMessage message)
        {
            String logMsg = "";

            if (message.Exception is CommandException cmdException)
            {
                // Log message + exception itself
                logMsg = $"{DateTime.Now.ToString(TIME_FORMAT)} [Command/{message.Severity}] {cmdException.Command.Name}"
                    + $" failed to execute in {cmdException.Context.Channel}\n";
                logMsg += cmdException;
            }
            else
            { 
                logMsg = $"{DateTime.Now.ToString(TIME_FORMAT)} [System/{message.Severity}] {message}";
            }

            // Log to enabled methods
            LogMessage(logMsg);

            return Task.CompletedTask;
        }

        // Logs messages specificly written into the bot
        public void Log(LogLevel level, String message)
        {
            // Don't log debug statements
            if (level == LogLevel.DEBUG && !_isDebug)
                return;

            // For nicer print, only capitalize the first letter
            char firstLetter = level.ToString()[0]; // Already capital because of the enum
            String pascalCase = firstLetter + level.ToString().Substring(1).ToLower();

            // Log to enabled methods
            LogMessage($"{DateTime.Now.ToString(TIME_FORMAT)} [General/{pascalCase}] {message}");
        }

        // Logs to enabled methods
        private void LogMessage(String message)
        {
            if (_logToConsole)
            {
                Console.WriteLine(message);
            }

            if (_logToFile)
            {
                _fileWriter.WriteLine(message);
                _fileWriter.Flush();
            }
        }
    }
}
