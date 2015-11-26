using System;
using System.Windows;
using Versionr;
using Versionr.Objects;
using Versionr.Utilities;
using VersionrUI.Commands;

namespace VersionrUI.ViewModels
{
    public class AlterationVM
    {
        public DelegateCommand DiffCommand { get; private set; }

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

            DiffCommand = new DelegateCommand(() => Diff(),
                                              () => _alteration.PriorRecord.HasValue && _alteration.NewRecord.HasValue);
        }

        public AlterationType AlterationType { get { return _alteration.Type; } }

        public string Name { get { return _newRecord?.CanonicalName ?? _priorRecord.CanonicalName; } }

        public void Diff()
        {
            if (_newRecord != null && _priorRecord != null &&
                !_newRecord.IsDirectory && !_priorRecord.IsDirectory &&
                _alteration.Type == AlterationType.Update)
            {
                if ((_priorRecord.Attributes & Attributes.Binary) == Attributes.Binary ||
                    (_newRecord.Attributes & Attributes.Binary) == Attributes.Binary)
                {
                    MessageBox.Show(string.Format("Not showing binary differences."));
                }
                else
                {
                    // Displaying local modifications
                    string tmpPrior = DiffTool.GetTempFilename();
                    string tmpNew = DiffTool.GetTempFilename();
                    _area.GetMissingRecords(new Record[] { _priorRecord, _newRecord });
                    _area.RestoreRecord(_priorRecord, DateTime.UtcNow, tmpPrior);
                    _area.RestoreRecord(_newRecord, DateTime.UtcNow, tmpNew);
                    LimitedTaskDispatcher.Factory.StartNew(() =>
                    {
                        try
                        {
                            DiffTool.Diff(tmpPrior, _priorRecord.Name, 
                                          tmpNew, _newRecord.Name,
                                          _area.Directives.ExternalDiff, false);
                        }
                        finally
                        {
                            System.IO.File.Delete(tmpPrior);
                            System.IO.File.Delete(tmpNew);
                        }
                    });
                }
            }
        }
    }
}
