using CommandLine;
using CommandLine.Text;
using SpinTheWheel.Utilities;
using System;
using System.Collections.Generic;

namespace SpinTheWheel
{
    class Program
    {
        private readonly static String VERSION = "v0.8";

        static void Main(string[] args)
        {
            // Start bot with command line options
            Parser parser = new Parser(with => with.HelpWriter = null);
            ParserResult<CommandLineOptions> result = parser.ParseArguments<CommandLineOptions>(args);

            result.WithParsed(options =>
                new SpinTheWheelBot(options).Start().GetAwaiter().GetResult()
            )
            .WithNotParsed(errs => DisplayHelp(result, errs));
        }

        // Displays the help text if commands were not parsed
        private static void DisplayHelp(ParserResult<CommandLineOptions> result, IEnumerable<Error> errs)
        {
            String helpText = HelpText.AutoBuild(result, h =>
            {
                h.AdditionalNewLineAfterOption = false;
                h.Heading = $"Spin The Wheel {VERSION}";
                h.Copyright = "Licensed under the GPL-3.0";

                return HelpText.DefaultParsingErrorsHandler(result, h);
            }, e => e);
            Console.WriteLine(helpText);
        }
    }
}
