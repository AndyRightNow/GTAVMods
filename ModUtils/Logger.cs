using System;
using System.IO;
using System.Linq;
using System.Reflection;

namespace ADModUtils
{
    public class Logger
    {
        private static DeveloperConsole.DeveloperConsole developerConsole;

        public static void Init(DeveloperConsole.DeveloperConsole developerConsole)
        {
            Logger.developerConsole = developerConsole;
        }

        private static string FormatMessage(string logLevel, params string[] message)
        {
            var datetime = DateTime.Now;

            var output = string.Concat("[", datetime.ToString("HH:mm:ss"), "] ", logLevel, " ");


            foreach (string str in message)
            {
                output = string.Concat(output, " ", str);
            }

            return output;
        }

        public static void Log(string logLevel, params string[] message)
        {
            var datetime = DateTime.Now;
            string logPath = Path.ChangeExtension(Assembly.GetExecutingAssembly().Location, ".log");
            if (!File.Exists(logPath))
            {
                File.WriteAllText(logPath, String.Empty);
            }

            try
            {
                var fs = new FileStream(logPath, FileMode.Append, FileAccess.Write, FileShare.Read);
                var sw = new StreamWriter(fs);

                try
                {
                    sw.Write(FormatMessage(logLevel, message));

                    foreach (string str in message)
                    {
                        sw.Write(str);
                    }

                    sw.WriteLine();
                }
                finally
                {
                    sw.Close();
                    fs.Close();
                }
            }
            catch (Exception)
            {
            }
        }

        public static void LogConsole(string logLevel, params string[] message)
        {
            if (developerConsole == null)
            {
                return;
            }

            developerConsole.PrintLine(FormatMessage(logLevel, message));
        }
    }
}
