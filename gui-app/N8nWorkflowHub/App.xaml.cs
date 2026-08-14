using System;
using System.IO;
using System.Windows;
using System.Windows.Threading;

namespace N8nWorkflowHub
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            AppDomain.CurrentDomain.UnhandledException += (s, args) =>
            {
                LogCrash("AppDomain.UnhandledException", args.ExceptionObject as Exception);
            };

            DispatcherUnhandledException += (s, args) =>
            {
                LogCrash("DispatcherUnhandledException", args.Exception);
                args.Handled = true;
            };

            base.OnStartup(e);
        }

        private void LogCrash(string source, Exception? ex)
        {
            string logPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "N8nWorkflowHub",
                "crash.log"
            );

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);
                File.AppendAllText(logPath, $"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}] [{source}] {ex}\n\n");
                MessageBox.Show($"An unexpected error occurred:\n\n{ex?.Message}\n\nDetails saved to:\n{logPath}", "n8n Workflow Hub Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            catch
            {
                // Fallback
            }
        }
    }
}
