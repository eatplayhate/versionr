using System;
using System.IO;
using System.Windows;
using Versionr;
using Versionr.Objects;
using Versionr.Utilities;
using VersionrUI.Commands;

namespace VersionrUI.ViewModels
{
    public class AlterationVM
    {
        public DelegateCommand DiffWithPreviousCommand { get; private set; }
        public DelegateCommand DiffWithCurrentCommand { get; private set; }

        private Alteration _alteration;
        private Area _area;
        private Record _priorRecord;
        private Record _newRecord;

        public AlterationVM(Alteration alteration, Area area)
        {
            _alteration = alteration;
            _area = area;
            if (_alteration.PriorRecord.HasValue)
                _priorRecord = _area.GetRecord(_alteration.PriorRecord.Value);
            if (_alteration.NewRecord.HasValue)
                _newRecord = _area.GetRecord(_alteration.NewRecord.Value);

            DiffWithPreviousCommand = new DelegateCommand(DiffWithPrevious, CanDiffWithPrevious);
            DiffWithCurrentCommand = new DelegateCommand(DiffWithCurrent, CanDiffWithCurrent);
        }

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

        public bool CanDiffWithCurrent()
        {
            return _newRecord != null &&
                   !_newRecord.IsDirectory &&
                   _alteration.Type != AlterationType.Delete &&
                   File.Exists(Path.Combine(_area.Root.FullName, _newRecord.CanonicalName));
        }

        public void DiffWithPrevious()
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

        public void DiffWithCurrent()
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
    }
}
