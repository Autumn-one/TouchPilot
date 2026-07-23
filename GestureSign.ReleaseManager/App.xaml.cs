using System;
using System.Windows;

namespace GestureSign.ReleaseManager
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            if (e.Args.Length != 0)
            {
                InitializeSigningKey(e.Args);
                return;
            }

            new MainWindow().Show();
        }

        private void InitializeSigningKey(string[] args)
        {
            if (args.Length != 2 || !string.Equals(args[0], "--initialize-signing-key",
                    StringComparison.Ordinal))
            {
                Console.Error.WriteLine(
                    "Usage: GestureSign.ReleaseManager.exe --initialize-signing-key <source-directory>");
                Shutdown(2);
                return;
            }

            try
            {
                SigningKeyBootstrapResult result = ReleaseSigningKeyBootstrapper.Initialize(args[1]);
                Console.WriteLine("TouchPilot signing key ready. Key ID: " + result.KeyId);
                Console.WriteLine("Public key: " + result.PublicKeyPath);
                Shutdown(0);
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine("Signing key initialization failed: " + exception.Message);
                Shutdown(1);
            }
        }
    }
}
