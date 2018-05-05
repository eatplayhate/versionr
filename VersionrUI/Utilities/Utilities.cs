using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Media;

namespace VersionrUI
{
    internal class Utilities
    {
        public static bool IsVersionrPath(string path)
        {
            if (string.IsNullOrEmpty(path))
                return false;

            DirectoryInfo directoryInfo = new DirectoryInfo(path);
            {
                bool loaded = false;
                using (var ws = Versionr.Area.Load(directoryInfo))
                {
                    loaded = ws != null;
                }
                return loaded;
            }
        }
        public static IEnumerable<T> FindVisualChildren<T>(DependencyObject depObj) where T : DependencyObject
        {
            if (depObj != null)
            {
                for (int i = 0; i < VisualTreeHelper.GetChildrenCount(depObj); i++)
                {
                    DependencyObject child = VisualTreeHelper.GetChild(depObj, i);

                    if (child != null && child is T)
                        yield return (T)child;

                    foreach (T childOfChild in FindVisualChildren<T>(child))
                        yield return childOfChild;
                }
            }
        }

        // Run versionr command on the command line
        private static string s_outString;
        public static string RunOnCommandLine(string arguments, string workingdir = "")
        {
            s_outString = "";
            Process process = new Process
            {
                StartInfo =
                {
                    FileName = "cmd.exe",
                    Arguments = arguments,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                }
            };
            if (workingdir != "")
                process.StartInfo.WorkingDirectory = workingdir;

            // Setup output and error (asynchronous) handlers
            process.OutputDataReceived += GetProcessOutput;
            process.ErrorDataReceived += GetProcessOutput;

            // Start process and handlers
            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            process.WaitForExit();

            return s_outString;
        }

        private static void GetProcessOutput(object sender, DataReceivedEventArgs e)
        {
            s_outString += e.Data + Environment.NewLine;
        }
    }
}
