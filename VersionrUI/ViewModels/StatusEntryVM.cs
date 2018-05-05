using MahApps.Metro.Controls.Dialogs;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using Microsoft.Win32;
using Versionr;
using Versionr.Utilities;
using VersionrUI.Commands;
using VersionrUI.Controls;
using VersionrUI.Dialogs;

namespace VersionrUI.ViewModels
{
    public class StatusEntryVM : NotifyPropertyChangedBase
    {
        public DelegateCommand DiffCommand { get; private set; }
        public DelegateCommand LogCommand { get; private set; }
        public DelegateCommand RevertCommand { get; private set; }
        public DelegateCommand OpenInExplorerCommand { get; private set; }
        public DelegateCommand GeneratePatchFileCommand { get; private set; }

        private readonly StatusVM m_StatusVM;
        private readonly Area m_Area;

        public StatusEntryVM(Status.StatusEntry statusEntry, StatusVM statusVM, Area area)
        {
            StatusEntry = statusEntry;
            m_StatusVM = statusVM;
            m_Area = area;

            DiffCommand = new DelegateCommand(Diff);
            LogCommand = new DelegateCommand(Log);
            RevertCommand = new DelegateCommand(() => Load(RevertSelected));
            OpenInExplorerCommand = new DelegateCommand(OpenInExplorer);
            GeneratePatchFileCommand = new DelegateCommand(GeneratePatchFile);
        }

        public Versionr.StatusCode Code => StatusEntry.Code;

        public bool IsStaged
        {
            get => StatusEntry.Staged;
            set
            {
                if (StatusEntry.Staged == value)
                    return;
                if (value)
                    m_Area.RecordChanges(m_StatusVM.Status, new List<Status.StatusEntry>() { StatusEntry }, true, false, (se, code, b) => { StatusEntry.Code = code; StatusEntry.Staged = true; });
                else
                    m_Area.Revert(new List<Status.StatusEntry>() { StatusEntry }, false, false, false, (se, code) => { StatusEntry.Code = code; StatusEntry.Staged = false; });
                NotifyPropertyChanged("IsStaged");
                NotifyPropertyChanged("Code");
                // TODO: Changing the status of a file or folder may change the staged status for parent folders.
            }
        }

        public Status.StatusEntry StatusEntry { get; }

        public string Name => StatusEntry.Name;

        public string CanonicalName => StatusEntry.CanonicalName;

        public bool IsDirectory => StatusEntry.IsDirectory;

        public FlowDocument DiffPreview
        {
            get
            {
                // Not caching this one (like we do in AlterationVM) because the diff could change at any time.
                FlowDocument diffPreviewDocument = new FlowDocument();
                diffPreviewDocument.PageWidth = 10000;

                if (StatusEntry.VersionControlRecord != null && !StatusEntry.IsDirectory && StatusEntry.FilesystemEntry != null && StatusEntry.Code == Versionr.StatusCode.Modified)
                {
                    if (FileClassifier.Classify(StatusEntry.FilesystemEntry.Info) == FileEncoding.Binary)
                    {
                        Paragraph text = new Paragraph();
                        text.Inlines.Add(new Run("File: "));
                        text.Inlines.Add(new Run(StatusEntry.CanonicalName) { FontWeight = FontWeights.Bold });
                        text.Inlines.Add(new Run(" is binary "));
                        text.Inlines.Add(new Run("different") { Foreground = Brushes.Yellow });
                        text.Inlines.Add(new Run("."));
                    }
                    else
                    {
                        // Displaying local modifications
                        string tmp = DiffTool.GetTempFilename();
                        if (m_Area.ExportRecord(StatusEntry.CanonicalName, m_Area.Version, tmp))
                        {
                            Paragraph text = new Paragraph();
                            text.Inlines.Add(new Run("Displaying changes for file: "));
                            text.Inlines.Add(new Run(StatusEntry.CanonicalName) { FontWeight = FontWeights.Bold });
                            diffPreviewDocument.Blocks.Add(text);
                            try
                            {
                                RunInternalDiff(diffPreviewDocument, tmp, System.IO.Path.Combine(m_Area.Root.FullName, StatusEntry.CanonicalName));
                            }
                            finally
                            {
                                System.IO.File.Delete(tmp);
                            }
                        }
                    }
                }
                else if (StatusEntry.Code == Versionr.StatusCode.Unchanged && !StatusEntry.IsDirectory)
                {
                    Paragraph text = new Paragraph();
                    text.Inlines.Add(new Run("Object: "));
                    text.Inlines.Add(new Run(StatusEntry.CanonicalName) { FontWeight = FontWeights.Bold });
                    text.Inlines.Add(new Run(" is "));
                    text.Inlines.Add(new Run("different") { Foreground = Brushes.Green, Background = new SolidColorBrush(Color.FromRgb(0, 35, 0)) });
                    text.Inlines.Add(new Run("."));
                    diffPreviewDocument.Blocks.Add(text);
                }
                else if (StatusEntry.VersionControlRecord == null)
                {
                    Paragraph text = new Paragraph();
                    text.Inlines.Add(new Run("Object: "));
                    text.Inlines.Add(new Run(StatusEntry.CanonicalName) { FontWeight = FontWeights.Bold });
                    text.Inlines.Add(new Run(" is "));
                    text.Inlines.Add(new Run("unversioned") { Foreground = Brushes.DarkCyan });
                    text.Inlines.Add(new Run("."));
                    diffPreviewDocument.Blocks.Add(text);

                    string fileName = System.IO.Path.Combine(m_Area.Root.FullName, StatusEntry.CanonicalName);
                    if (File.Exists(fileName))
                    {
                        using (var fs = new FileInfo(fileName).OpenText())
                        {
                            Paragraph content = new Paragraph();
                            while (true)
                            {
                                if (fs.EndOfStream)
                                    break;
                                string line = fs.ReadLine().Replace("\t", "    ");
                                content.Inlines.Add(new Run(line + Environment.NewLine));
                            }
                            diffPreviewDocument.Blocks.Add(content);
                        }
                    }
                }

                return diffPreviewDocument;
            }
        }

        public void Diff()
        {
            if (StatusEntry.VersionControlRecord == null || StatusEntry.IsDirectory ||
                StatusEntry.FilesystemEntry == null || StatusEntry.Code != Versionr.StatusCode.Modified)
                return;
            if (FileClassifier.Classify(StatusEntry.FilesystemEntry.Info) == FileEncoding.Binary)
            {
                MainWindow.ShowMessage("Binary differences",
                    $"File: {StatusEntry.CanonicalName} has binary differences, but you don't really want to see them.");
            }
            else
            {
                List<StatusEntryVM> selectedItems = new List<StatusEntryVM>(VersionrPanel.SelectedItems.OfType<StatusEntryVM>());

                // Displaying local modifications
                foreach (var statusEntry in selectedItems)
                {
                    string tmp = DiffTool.GetTempFilename();
                    if (m_Area.ExportRecord(statusEntry.CanonicalName, m_Area.Version, tmp))
                    {
                        m_Area.GetTaskFactory().StartNew(() =>
                        {
                            try
                            {
                                DiffTool.Diff(tmp, statusEntry.Name + "-base",
                                    System.IO.Path.Combine(m_Area.Root.FullName, statusEntry.CanonicalName),
                                    statusEntry.Name, m_Area.Directives.ExternalDiff, false);
                            }
                            finally
                            {
                                System.IO.File.Delete(tmp);
                            }
                        });
                    } 
                }
            }
        }

        private void Log()
        {
            LogDialog.Show(m_Area.Version, m_Area, StatusEntry.CanonicalName);
        }

        public void RevertSelected()
        {
            List<StatusEntryVM> selectedItems = new List<StatusEntryVM>();
            
            MessageDialogResult result = MessageDialogResult.FirstAuxiliary;
            MainWindow.Instance.Dispatcher.Invoke(async () =>
            {
                selectedItems = VersionrPanel.SelectedItems.OfType<StatusEntryVM>().ToList();

                string plural = (selectedItems.Count > 1) ? "s" : "";
                var message = string.Format(
                    selectedItems.All(x =>
                        x.Code == StatusCode.Added || x.Code == StatusCode.Unversioned || x.Code == StatusCode.Copied ||
                        x.Code == StatusCode.Renamed)
                        ? "The selected item{0} will be permanently deleted. "
                        : "All changes to the selected item{0} will be lost. ", plural);

                result = await MainWindow.ShowMessage($"Reverting {selectedItems.Count} item{plural}",
                    message + "Do you want to continue?", MessageDialogStyle.AffirmativeAndNegative);

            }).Wait();

            if (result == MessageDialogResult.Affirmative)
            {
                m_Area.Revert(selectedItems.Select(x => x.StatusEntry).ToList(), true, false, true);
                m_StatusVM.Refresh();
            }
        }

        private class Region
        {
            public int Start1;
            public int End1;
            public int Start2;
            public int End2;
        }
        
        internal static void RunInternalDiff(FlowDocument document, string file1, string file2, bool processTabs = true)
        {
            List<string> lines1 = new List<string>();
            List<string> lines2 = new List<string>();
            
            if (!System.IO.File.Exists(file1))
            {
                document.Blocks.Add(new Paragraph(new Run(string.Format("{0} could not be opened", file1)) { Foreground = Brushes.Red }));
                return;
            }
            if (!System.IO.File.Exists(file2))
            {
                document.Blocks.Add(new Paragraph(new Run(string.Format("{0} could not be opened", file2)) { Foreground = Brushes.Red }));
                return;
            }

            using (var fs = new System.IO.FileInfo(file1).OpenText())
            {
                while (true)
                {
                    if (fs.EndOfStream)
                        break;
                    string line = fs.ReadLine();
                    if (processTabs)
                        line = line.Replace("\t", "    ");
                    lines1.Add(line);
                }
            }
            using (var fs = new System.IO.FileInfo(file2).OpenText())
            {
                while (true)
                {
                    if (fs.EndOfStream)
                        break;
                    string line = fs.ReadLine();
                    if (processTabs)
                        line = line.Replace("\t", "    ");
                    lines2.Add(line);
                }
            }

            List<Diff.commonOrDifferentThing> diff = null;
            diff = Versionr.Utilities.Diff.diff_comm2(lines1.ToArray(), lines2.ToArray(), true);
            int line0 = 0;
            int line1 = 0;
            Paragraph header = new Paragraph();
            header.Inlines.Add(new Run(string.Format("--- a/{0}" + Environment.NewLine, file1)));
            header.Inlines.Add(new Run(string.Format("+++ b/{0}" + Environment.NewLine, file2)));
            document.Blocks.Add(header);
            List<Region> regions = new List<Region>();
            Region openRegion = null;
            Region last = null;
            // cleanup step
            bool doCleanup = true;
            if (!doCleanup)
                goto Display;
            for (int i = 1; i < diff.Count - 1; i++)
            {
                if (diff[i - 1].common == null || diff[i - 1].common.Count == 0)
                    continue;
                if (diff[i + 1].common == null || diff[i + 1].common.Count == 0)
                    continue;
                int cf0 = diff[i].file1 == null ? 0 : diff[i].file1.Count;
                int cf1 = diff[i].file2 == null ? 0 : diff[i].file2.Count;
                if ((cf0 == 0) ^ (cf1 == 0)) // insertion
                {
                    List<string> target = cf0 == 0 ? diff[i].file2 : diff[i].file1;
                    List<string> receiver = diff[i - 1].common;
                    List<string> source = diff[i + 1].common;

                    int copied = 0;
                    for (int j = 0; j < target.Count && j < source.Count; j++)
                    {
                        if (target[j] == source[j])
                            copied++;
                        else
                            break;
                    }

                    if (copied > 0)
                    {
                        target.AddRange(source.Take(copied));
                        source.RemoveRange(0, copied);
                        receiver.AddRange(target.Take(copied));
                        target.RemoveRange(0, copied);
                    }
                }
            }
            for (int i = 0; i < diff.Count - 1; i++)
            {
                if (diff[i].common != null)
                    continue;
                if (diff[i + 1].common == null)
                {
                    var next = diff[i + 1];
                    diff.RemoveAt(i + 1);
                    foreach (var x in next.file1)
                    {
                        diff[i].file1.Add(x);
                    }
                    foreach (var x in next.file2)
                    {
                        diff[i].file2.Add(x);
                    }
                    i--;
                    continue;
                }
                if (diff[i + 1].common == null || diff[i + 1].common.Count == 0)
                    continue;
                bool isWhitespace = true;
                bool isShort = false;
                bool isBrace = false;
                if (diff[i + 1].common.Count * 2 <= diff[i].file1.Count &&
                    diff[i + 1].common.Count * 2 <= diff[i].file2.Count)
                    isShort = true;
                foreach (var x in diff[i + 1].common)
                {
                    if (x.Trim().Length != 0)
                    {
                        isWhitespace = false;
                        break;
                    }
                }
                if (diff[i + 1].common.Count == 1 || (diff[i + 1].common.Count == 1 && (diff[i + 1].common[0].Trim() == "{" || diff[i + 1].common[0].Trim() == "}")))
                {
                    if (i < diff.Count - 2 && (diff[i + 2].common == null || diff[i + 2].common.Count == 0))
                        isBrace = true;
                }
                if ((isWhitespace && isShort) || isShort || isBrace)
                {
                    var next = diff[i + 1];
                    if (isBrace && next.common.Count > 1)
                    {
                        // currently disabled
                        diff[i].file1.Add(next.common[0]);
                        diff[i].file2.Add(next.common[0]);
                        next.common.RemoveAt(0);
                    }
                    else
                    {
                        diff.RemoveAt(i + 1);
                        foreach (var x in next.common)
                        {
                            diff[i].file1.Add(x);
                            diff[i].file2.Add(x);
                        }
                        i--;
                    }
                }
            }
        Display:
            for (int i = 0; i < diff.Count; i++)
            {
                if (regions.Count > 0)
                    last = regions[regions.Count - 1];
                if (diff[i].common != null)
                {
                    foreach (var x in diff[i].common)
                    {
                        line0++;
                        line1++;
                    }
                }
                int cf0 = diff[i].file1 == null ? 0 : diff[i].file1.Count;
                int cf1 = diff[i].file2 == null ? 0 : diff[i].file2.Count;
                for (int j = 1; j <= cf0 || j <= cf1; j++)
                {
                    if (openRegion == null)
                    {
                        int s1 = System.Math.Max(1, line0 - 2);
                        int s2 = System.Math.Max(1, line1 - 2);
                        if (last != null && (last.End1 + 3 > s1 || last.End2 + 3 > s2))
                            openRegion = last;
                        else
                            openRegion = new Region() { Start1 = s1, Start2 = s2 };
                    }
                    openRegion.End1 = System.Math.Min(line0 + 4, lines1.Count + 1);
                    openRegion.End2 = System.Math.Min(line1 + 4, lines2.Count + 1);
                    if (j <= cf0)
                    {
                        line0++;
                    }
                    if (j <= cf1)
                    {
                        line1++;
                    }
                }
                if (openRegion != null && (openRegion.End1 < line0 && openRegion.End2 < line1))
                {
                    if (regions.Count == 0 || regions[regions.Count - 1] != openRegion)
                        regions.Add(openRegion);
                    openRegion = null;
                }
            }
            if (openRegion != null && openRegion != last)
            {
                if (regions.Count == 0 || regions[regions.Count - 1] != openRegion)
                    regions.Add(openRegion);
            }
            int activeRegion = 0;
            while (activeRegion < regions.Count)
            {
                Region reg = regions[activeRegion];
                line0 = 0;
                line1 = 0;
                Paragraph region = new Paragraph();
                region.Inlines.Add(new Run(string.Format("@@ -{0},{1} +{2},{3} @@" + Environment.NewLine, reg.Start1, reg.End1 - reg.Start1, reg.Start2, reg.End2 - reg.Start2)) { Foreground = Brushes.DarkCyan });
                for (int i = 0; i < diff.Count; i++)
                {
                    if ((line0 > reg.End1) || (line1 > reg.End2))
                    {
                        break;
                    }
                    if (diff[i].common != null)
                    {
                        foreach (var x in diff[i].common)
                        {
                            line0++;
                            line1++;
                            if ((line0 >= reg.Start1 && line0 <= reg.End1) || (line1 >= reg.Start2 && line1 <= reg.End2))
                                region.Inlines.Add(new Run(string.Format(" {0}" + Environment.NewLine, x)));
                        }
                    }
                    int cf0 = diff[i].file1 == null ? 0 : diff[i].file1.Count;
                    int cf1 = diff[i].file2 == null ? 0 : diff[i].file2.Count;
                    for (int j = 1; j <= cf0; j++)
                    {
                        line0++;
                        if (line0 >= reg.Start1 && line0 <= reg.End1)
                            region.Inlines.Add(new Run(string.Format("-{0}" + Environment.NewLine, diff[i].file1[j - 1])) { Foreground = Brushes.Red, Background = new SolidColorBrush(Color.FromRgb(35, 0, 0)) });
                    }
                    for (int j = 1; j <= cf1; j++)
                    {
                        line1++;
                        if (line1 >= reg.Start2 && line1 <= reg.End2)
                            region.Inlines.Add(new Run(string.Format("+{0}" + Environment.NewLine, diff[i].file2[j - 1])) { Foreground = Brushes.Green, Background = new SolidColorBrush(Color.FromRgb(0, 35, 0)) });
                    }
                }
                document.Blocks.Add(region);
                activeRegion++;
            }
        }

        private void OpenInExplorer()
        {
            ProcessStartInfo si = new ProcessStartInfo("explorer");
            si.Arguments = "/select,\"" + Path.Combine(m_Area.Root.FullName, CanonicalName).Replace('/', '\\') + "\"";
            Process.Start(si);
        }

        private void GeneratePatchFile()
        {
            List<StatusEntryVM> selectedItems = VersionrPanel.SelectedItems.OfType<StatusEntryVM>().ToList();
            if (!selectedItems.Any())
                return;
            var allFiles = new List<string>();
            foreach (var entry in selectedItems)
            {
                if (!entry.StatusEntry.IsFile)
                    continue;
                allFiles.Add(entry.StatusEntry.CanonicalName);
            }
            if (!allFiles.Any())
                return;

            Utilities.GeneratePatchFile(allFiles, m_Area.Root.FullName);
        }
    }
}
