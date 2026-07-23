using GestureSign.Common;
using Xunit;

namespace GestureSign.Tests
{
    public class ProductIdentityTests
    {
        [Fact]
        public void RuntimeIdentityUsesTouchPilotNames()
        {
            Assert.Equal("TouchPilot", Constants.ProductName);
            Assert.Equal("TouchPilot.exe", Constants.DaemonFileName);
            Assert.Equal("TouchPilot.ControlPanel.exe", Constants.ControlPanelFileName);
            Assert.Equal("TouchPilot.Updater.exe", Constants.UpdaterFileName);
            Assert.Equal("Autumn-one/TouchPilot", Constants.DefaultGitHubRepository);
            Assert.Equal("TouchPilot", typeof(GestureSign.Daemon.Program).Assembly.GetName().Name);
            Assert.Equal("TouchPilot.ControlPanel",
                typeof(GestureSign.ControlPanel.App).Assembly.GetName().Name);
            Assert.Equal("TouchPilot.Updater",
                typeof(GestureSign.Updater.Program).Assembly.GetName().Name);
            Assert.Equal("TouchPilot.ReleaseManager",
                typeof(GestureSign.ReleaseManager.App).Assembly.GetName().Name);
        }
    }
}
