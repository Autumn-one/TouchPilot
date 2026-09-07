using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Security.Principal;
using System.Threading;
using System.Threading.Tasks;
using GestureSign.Common.Plugins;
using GestureSign.CorePlugins.OpenFile;
using Newtonsoft.Json;

namespace GestureSign.Tests.DesktopHost
{
    internal static class OpenFileProbe
    {
        internal static int Run(string[] args)
        {
            if (args.Length < 2)
                return 2;

            if (args[0] == "--open-file-report")
            {
                WriteReport(args[1], args.Skip(2).ToArray());
                return 0;
            }

            if (args[0] != "--open-file-matrix")
                return 2;

            string directory = args[1];
            try
            {
                WriteReport(Path.Combine(directory, "parent.json"), Array.Empty<string>());
                foreach (bool elevated in new[] { false, true })
                {
                    string report = Path.Combine(directory, elevated ? "administrator.json" : "normal.json");
                    var plugin = new OpenFilePlugin();
                    plugin.Deserialize(JsonConvert.SerializeObject(new
                    {
                        Path = Environment.ProcessPath,
                        Variables = $"--open-file-report \"{report}\" \"two words\" \"a&b\" %GS_StartPoint_X%",
                        RunAsAdministrator = elevated
                    }));
                    var points = new List<Point> { new Point(17, 29) };
                    var pointInfo = new PointInfo(points, new List<List<Point>> { points }, null,
                        new SynchronizationContext());
                    bool started = Task.Run(() => plugin.Gestured(pointInfo)).GetAwaiter().GetResult();
                    if (!started || !SpinWait.SpinUntil(() => File.Exists(report), TimeSpan.FromSeconds(30)))
                        throw new InvalidOperationException("The permission probe did not complete: " + elevated);
                }
                return 0;
            }
            catch (Exception exception)
            {
                File.WriteAllText(Path.Combine(directory, "error.txt"), exception.ToString());
                return 1;
            }
        }

        private static void WriteReport(string path, string[] arguments)
        {
            using var identity = WindowsIdentity.GetCurrent();
            string json = JsonConvert.SerializeObject(new
            {
                IsAdministrator = new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator),
                UserSid = identity.User.Value,
                WorkingDirectory = Environment.CurrentDirectory,
                Arguments = arguments
            });
            File.WriteAllText(path + ".tmp", json);
            File.Move(path + ".tmp", path);
        }
    }
}
