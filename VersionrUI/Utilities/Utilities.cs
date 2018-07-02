using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;
using VersionrUI.ViewModels;

namespace VersionrUI
{
    internal class Utilities
    {
        public static string RepoName => "osiris";
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
            if (depObj == null)
                yield break;
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(depObj); i++)
            {
                DependencyObject child = VisualTreeHelper.GetChild(depObj, i);

                if (child is T variable)
                    yield return variable;

                foreach (T childOfChild in FindVisualChildren<T>(child))
                    yield return childOfChild;
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

        public static void GeneratePatchFile(IEnumerable<string> files, string workingDir, string version = null)
        {
            try
            {
                SaveFileDialog dialog = new SaveFileDialog
                {
                    Title = "Save Patch File",
                    Filter = "Patch Files|*.patch;"
                };

                if (dialog.ShowDialog() != true)
                    return;

                string allFileNamesString = files.Aggregate("", (current, file) => current + (file + " "));
                string args = $"/c \"vsr diff {allFileNamesString}\"";
                if (!string.IsNullOrEmpty(version))
                    args += " -v " + version;
                string patch = RunOnCommandLine(args, workingDir);
                File.WriteAllText(dialog.FileName, patch);

                if (File.Exists(dialog.FileName))
                    MessageBox.Show("Patch file created successfully");
            }
            catch (Exception e)
            {
                MessageBox.Show(e.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        public static List<Versionr.Objects.Version> FindVersionsWithID(AreaVM area, string versionID)
        {
            try
            {
                if (area?.Area == null || string.IsNullOrEmpty(versionID))
                    return null;

                var found = area.Area.GetPotentionalVersions(versionID);

                // If not found...
                if (found != null && found.Any())
                    return found;
                MainWindow.Instance.Dispatcher.Invoke(() =>
                {
                    MainWindow.ShowMessage(
                        "Repo: " + area.Name,
                        $"Could not find a version with ID: [{versionID}] in this repo");
                });
                return null;

            }
            catch (Exception)
            {
                MainWindow.Instance.Dispatcher.Invoke(() =>
                {
                    MainWindow.ShowMessage(
                        "Something went wrong !!!",
                        $"And you thought it was going to crash... how do you like this graceful exit");
                });
                return null;
            }
        }
    }
}
