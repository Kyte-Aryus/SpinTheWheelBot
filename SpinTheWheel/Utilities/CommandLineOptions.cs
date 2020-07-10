/**
 * Provides the command line arguments to the application
 **/
using CommandLine;

namespace SpinTheWheel.Utilities
{
    public class CommandLineOptions
    {
        [Option('s', "silent", Required = false, HelpText = "Turns console logging off")]
        public bool SilentMode { get; set; }

        [Option('f', "log-file", Required = false, Default = "log.txt", HelpText = "Logs to specified file (can be used with -s)")]
        public string LogFile { get; set; }

        [Option('c', "cfg-file", Required = false, Default = "cfg.yaml", HelpText = "The configuration YAML file")]
        public string CfgFile { get; set; }

        [Option('d', "debug", Required = false, HelpText = "Turns debug level logging on")]
        public bool Debug { get; set; }
    }
}
