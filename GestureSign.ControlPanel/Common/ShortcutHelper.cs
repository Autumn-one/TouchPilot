using System;
using System.Runtime.InteropServices;

namespace GestureSign.ControlPanel.Common
{
    internal static class ShortcutHelper
    {
        private const string ShellProgId = "WScript.Shell";

        public static string GetTargetPath(string shortcutPath)
        {
            dynamic shell = null;
            dynamic shortcut = null;
            try
            {
                shell = CreateShell();
                shortcut = shell.CreateShortcut(shortcutPath);
                return shortcut.TargetPath;
            }
            finally
            {
                ReleaseComObject(shortcut);
                ReleaseComObject(shell);
            }
        }

        public static void Create(string shortcutPath, string targetPath, string description)
        {
            dynamic shell = null;
            dynamic shortcut = null;
            try
            {
                shell = CreateShell();
                shortcut = shell.CreateShortcut(shortcutPath);
                shortcut.TargetPath = targetPath;
                shortcut.WindowStyle = 7;
                shortcut.Arguments = string.Empty;
                shortcut.Description = description;
                shortcut.Save();
            }
            finally
            {
                ReleaseComObject(shortcut);
                ReleaseComObject(shell);
            }
        }

        private static object CreateShell()
        {
            Type shellType = Type.GetTypeFromProgID(ShellProgId, true);
            return Activator.CreateInstance(shellType);
        }

        private static void ReleaseComObject(object value)
        {
            if (value != null && Marshal.IsComObject(value))
                Marshal.FinalReleaseComObject(value);
        }
    }
}
