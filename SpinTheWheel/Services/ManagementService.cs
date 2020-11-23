/**
 * Manages player registration, channel registration,
 * the requested command prefix, and players waiting to end the day.
 * 
 * Not that since the CommandModule handles its own logging,
 * only functions called outside of CommandModule have log statements
 * present in this class.
 **/

using Discord;
using Discord.WebSocket;
using Microsoft.Extensions.DependencyInjection;
using SpinTheWheel.Services;
using SpinTheWheel.Utilities;
using System;
using System.Collections.Generic;
using System.IO;
using System.Timers;
using System.Threading.Tasks;
using YamlDotNet.RepresentationModel;
using System.ComponentModel;
using Discord.Net;

namespace SpinTheWheel
{
    public class ManagementService
    {
        // Token for the bot 
        private String BOT_TOKEN = null;

        // Checks to enable features
        private Boolean _enableButton = false;
        private Boolean _enableSpin = false;
        private Boolean _enableConsolation = false;
        private Boolean _enableConsecutiveSpinPenalty = true; // Defaulting to true makes it easier to check later
        private Boolean _bigRedButtonCurrentlyActive = false;

        // Prize managment
        private List<Prize> _possiblePrizes = new List<Prize>();
        private Dictionary<string, List<ulong>> _membersWithPrizeDict = new Dictionary<string, List<ulong>>();
        private List<ulong> _membersWithButtonRole = new List<ulong>();

        // Multi-spin penalty dictionaries
        private Dictionary<ulong, Timer> _userTimers = new Dictionary<ulong, Timer>();
        private Dictionary<ulong, int> _numTimesSpunWithoutBreak = new Dictionary<ulong, int>();

        // Consolation prize
        private Prize _consolationPrize = new Prize();

        // All properties needed for button and penalty functions
        // Ids
        private ulong BUTTON_ROLE_ID;
        private string BUTTON_MESSAGE = null;
        private int BUTTON_ENABLE_TIME;
        private int BUTTON_ROLE_TIME;
        private bool BUTTON_ROLE_SILENCES = false;
        private bool BUTTON_MOVE_BACK_AFTER_SILENCE = false;

        // Penalty configs
        private uint SPIN_PENALTY_RESET_TIME;
        private uint SPINS_BEFORE_PENALTY;

        // Random number generator
        private readonly Random _random;

        // Whether or not to send DMs
        public static Boolean SendDMs { get; set; } = true;

        // Needed services
        private readonly DiscordSocketClient _client;
        private readonly LoggingService _logger;

        public ManagementService(IServiceProvider provider)
        {
            // Initialize required clients and services
            _logger = provider.GetRequiredService<LoggingService>();
            _client = provider.GetRequiredService<DiscordSocketClient>();

            _random = new Random();

            _logger.Log(LoggingService.LogLevel.DEBUG, "Management service configured");
        }

        #region Config Loading

        /**************************************************
         * Config loading
         **************************************************/
        internal void LoadConfigFile(string cfgFile)
        {
            // Try loading config
            _logger.Log(LoggingService.LogLevel.INFO, "Reading config!");

            String filepath = Path.Combine(Environment.CurrentDirectory, cfgFile);
            _logger.Log(LoggingService.LogLevel.DEBUG, $"Looking for {filepath}...");

            if (!File.Exists(filepath))
            {
                _logger.Log(LoggingService.LogLevel.ERROR, $"Config file ./{cfgFile} not found");
                Environment.Exit(ExitCode.CONFIG_FILE_NOT_FOUND);
            }

            _logger.Log(LoggingService.LogLevel.DEBUG, "Found! Starting parsing...");

            // File exists, load as yaml
            using (var reader = new StreamReader(filepath))
            {
                try
                {
                    YamlStream yaml = new YamlStream();
                    yaml.Load(reader);

                    YamlMappingNode rootNode = yaml.Documents[0].RootNode as YamlMappingNode;

                    // Find bot token
                    String token = ParseYamlScalarNodeValue("bot-token", rootNode);
                    if (token == null || token == "")
                    {
                        _logger.Log(LoggingService.LogLevel.ERROR, $"Bot Token not found in config! Exiting!");
                        Environment.Exit(ExitCode.BOT_TOKEN_NOT_PROVIDED);
                    }
                    else
                    {
                        _logger.Log(LoggingService.LogLevel.DEBUG, $"Setting bot token.");
                        BOT_TOKEN = token;
                    }

                    // Find command prefix
                    String prefix = ParseYamlScalarNodeValue("command-prefix", rootNode);
                    if(prefix == null)
                    {
                        _logger.Log(LoggingService.LogLevel.WARNING, $"Command prefix not found in config! Defaulting to {CommandHandlerService.CommandPrefix}");
                    }
                    else if (prefix == "")
                    {
                        _logger.Log(LoggingService.LogLevel.WARNING, $"Command prefix entry has no value! Defaulting to {CommandHandlerService.CommandPrefix}");
                    }
                    else
                    {
                        _logger.Log(LoggingService.LogLevel.DEBUG, $"Setting prefix to {prefix}");
                        CommandHandlerService.CommandPrefix = prefix;
                    }
                    
                    // Check for button settings
                    if(rootNode.Children.ContainsKey(new YamlScalarNode("big-red-button")))
                    {
                        if (rootNode.Children[new YamlScalarNode("big-red-button")] is YamlMappingNode buttonRootNode)
                        {
                            _logger.Log(LoggingService.LogLevel.DEBUG, "Found configuration for the big red button. Attempting to parse.");
                            _enableButton = ParseButtonConfig(buttonRootNode);
                        }
                        else
                        {
                            _logger.Log(LoggingService.LogLevel.WARNING, "Configuration for the big red button is malformed. It will not be enabled.");
                            _enableButton = false;
                        }

                    }
                    else
                    {
                        _logger.Log(LoggingService.LogLevel.WARNING, "No configuration for the button. It will not be enabled");
                        _enableButton = false;
                    }

                    // Check for spin settings
                    if (rootNode.Children.ContainsKey(new YamlScalarNode("spin")))
                    {
                        if (rootNode.Children[new YamlScalarNode("spin")] is YamlMappingNode spinRootNode)
                        {
                            _logger.Log(LoggingService.LogLevel.DEBUG, "Found configuration for the spin function. Attempting to parse.");
                            _enableSpin = ParseSpinConfig(spinRootNode);
                        }
                        else
                        {
                            _logger.Log(LoggingService.LogLevel.WARNING, "Configuration for the spin function is malformed. It will not be enabled.");
                            _enableSpin = false;
                        }
                    }
                    else
                    {
                        _logger.Log(LoggingService.LogLevel.WARNING, "No configuration for the spin function. It will not be enabled");
                        _enableSpin = false;
                    }

                    if(!_enableSpin && !_enableButton)
                    {
                        _logger.Log(LoggingService.LogLevel.ERROR, $"No features enabled! Exiting.");
                        Environment.Exit(ExitCode.CONFIG_NO_FEATURES);
                    }

                    // Everything is good
                    _logger.Log(LoggingService.LogLevel.INFO, "Parsing complete!");
                }
                catch (Exception ex)
                {
                    _logger.Log(LoggingService.LogLevel.ERROR, $"Exception: {ex.Message} - {ex.StackTrace}");
                    Environment.Exit(ExitCode.CONFIG_FILE_MALFORMED);
                }
            }
        }

        private bool ParseSpinConfig(YamlMappingNode spinRootNode)
        {
            // Parse consolation first
            if (spinRootNode.Children.ContainsKey(new YamlScalarNode("consolation")))
            {
                _logger.Log(LoggingService.LogLevel.DEBUG, "Found configuration for the consolation function. Attempting to parse.");
                if(spinRootNode.Children[new YamlScalarNode("consolation")] is YamlMappingNode consolationRootNode)
                {
                    _enableConsolation = ParseConsolationConfig(consolationRootNode);

                    if(_enableConsolation)
                    {
                        _membersWithPrizeDict.Add(_consolationPrize.Name, new List<ulong>());
                    }
                }
                else
                {
                    _logger.Log(LoggingService.LogLevel.WARNING, "Consolation configuration is malformed. It will not be enabled");
                    _enableConsolation = false;
                }
                
            }
            else
            {
                _logger.Log(LoggingService.LogLevel.WARNING, "No consolation configuration. It will not be enabled");
                _enableConsolation = false;
            }

            // Parse penalty configs
            if (!TryParseFromChildren("spin-penalty-reset", spinRootNode, out SPIN_PENALTY_RESET_TIME))
            {
                // This is non fatal
                _logger.Log(LoggingService.LogLevel.INFO, $"Consecutive spin penalty will not be enabled.");
                _enableConsecutiveSpinPenalty = false;
            }

            if (!TryParseFromChildren("consecutive-spins-before-penalty", spinRootNode, out SPINS_BEFORE_PENALTY))
            {
                // This is non fatal
                _logger.Log(LoggingService.LogLevel.INFO, $"Consecutive spin penalty will not be enabled.");
                _enableConsecutiveSpinPenalty = false;
            }

            // Parse prizes

            // Scan each child of the spin node to see if any start with "prize"
            foreach (var nodeMapping in spinRootNode.Children)
            {
                YamlNode node = nodeMapping.Value;
                string nodeTag = (nodeMapping.Key as YamlScalarNode).Value;

                // Parse each prize
                if(node.NodeType == YamlNodeType.Mapping && nodeTag.StartsWith("prize"))
                {
                    Prize prize = new Prize();
                    if(!TryParsePrizeConfig(node as YamlMappingNode, out prize))
                    {
                        _logger.Log(LoggingService.LogLevel.WARNING, $"Could not parse config for {node.Tag}");
                    }
                    else
                    {
                        _logger.Log(LoggingService.LogLevel.DEBUG, $"Successfully parsed prize {node.Tag}");
                        _possiblePrizes.Add(prize);
                        _membersWithPrizeDict.Add(prize.Name, new List<ulong>());
                    }
                }
            }

            if(_possiblePrizes.Count == 0)
            {
                _logger.Log(LoggingService.LogLevel.WARNING, "No prizes configured. Spin functiony will not be enabled.");
                return false;
            }

            // We're here, all values are assigned. Success
            return true;
        }

        private bool ParseConsolationConfig(YamlMappingNode consolationRootNode)
        {
            // The consolation config is there, start parsing elements

            // Message
            if (TryParseFromChildren("message", consolationRootNode, out string message))
            {
                _consolationPrize.Message = message;
            }
            else
            {
                _logger.Log(LoggingService.LogLevel.WARNING, "Consolation message not found. Consolation prize will not be enabled");
                return false;
            }

            // Description
            if (TryParseFromChildren("description", consolationRootNode, out string descrip))
            {
                _consolationPrize.Description = descrip;
            }
            else
            {
                _logger.Log(LoggingService.LogLevel.WARNING, "Consolation description not found. Consolation prize will not be enabled");
                return false;
            }

            // Role Id
            if (TryParseFromChildren("role-id", consolationRootNode, out ulong roleId))
            {
                _consolationPrize.RoleId = roleId;
            }
            else
            {
                _logger.Log(LoggingService.LogLevel.WARNING, "Consolation role ID not found. Consolation prize will not be enabled");
                return false;
            }

            // Role Time (OPTIONAL)
            if (TryParseFromChildren("role-time", consolationRootNode, out int roleTime))
            {
                _consolationPrize.RoleTime = roleTime;
            }

            // Role Time Varation (OPTIONAL)
            if (TryParseFromChildren("role-time-variation", consolationRootNode, out int variation))
            {
                _consolationPrize.RoleTimeVariation = variation;
            }

            // Whether or not role silences (OPTIONAL)
            if (TryParseFromChildren("is-silencing-role", consolationRootNode, out bool isSilencing))
            {
                _consolationPrize.IsSilencingRole = isSilencing;
            }

            // Whether or not to move use back after silence (OPTIONAL)
            if (TryParseFromChildren("move-user-back-after-silence", consolationRootNode, out bool moveBack))
            {
                _consolationPrize.MoveUserBackAfterSilence = moveBack;
            }

            _consolationPrize.Name = "Consolation Prize";

            return true;
        }

        private bool TryParsePrizeConfig(YamlMappingNode prizeRootNode, out Prize prize)
        {
            prize = new Prize();

            // Parse type of prize
            String valueString = ParseYamlScalarNodeValue("type", prizeRootNode);
            if (valueString == null)
            {
                _logger.Log(LoggingService.LogLevel.WARNING, $"Node {prizeRootNode.Tag} does not define a valid prize type.");
                return false;
            }
            else if (valueString.ToLower() == "role")
            {
                prize.Type = Prize.PrizeType.ROLE;
            }
            else
            {
                _logger.Log(LoggingService.LogLevel.WARNING, $"Node {prizeRootNode.Tag} does not define a valid prize type.");
                return false;
            }

            // Parse attributes

            // Name
            if (TryParseFromChildren("name", prizeRootNode, out string name))
            {
                prize.Name = name;
            }
            else
            {
                _logger.Log(LoggingService.LogLevel.WARNING, $"Node {prizeRootNode.Tag} does not define a name.");
                return false;
            }

            // Description
            if (TryParseFromChildren("description", prizeRootNode, out string descrip))
            {
                prize.Description = descrip;
            }
            else
            {
                _logger.Log(LoggingService.LogLevel.WARNING, $"Node {prizeRootNode.Tag} does not define a description.");
                return false;
            }

            // Image path (OPTIONAL)
            if (TryParseFromChildren("image-resource", prizeRootNode, out string imagePath))
            {
                prize.ImageResourcePath = imagePath;
            }

            // Message
            if (TryParseFromChildren("message", prizeRootNode, out string message))
            {
                prize.Message = message;
            }
            else
            {
                _logger.Log(LoggingService.LogLevel.WARNING, $"Node {prizeRootNode.Tag} does not define a message.");
                return false;
            }

            // Odds
            if (TryParseFromChildren("odds", prizeRootNode, out uint odds))
            {
                prize.Odds = odds;
            }
            else
            {
                _logger.Log(LoggingService.LogLevel.WARNING, $"Node {prizeRootNode.Tag} does not define odds.");
                return false;
            }

            // Parse prize type specific
            if (prize.Type == Prize.PrizeType.ROLE)
            {
                // Message
                if (TryParseFromChildren("role-id", prizeRootNode, out ulong roleId))
                {
                    prize.RoleId = roleId;
                }
                else
                {
                    _logger.Log(LoggingService.LogLevel.WARNING, $"Node {prizeRootNode.Tag} does not define a role-id.");
                    return false;
                }

                // Role Time (OPTIONAL)
                if (TryParseFromChildren("role-time", prizeRootNode, out int roleTime))
                {
                    prize.RoleTime = roleTime;
                }

                // Role Time Variation (OPTIONAL)
                if (TryParseFromChildren("role-time-variation", prizeRootNode, out int variation))
                {
                    prize.RoleTimeVariation = variation;
                }

                // Whether or not role silences (OPTIONAL)
                if (TryParseFromChildren("is-silencing-role", prizeRootNode, out bool silence))
                {
                    prize.IsSilencingRole = silence;
                }

                // Whether or not to move use back after silence (OPTIONAL)
                if (TryParseFromChildren("move-user-back-after-silence", prizeRootNode, out bool moveBack))
                {
                    prize.MoveUserBackAfterSilence = moveBack;
                }
            }

            return true;
        }

        private bool ParseButtonConfig(YamlMappingNode buttonRootNode)
        {

            // Parse role ID
            if (!TryParseFromChildren("role-id", buttonRootNode, out BUTTON_ROLE_ID))
            {
                return false;
            }

            // Parse the time the button is active
            if (!TryParseFromChildren("active-time", buttonRootNode, out BUTTON_ENABLE_TIME))
            {
                return false;
            }

            // Parse the time the button silences someone
            if (!TryParseFromChildren("role-time", buttonRootNode, out BUTTON_ROLE_TIME))
            {
                return false;
            }

            // Message
            if (!TryParseFromChildren("message", buttonRootNode, out BUTTON_MESSAGE))
            {
                return false;
            }

            // Whether or not role silences (OPTIONAL)
            TryParseFromChildren("is-silencing-role", buttonRootNode, out BUTTON_ROLE_SILENCES);

            // Whether or not to move use back after silence (OPTIONAL)
            TryParseFromChildren("move-user-back-after-silence", buttonRootNode, out BUTTON_MOVE_BACK_AFTER_SILENCE);

            // We're here, all values are assigned. Success
            return true;
        }

        // Templated parsing function to parse a generic object
        private Boolean TryParseFromChildren<T>(string yamlFieldName, YamlMappingNode parentNode, out T valueToSave)
        {
            // Parse string from node
            String valueString = ParseYamlScalarNodeValue(yamlFieldName, parentNode);
            if (valueString != null)
            {
                // Try to convert
                TypeConverter converter = TypeDescriptor.GetConverter(typeof(T));
                if (converter != null)
                {
                    try
                    {
                        // Success if no exception
                        valueToSave = (T)converter.ConvertFromString(valueString);
                        _logger.Log(LoggingService.LogLevel.DEBUG, $"Successfully converted {valueString} to {typeof(T).Name}!");
                        return true;
                    }
                    catch
                    {
                        _logger.Log(LoggingService.LogLevel.DEBUG, $"Failed to convert {valueString} to {typeof(T).Name}!");
                        valueToSave = default(T);
                        return false;
                    }
                }
                else
                {
                    _logger.Log(LoggingService.LogLevel.DEBUG, $"No converter exists for {typeof(T).Name}! Failed to parse {valueString}");
                    valueToSave = default(T);
                    return false;
                }
            }
            else
            {
                // Logging handled in ParseYamlScalarNodeValue
                valueToSave = default(T);
                return false;
            }
        }

        // Fetches a scalar string value from a node in the child if it exists, returns null otherwise
        private String ParseYamlScalarNodeValue(string nodeId, YamlMappingNode parentNode)
        {
            if (parentNode.Children.ContainsKey(new YamlScalarNode(nodeId)))
            {
                _logger.Log(LoggingService.LogLevel.DEBUG, $"Found {nodeId}. Attempting to parse.");

                if (!(parentNode.Children[new YamlScalarNode(nodeId)] is YamlScalarNode node))
                {
                    _logger.Log(LoggingService.LogLevel.WARNING, $"{nodeId} is not type scalar.");
                    return null;
                }
                else if (node.Value == "")
                {
                    _logger.Log(LoggingService.LogLevel.WARNING, $"{nodeId} is empty.");
                    return null;
                }
                else
                {
                    _logger.Log(LoggingService.LogLevel.DEBUG, $"{nodeId} has value: {node.Value}");
                    return node.Value;
                }
            }
            else
            {
                _logger.Log(LoggingService.LogLevel.WARNING, $"{nodeId} not found under {parentNode.Tag}");
                return null;
            }
        }

        internal String GetBotToken()
        {
            return BOT_TOKEN;
        }

        #endregion

        #region Big Red Button

        /**************************************************
        * Don't push the button
        **************************************************/
        internal bool IsBigRedButtonEnabled()
        {
            return _enableButton;
        }

        internal bool IsBigRedButtonActive()
        {
            return _bigRedButtonCurrentlyActive;
        }

        internal void ShowBigRedButton(IMessageChannel channel, IUser user)
        {
            _logger.Log(LoggingService.LogLevel.INFO, $"{user.Username} has enabled the big red button.");

            // Display text
            channel.SendMessageAsync($"{user.Mention} has enabled the Big Red Button! The button will be active for {BUTTON_ENABLE_TIME / 1000 / 60} minutes!");
            channel.SendMessageAsync(BUTTON_MESSAGE);
            channel.SendMessageAsync($"Use {CommandHandlerService.CommandPrefix}SMASH to press it!");
            channel.SendFileAsync(@"Resources/big-red-button.jpg");

            // Make button active
            _bigRedButtonCurrentlyActive = true;

            // Start timer to turn button off
            Task task = Task.Delay(BUTTON_ENABLE_TIME).ContinueWith(t => DisableBigRedButton(channel));
        }

        internal void DisableBigRedButton(IMessageChannel channel)
        {
            _bigRedButtonCurrentlyActive =  false;

            _logger.Log(LoggingService.LogLevel.INFO, $"The big red button has been deactivated.");
            channel.SendMessageAsync("The Big Red Button has been deactivated");
        }

        // Funtion to give button role
        internal async void GiveButtonRole(IMessageChannel channel, IGuildUser user)
        {
            _logger.Log(LoggingService.LogLevel.INFO, $"{user.Username} has pressed the button.");

            // Check that user isn't already silenced
            // (How did you get here if so?)
            if (_membersWithButtonRole.Contains(user.Id))
            {
                _logger.Log(LoggingService.LogLevel.WARNING, $"{user.Username} already has a button role.");
                return;
            }

            _logger.Log(LoggingService.LogLevel.DEBUG, $"{user.Username} has pressed the button and will have the role {BUTTON_ROLE_ID} for {BUTTON_ROLE_TIME / 1000} seconds.");

            // Add to list
            _membersWithButtonRole.Add(user.Id);
            
            // Add silence
            IRole role = user.Guild.GetRole(BUTTON_ROLE_ID);
            if (role == null)
            {
                _logger.Log(LoggingService.LogLevel.ERROR, $"Role ID {BUTTON_ROLE_ID} was not found in the server!.");
                return;
            }

            await user.AddRoleAsync(role);

            // Need this outside in order to pass to task below
            IVoiceChannel oldChannel = null;

            // If silencing, save old channel to move them back after the silence
            // We need to move them to AFK instead since disconnecting them doesn't let 
            // us move them back
            if (BUTTON_ROLE_SILENCES)
            {
                IVoiceChannel afkChannel = null;

               
                if (user.VoiceChannel != null)
                {
                    _logger.Log(LoggingService.LogLevel.DEBUG, $"{user.Username} is in a voice channel and will be muted.");
                    oldChannel = user.VoiceChannel;
                    afkChannel = await user.Guild.GetAFKChannelAsync();

                    var test = user.ModifyAsync(x =>
                    {
                        x.Mute = true;

                        if (afkChannel != null)
                            x.Channel = Optional.Create<IVoiceChannel>(afkChannel);
                        else
                            _logger.Log(LoggingService.LogLevel.WARNING, $"AFK channel doesn't exist, silence may not work as expected for voice chats.");
                    });
                }
                else
                {
                    _logger.Log(LoggingService.LogLevel.DEBUG, $"{user.Username} is not in a voice channel and will not be muted.");
                }
            }

            // Show picture
            await channel.SendFileAsync(@"Resources/pressed.png");

            // Let everyone know they're an idiot
            await channel.SendMessageAsync($"{user.Mention} has pressed the Big Red Button!");
            await channel.SendMessageAsync($"They will have the role for {BUTTON_ROLE_TIME / 1000} seconds!");


            // If the role should be removed after a time delay, start the timer
            if (BUTTON_ROLE_TIME != default(int))
            {
                try
                {
                    Task task = Task.Delay(BUTTON_ROLE_TIME).ContinueWith(t => RemoveButtonRole(user, oldChannel));
                }
                catch (Exception ex)
                {
                    if (ex.InnerException != null)
                        _logger.Log(LoggingService.LogLevel.WARNING, $"{ex.InnerException.Message} - {ex.InnerException.StackTrace}");

                    _membersWithButtonRole.Remove(user.Id);
                }
            }
        }

        // Funtion to remove user's button role
        internal async void RemoveButtonRole(IGuildUser user, IVoiceChannel channelToJoin)
        {
            _logger.Log(LoggingService.LogLevel.INFO, $"Removing {user.Username}'s button role.");

            // Remove role
            IRole role = user.Guild.GetRole(BUTTON_ROLE_ID);
            if (role == null)
            {
                _logger.Log(LoggingService.LogLevel.ERROR, $"Role ID {BUTTON_ROLE_ID} was not found in the server!.");
                return;
            }

            await user.RemoveRoleAsync(role);

            // If the role silenced, unsilence them
            if (BUTTON_ROLE_SILENCES)
            {
                // Unmute user and move them back to the old channel
                if (channelToJoin != null)
                {
                    _logger.Log(LoggingService.LogLevel.DEBUG, $"{user.Username} was not a voice channel and will be unmuted and replaced.");

                    var test = user.ModifyAsync(x =>
                    {
                        x.Mute = false;
                        x.Channel = Optional.Create<IVoiceChannel>(channelToJoin);
                    });
                }
                else
                {
                    _logger.Log(LoggingService.LogLevel.DEBUG, $"{user.Username} was not in a voice channel, no voice settings were altered.");
                }
            }

            // Remove from list
            _membersWithButtonRole.Remove(user.Id);

            // Message user about silence being up if allowed
            SendMessageToUserAsync(user, "Your Big Red Button role has been lifted!");
        }


        #endregion

        #region Spin Spin Spin!

        /**************************************************
        * Spin spin spin!
        **************************************************/
        internal bool IsSpinEnabled()
        {
            return _enableSpin;
        }

        internal Prize GetPrize(string prizeName)
        {
            _logger.Log(LoggingService.LogLevel.DEBUG, $"Checking for prize {prizeName}.");

            Prize prize = null;

            // First check consolation prize
            if (prizeName == _consolationPrize.Name)
            {
                _logger.Log(LoggingService.LogLevel.DEBUG, $"{prizeName} is the consulation prize.");

                prize = _consolationPrize;
            }
            else
            {
                foreach (Prize tempPrize in _possiblePrizes)
                {
                    _logger.Log(LoggingService.LogLevel.DEBUG, $"Checking if {tempPrize.Name} is the prize.");

                    if (tempPrize.Name == prizeName)
                    {
                        _logger.Log(LoggingService.LogLevel.DEBUG, $"Found the prize.");
                        prize = tempPrize;
                        break;
                    }
                }
            }

            if(prize == null)
            {
                _logger.Log(LoggingService.LogLevel.DEBUG, $"No prize {prizeName} found.");
            }

            return prize;
        }

        internal void SpinTheWheel(IGuild guild, IMessageChannel channel, IGuildUser user)
        {
            // Increase the spin penalty if a user spins too many times without waiting if the penalty is enabled
            if (_enableConsecutiveSpinPenalty)
            {
                if (!_userTimers.ContainsKey(user.Id))
                {
                    _logger.Log(LoggingService.LogLevel.DEBUG, $"Creating a timer for {user.Username}.");

                    Timer timer = new Timer();

                    timer.Elapsed += (sender, e) => {
                        _numTimesSpunWithoutBreak[user.Id] = 0;
                        timer.Interval = SPIN_PENALTY_RESET_TIME;
                        _logger.Log(LoggingService.LogLevel.DEBUG, $"Spin penalty for {user.Username} reset.");
                    };

                    timer.Interval = SPIN_PENALTY_RESET_TIME;
                    timer.AutoReset = false;
                    _userTimers.Add(user.Id, timer);
                }

                if (!_numTimesSpunWithoutBreak.ContainsKey(user.Id))
                {
                    _numTimesSpunWithoutBreak[user.Id] = 0;
                }

                _userTimers[user.Id].Stop();
                _numTimesSpunWithoutBreak[user.Id]++;

                _logger.Log(LoggingService.LogLevel.DEBUG, $"{user.Username} has spun {_numTimesSpunWithoutBreak[user.Id]} without a break.");
            }

            // Roll for rewards. Note that rolling multiple times for each doesn't change the
            // chances in any real way. While there may be some chance lost due to the seeding
            // it's close enough for our purposes and is more beneficial for logging

            // This isn't a perfect chance because the first prize will trigger a break so prizes defined earlier
            // in the config are checked first. This means that you should arrange a config file such that
            // the lowest chance prizes are first if you want to get as close as possible to stastical random.
            // However this is not enforced and left in intentionally, if you want to abuse this limitation
            // in your configuration, you are welcome to

            // We can compute these by saying any number is equally likely, therefore we can just compare to 0
            bool wonPrize = false;
            foreach (Prize prize in _possiblePrizes)
            {
                int prizeLuckyNumber = _random.Next((int)prize.Odds);
                _logger.Log(LoggingService.LogLevel.DEBUG, $"{user.Username} has spun a {prizeLuckyNumber} for {prize.Name}. Needed a 0.");

                if(prizeLuckyNumber == 0)
                {
                    _logger.Log(LoggingService.LogLevel.INFO, $"{user.Username} has won {prize.Name}!");
                    GivePrize(guild, channel, user, prize);

                    wonPrize = true;
                    break;
                }
            }

            // If they didn't win anything and the consolation prize is enabled, then give consolation prize
            if(!wonPrize && _enableConsolation)
            {
                GivePrize(guild, channel, user, _consolationPrize);
                _logger.Log(LoggingService.LogLevel.INFO, $"{user.Username} has gotten a consolation prize.");
            }
        }

        internal async void ShowPrizeList(IMessageChannel channel)
        {
            _logger.Log(LoggingService.LogLevel.DEBUG, $"Attempting to show prize list.");

            string prizeList = "The current prizes are:\n";

            foreach (Prize prize in _possiblePrizes)
            {
                prizeList += $"> {prize.Name} (1 in {prize.Odds} chance): {prize.Description}!\n";
            }

            if(_enableConsolation)
            {
                prizeList += $"> {_consolationPrize.Name}: {_consolationPrize.Description}!";
            }

            await channel.SendMessageAsync(prizeList);
        }

        internal async void GivePrize(IGuild guild, IMessageChannel channel, IGuildUser user, Prize prize)
        {
            _logger.Log(LoggingService.LogLevel.INFO, $"Giving {user.Username} prize {prize.Name}.");

            // Check that user doesn't already have that prize
            if (_membersWithPrizeDict[prize.Name].Contains(user.Id))
            {
                _logger.Log(LoggingService.LogLevel.INFO, $"{user.Username} already has that prize.");
                await channel.SendMessageAsync($"{user.Mention} won {prize.Name} but already has it!");
                return;
            }

            // Add to list
            _membersWithPrizeDict[prize.Name].Add(user.Id);

            // Give role
            if(prize.Type == Prize.PrizeType.ROLE)
            {
                IRole role = user.Guild.GetRole(prize.RoleId);
                if (role == null)
                {
                    _logger.Log(LoggingService.LogLevel.ERROR, $"{prize.Name} role ID {prize.RoleId} was not found in the server!.");
                    await channel.SendMessageAsync($"Something went wrong. Please contact your admin.");
                    return;
                }

                await user.AddRoleAsync(role);

                // Need to declare this outside, even if not a silencing role
                IVoiceChannel oldVoiceChannel = null;

                // If the role silences, do that
                if (prize.IsSilencingRole)
                {
                    _logger.Log(LoggingService.LogLevel.DEBUG, $"{user.Username} will be silenced.");

                    IVoiceChannel afkChannel = null;

                    // Save old channel to move them back after the silence
                    // We need to move them to AFK instead since disconnecting them doesn't let 
                    // us move them back
                    if (user.VoiceChannel != null)
                    {
                        _logger.Log(LoggingService.LogLevel.DEBUG, $"{user.Username} is in a voice channel and will be moved.");
                        oldVoiceChannel = user.VoiceChannel;
                        afkChannel = await user.Guild.GetAFKChannelAsync();
                    }

                    // Disconnect user and server mute them if they're currently in voice
                    var test = user.ModifyAsync(x =>
                    {
                        x.Mute = true;
                        if (afkChannel != null)
                            x.Channel = Optional.Create<IVoiceChannel>(afkChannel);
                        else
                            _logger.Log(LoggingService.LogLevel.WARNING, $"AFK channel doesn't exist, silence may not work as expected for voice chats.");
                    });
                }

                // Display
                if (prize.ImageResourcePath != default(string))
                {
                    await channel.SendFileAsync(prize.ImageResourcePath);
                }

                await channel.SendMessageAsync(prize.Message);

                // If the role is removed after a time, do so
                if (prize.RoleTime != default(int))
                {
                    // Start timer
                    try
                    {
                        int delay = prize.RoleTime;
                        if (prize.RoleTimeVariation != default(int))
                        {
                            delay = _random.Next(prize.RoleTime - prize.RoleTimeVariation, prize.RoleTime + prize.RoleTimeVariation);
                        }

                        // If this is a consolation prize, assess penalty if it exists
                        if(prize.Name == _consolationPrize.Name && _enableConsecutiveSpinPenalty && _numTimesSpunWithoutBreak[user.Id] > SPINS_BEFORE_PENALTY)
                        {
                            // Start multiplier at 2x
                            delay *= _numTimesSpunWithoutBreak[user.Id] - (int)SPINS_BEFORE_PENALTY + 1;
                            await channel.SendMessageAsync($"You're spinning too much! Penalty increased. Wait about {SPIN_PENALTY_RESET_TIME / 1000} seconds after the consolation is removed to reset this penalty.");

                            // Adjust timer interval
                            _userTimers[user.Id].Interval = _userTimers[user.Id].Interval + delay;
                        }

                        Task task = Task.Delay(delay).ContinueWith(t => RemovePrizeRole(user, prize, channel, oldVoiceChannel));

                        await channel.SendMessageAsync($"You will have this prize for {delay / 1000} seconds.");
                    }
                    catch (Exception ex)
                    {
                        if(ex.InnerException != null)
                            _logger.Log(LoggingService.LogLevel.WARNING, $"{ex.InnerException.Message} - {ex.InnerException.StackTrace}");

                        _membersWithPrizeDict[prize.Name].Remove(user.Id);
                    }
                }

            }
        }

        // Funtion to remove user's silence
        internal async void RemovePrizeRole(IGuildUser user, Prize prize, IMessageChannel channel, IVoiceChannel voiceChannelToJoin)
        {
            _logger.Log(LoggingService.LogLevel.INFO, $"Removing {user.Username}'s prize role for {prize.Name}.");

            // Remove role
            IRole role = user.Guild.GetRole(prize.RoleId);
            if (role == null)
            {
                _logger.Log(LoggingService.LogLevel.ERROR, $"{prize.Name} role ID {prize.RoleId} was not found in the server!.");
                await channel.SendMessageAsync($"Something went wrong. Please contact your admin.");
                return;
            }

            await user.RemoveRoleAsync(role);

            // Remove from list
            _membersWithPrizeDict[prize.Name].Remove(user.Id);

            if(prize.IsSilencingRole)
            {
                _logger.Log(LoggingService.LogLevel.DEBUG, $"Unsilencing {user.Username}.");
                var test = user.ModifyAsync(x =>
                {
                    x.Mute = false;

                    if (prize.MoveUserBackAfterSilence && voiceChannelToJoin != null)
                    {
                        x.Channel = Optional.Create<IVoiceChannel>(voiceChannelToJoin);
                    }
                });
            }

            // Message user about prize being removed
            SendMessageToUserAsync(user, $"Your prize role for {prize.Name} has been removed!");

            // If spin penalty is enabled, start the timer to remove it
            if(prize.Name == _consolationPrize.Name && _enableConsecutiveSpinPenalty)
            {
                _logger.Log(LoggingService.LogLevel.DEBUG, "Starting spin penalty reset timer.");
                _userTimers[user.Id].Start();
            }
        }

        #endregion

        #region Utilities

        internal async void SendMessageToUserAsync(IUser user, String message)
        {
            if(!SendDMs)
            {
                return;
            }

            try
            {
                await user.SendMessageAsync(message);
            }
            catch (HttpException ex)
            {
                _logger.Log(LoggingService.LogLevel.INFO, $"{user.Username} has DMs off, message will not be sent to them");
                _logger.Log(LoggingService.LogLevel.DEBUG, ex.ToString());
            }
            catch (Exception ex)
            {
                _logger.Log(LoggingService.LogLevel.WARNING, $"Unknown Error: {ex.ToString()}");
            }
        }

        #endregion
    }
}


