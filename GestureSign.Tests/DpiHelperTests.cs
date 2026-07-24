using GestureSign.Daemon.Native;
using System.Drawing;
using Xunit;

namespace GestureSign.Tests
{
    public class DpiHelperTests
    {
        [Fact]
        [Trait("Category", "WindowsIntegration")]
        public void SystemAndScreenDpiRemainUsableAcrossRepeatedQueries()
        {
            int systemDpi = DpiHelper.GetSystemDpi();
            Assert.True(systemDpi > 0, $"The system DPI was {systemDpi}.");

            Point screenPoint = System.Windows.Forms.Cursor.Position;
            int screenDpi = DpiHelper.GetScreenDpi(screenPoint);
            Assert.True(screenDpi > 0, $"The screen DPI was {screenDpi} at {screenPoint}.");

            for (int index = 0; index < 128; index++)
                Assert.Equal(systemDpi, DpiHelper.GetSystemDpi());
        }
    }
}
