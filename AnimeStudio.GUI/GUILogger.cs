using System;
using System.Windows.Forms;

namespace AnimeStudio.GUI
{
    class GUILogger : ILogger
    {
        public bool ShowErrorMessage = true;
        public bool DeferErrors;
        public int DeferredErrorCount;
        public string FirstDeferredError;
        public bool WriteConsole;
        private Action<string> action;

        public bool Silent { get; set; }
        public LoggerEvent Flags { get; set; }

        public GUILogger(Action<string> action)
        {
            this.action = action;
        }

        public void Log(LoggerEvent loggerEvent, string message)
        {
            if (!Logger.Flags.HasFlag(loggerEvent) || Silent)
                return;
            if (WriteConsole) Console.WriteLine("[{0}] {1}", loggerEvent, message);

            switch (loggerEvent)
            {
                case LoggerEvent.Error:
                    if (DeferErrors)
                    {
                        if (System.Threading.Interlocked.Increment(ref DeferredErrorCount) == 1) FirstDeferredError = message;
                        action("Error: " + message);
                        break;
                    }
                    if (ShowErrorMessage && !WriteConsole)
                    {
                        MessageBox.Show(message);
                    }
                    break;
                default:
                    action(message);
                    break;
            }
        }
    }
}
