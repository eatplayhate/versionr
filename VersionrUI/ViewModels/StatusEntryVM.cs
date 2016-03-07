using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using Versionr;
using Versionr.Utilities;
using VersionrUI.Commands;
using VersionrUI.Dialogs;

namespace VersionrUI.ViewModels
{
    public class StatusEntryVM : NotifyPropertyChangedBase
    {
        public DelegateCommand DiffCommand { get; private set; }
        public DelegateCommand LogCommand { get; private set; }
        public DelegateCommand RevertCommand { get; private set; }

        private Status.StatusEntry _statusEntry;
        private StatusVM _statusVM;
        private Area _area;

        public StatusEntryVM(Status.StatusEntry statusEntry, StatusVM statusVM, Area area)
        {
            _statusEntry = statusEntry;
            _statusVM = statusVM;
            _area = area;

            DiffCommand = new DelegateCommand(Diff);
            LogCommand = new DelegateCommand(Log);
            RevertCommand = new DelegateCommand(RevertSelected);
        }

        public Versionr.StatusCode Code
        {
            get { return _statusEntry.Code; }
        }

        public bool IsStaged
        {
            get { return _statusEntry.Staged; }
            set
            {
                if (_statusEntry.Staged != value)
                {
                    if (value)
                        _area.RecordChanges(_statusVM.Status, new List<Status.StatusEntry>() { _statusEntry }, true, false, (se, code, b) => { _statusEntry.Code = code; _statusEntry.Staged = true; });
                    else
                        _area.Revert(new List<Status.StatusEntry>() { _statusEntry }, false, false, false, (se, code) => { _statusEntry.Code = code; _statusEntry.Staged = false; });
                    NotifyPropertyChanged("IsStaged");
                    NotifyPropertyChanged("Code");
                }
            }
        }

        public Status.StatusEntry StatusEntry
        {
            get { return _statusEntry; }
        }

        public string Name
        {
            get { return _statusEntry.Name; }
        }

        public string CanonicalName
        {
            get { return _statusEntry.CanonicalName; }
        }

        public bool IsDirectory
        {
            get { return _statusEntry.IsDirectory; }
        }

        public FlowDocument DiffPreview
        {
            get
            {
                FlowDocument diffPreviewDocument = new FlowDocument();
                diffPreviewDocument.PageWidth = 10000;

                if (_statusEntry.VersionControlRecord != null && !_statusEntry.IsDirectory && _statusEntry.FilesystemEntry != null && _statusEntry.Code == Versionr.StatusCode.Modified)
                {
                    if (FileClassifier.Classify(_statusEntry.FilesystemEntry.Info) == FileEncoding.Binary)
                    {
                        Paragraph text = new Paragraph();
                        text.Inlines.Add(new Run("File: "));
                        text.Inlines.Add(new Run(_statusEntry.CanonicalName) { FontWeight = FontWeights.Bold });
                        text.Inlines.Add(new Run(" is binary "));
                        text.Inlines.Add(new Run("different") { Foreground = Brushes.Yellow });
                        text.Inlines.Add(new Run("."));
                    }
                    else
                    {
                        // Displaying local modifications
                        string tmp = DiffTool.GetTempFilename();
                        if (_area.ExportRecord(_statusEntry.CanonicalName, _area.Version, tmp))
                        {
                            Paragraph text = new Paragraph();
                            text.Inlines.Add(new Run("Displaying changes for file: "));
                            text.Inlines.Add(new Run(_statusEntry.CanonicalName) { FontWeight = FontWeights.Bold });
                            diffPreviewDocument.Blocks.Add(text);
                            try
                            {
                                RunInternalDiff(diffPreviewDocument, tmp, System.IO.Path.Combine(_area.Root.FullName, _statusEntry.CanonicalName));
                            }
                            finally
                            {
                                System.IO.File.Delete(tmp);
                            }
                        }
                    }
                }
                else if (_statusEntry.Code == Versionr.StatusCode.Unchanged && !_statusEntry.IsDirectory)
                {
                    Paragraph text = new Paragraph();
                    text.Inlines.Add(new Run("Object: "));
                    text.Inlines.Add(new Run(_statusEntry.CanonicalName) { FontWeight = FontWeights.Bold });
                    text.Inlines.Add(new Run(" is "));
                    text.Inlines.Add(new Run("different") { Foreground = Brushes.Green, Background = new SolidColorBrush(Color.FromRgb(220, 255, 220)) });
                    text.Inlines.Add(new Run("."));
                    diffPreviewDocument.Blocks.Add(text);
                }
                else if (_statusEntry.VersionControlRecord == null)
                {
                    Paragraph text = new Paragraph();
                    text.Inlines.Add(new Run("Object: "));
                    text.Inlines.Add(new Run(_statusEntry.CanonicalName) { FontWeight = FontWeights.Bold });
                    text.Inlines.Add(new Run(" is "));
                    text.Inlines.Add(new Run("unversioned") { Foreground = Brushes.DarkCyan });
                    text.Inlines.Add(new Run("."));
                    diffPreviewDocument.Blocks.Add(text);

                    string fileName = System.IO.Path.Combine(_area.Root.FullName, _statusEntry.CanonicalName);
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
            if (_statusEntry.VersionControlRecord != null && !_statusEntry.IsDirectory && _statusEntry.FilesystemEntry != null && _statusEntry.Code == Versionr.StatusCode.Modified)
            {
                if (FileClassifier.Classify(_statusEntry.FilesystemEntry.Info) == FileEncoding.Binary)
                {
                    MessageBox.Show(string.Format("File: {0} is binary different.", _statusEntry.CanonicalName));
                }
                else
                {
                    // Displaying local modifications
                    string tmp = DiffTool.GetTempFilename();
                    if (_area.ExportRecord(_statusEntry.CanonicalName, _area.Version, tmp))
                    {
                        LimitedTaskDispatcher.Factory.StartNew(() =>
                        {
                            try
                            {
                                DiffTool.Diff(tmp, _statusEntry.Name + "-base", System.IO.Path.Combine(_area.Root.FullName, _statusEntry.CanonicalName), _statusEntry.Name, _area.Directives.ExternalDiff, false);
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
            new LogDialog(_area.Version, _area, _statusEntry.CanonicalName).ShowDialog();
        }

        public void RevertSelected()
        {
            if (VersionrUI.Controls.VersionrPanel.SelectedItems.OfType<StatusEntryVM>().Any(x => x.Code == StatusCode.Added || x.Code == StatusCode.Unversioned))
            {
                MessageBoxResult result = MessageBox.Show("Do you want to delete selected files from disk?", "Delete unversioned file?", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
                if (result == MessageBoxResult.Cancel)
                    return;
                else
                {
                    bool deleteNewFile = (result == MessageBoxResult.Yes);
                    _area.Revert(VersionrUI.Controls.VersionrPanel.SelectedItems.OfType<StatusEntryVM>().Select(x => x._statusEntry).ToList(), true, false, deleteNewFile);
                }
            }
            else
            {
                _area.Revert(VersionrUI.Controls.VersionrPanel.SelectedItems.OfType<StatusEntryVM>().Select(x => x._statusEntry).ToList(), true, false, false);
            }
            _statusVM.Refresh();
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
                            region.Inlines.Add(new Run(string.Format("-{0}" + Environment.NewLine, diff[i].file1[j - 1])) { Foreground = Brushes.Red, Background = new SolidColorBrush(Color.FromRgb(255, 220, 220)) });
                    }
                    for (int j = 1; j <= cf1; j++)
                    {
                        line1++;
                        if (line1 >= reg.Start2 && line1 <= reg.End2)
                            region.Inlines.Add(new Run(string.Format("+{0}" + Environment.NewLine, diff[i].file2[j - 1])) { Foreground = Brushes.Green, Background = new SolidColorBrush(Color.FromRgb(220, 255, 220)) });
                    }
                }
                document.Blocks.Add(region);
                activeRegion++;
            }
        }
    }
}
