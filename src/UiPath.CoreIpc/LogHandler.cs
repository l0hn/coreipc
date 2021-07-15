using System;
using System.Runtime.CompilerServices;

namespace UiPath.CoreIpc
{
    public static class LogHandler
    {
        public static event EventHandler<string> LogMessage;

        public static void Log(string message)
        {
            LogMessage?.Invoke(null, message);
        }
    }
}