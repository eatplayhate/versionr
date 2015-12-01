using Microsoft.Win32;
using System;
using System.IO;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using Versionr;
using Versionr.Objects;
using Versionr.Utilities;
using VersionrUI.Commands;
using VersionrUI.Dialogs;
using Version = Versionr.Objects.Version;

namespace VersionrUI.ViewModels
{
    public class AlterationVM
    {
        public DelegateCommand DiffWithPreviousCommand { get; private set; }
        public DelegateCommand DiffWithCurrentCommand { get; private set; }
        public DelegateCommand LogCommand { get; private set; }
        public DelegateCommand SaveVersionAsCommand { get; private set; }

        private Alteration _alteration;
        private Area _area;
        private Version _version;
        private Record _priorRecord;
        private Record _newRecord;
        private FlowDocument _diffPreviewDocument = null;

        public AlterationVM(Alteration alteration, Area area, Version version)
        {
            _alteration = alteration;
            _area = area;
            _version = version;

            if (_alteration.PriorRecord.HasValue)
                _priorRecord = _area.GetRecord(_alteration.PriorRecord.Value);
            if (_alteration.NewRecord.HasValue)
                _newRecord = _area.GetRecord(_alteration.NewRecord.Value);

            DiffWithPreviousCommand = new DelegateCommand(DiffWithPrevious, CanDiffWithPrevious);
            DiffWithCurrentCommand = new DelegateCommand(DiffWithCurrent, CanDiffWithCurrent);
            LogCommand = new DelegateCommand(Log);
            SaveVersionAsCommand = new DelegateCommand(SaveVersionAs, CanSaveVersionAs);
        }

        public Alteration Alteration{ get { return _alteration; } }

        public AlterationType AlterationType { get { return _alteration.Type; } }

        public string Name { get { return _newRecord?.CanonicalName ?? _priorRecord.CanonicalName; } }

        public bool CanDiffWithPrevious()
        {
            return _newRecord != null &&
                   _priorRecord != null &&
                   !_newRecord.IsDirectory &&
                   !_priorRecord.IsDirectory &&
                   _alteration.Type == AlterationType.Update;
        }

        private bool CanDiffWithCurrent()
        {
            return _newRecord != null &&
                   !_newRecord.IsDirectory &&
                   _alteration.Type != AlterationType.Delete &&
                   File.Exists(Path.Combine(_area.Root.FullName, _newRecord.CanonicalName));
        }

        private bool CanSaveVersionAs()
        {
            return _newRecord != null &&
                   !_newRecord.IsDirectory &&
                   _alteration.Type != AlterationType.Delete;
        }

        private void DiffWithPrevious()
        {
            if ((_priorRecord.Attributes & Attributes.Binary) == Attributes.Binary ||
                (_newRecord.Attributes & Attributes.Binary) == Attributes.Binary)
            {
                MessageBox.Show(string.Format("Not showing binary differences."));
            }
            else
            {
                // Displaying modifications
                string tmpPrior = DiffTool.GetTempFilename();
                string tmpNew = DiffTool.GetTempFilename();
                _area.GetMissingRecords(new Record[] { _priorRecord, _newRecord });
                _area.RestoreRecord(_priorRecord, DateTime.UtcNow, tmpPrior);
                _area.RestoreRecord(_newRecord, DateTime.UtcNow, tmpNew);
                LimitedTaskDispatcher.Factory.StartNew(() =>
                {
                    try
                    {
                        DiffTool.Diff(tmpPrior, String.Format("{0} ({1})", _priorRecord.Name, _alteration.PriorRecord),
                                      tmpNew, String.Format("{0} ({1})", _newRecord.Name, _alteration.NewRecord),
                                      _area.Directives.ExternalDiff, false);
                    }
                    finally
                    {
                        File.Delete(tmpPrior);
                        File.Delete(tmpNew);
                    }
                });
            }
        }

        private void DiffWithCurrent()
        {
            if ((_newRecord.Attributes & Attributes.Binary) == Attributes.Binary)
            {
                MessageBox.Show(string.Format("File: {0} is binary different.", _newRecord.CanonicalName));
            }
            else
            {
                string tmpNew = DiffTool.GetTempFilename();
                _area.GetMissingRecords(new Record[] { _newRecord });
                _area.RestoreRecord(_newRecord, DateTime.UtcNow, tmpNew);
                LimitedTaskDispatcher.Factory.StartNew(() =>
                {
                    try
                    {
                        DiffTool.Diff(tmpNew, String.Format("{0} ({1})", _newRecord.Name, _alteration.NewRecord),
                                      Path.Combine(_area.Root.FullName, _newRecord.CanonicalName), String.Format("{0} (working copy)", _newRecord.Name),
                                      _area.Directives.ExternalDiff, false);
                    }
                    finally
                    {
                        File.Delete(tmpNew);
                    }
                });
            }
        }

        private void SaveVersionAs()
        {
            if ((_newRecord.Attributes & Attributes.Binary) == Attributes.Binary)
            {
                MessageBox.Show(string.Format("File: {0} is binary different.", _newRecord.CanonicalName));
            }
            else
            {
                SaveFileDialog dialog = new SaveFileDialog();
                dialog.FileName = _newRecord.Name;
                dialog.CheckPathExists = true;
                if (dialog.ShowDialog() == true)
                {
                    _area.GetMissingRecords(new Record[] { _newRecord });
                    _area.RestoreRecord(_newRecord, DateTime.UtcNow, dialog.FileName);
                }
            }
        }

        private void Log()
        {
            new LogDialog(_version, _area, _newRecord.CanonicalName).ShowDialog();
        }

        public FlowDocument DiffPreview
        {
            get
            {
                if (_diffPreviewDocument == null)
                {
                    _diffPreviewDocument = new FlowDocument();

                    if (CanDiffWithPrevious())
                    {
                        if ((_priorRecord.Attributes & Attributes.Binary) == Attributes.Binary ||
                            (_newRecord.Attributes & Attributes.Binary) == Attributes.Binary)
                        {
                            Paragraph text = new Paragraph();
                            text.Inlines.Add(new Run("File: "));
                            text.Inlines.Add(new Run(Name) { FontWeight = FontWeights.Bold });
                            text.Inlines.Add(new Run(" is binary "));
                            text.Inlines.Add(new Run("different") { Foreground = Brushes.Yellow });
                            text.Inlines.Add(new Run("."));
                        }
                        else
                        {
                            // Displaying modifications
                            string tmpPrior = DiffTool.GetTempFilename();
                            string tmpNew = DiffTool.GetTempFilename();
                            _area.GetMissingRecords(new Record[] { _priorRecord, _newRecord });
                            _area.RestoreRecord(_priorRecord, DateTime.UtcNow, tmpPrior);
                            _area.RestoreRecord(_newRecord, DateTime.UtcNow, tmpNew);

                            Paragraph text = new Paragraph();
                            text.Inlines.Add(new Run("Displaying changes for file: "));
                            text.Inlines.Add(new Run(Name) { FontWeight = FontWeights.Bold });
                            _diffPreviewDocument.Blocks.Add(text);
                            try
                            {
                                StatusEntryVM.RunInternalDiff(_diffPreviewDocument, tmpPrior, tmpNew);
                            }
                            finally
                            {
                                File.Delete(tmpPrior);
                                File.Delete(tmpNew);
                            }
                        }
                    }
                    else if (_priorRecord == null)
                    {
                        Paragraph text = new Paragraph();
                        text.Inlines.Add(new Run("Object: "));
                        text.Inlines.Add(new Run(Name) { FontWeight = FontWeights.Bold });
                        text.Inlines.Add(new Run(" was previously "));
                        text.Inlines.Add(new Run("unversioned") { Foreground = Brushes.DarkCyan });
                        text.Inlines.Add(new Run("."));
                        _diffPreviewDocument.Blocks.Add(text);
                    }
                }

                return _diffPreviewDocument;
            }
        }
    }
}
