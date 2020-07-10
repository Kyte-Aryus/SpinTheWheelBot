using System;
using System.Collections.Generic;
using System.Text;

namespace SpinTheWheel.Utilities
{
    class ExitCode
    {
        public static int CONFIG_FILE_NOT_FOUND = 1;
        public static int CONFIG_FILE_MALFORMED = 2;
        public static int CONFIG_NO_FEATURES = 3;
        public static int BOT_TOKEN_NOT_PROVIDED = 4;
    }
}
