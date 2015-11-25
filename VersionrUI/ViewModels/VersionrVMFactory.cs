using System;
using System.Collections.Generic;
using System.IO;
using Versionr;

namespace VersionrUI.ViewModels
{
    class VersionrVMFactory
    {
        // Weary traveler, find here dictionaries of something unique to each versionr type
        // and the view model associated with that object.
        static private Dictionary<DirectoryInfo, AreaVM> _areaVMDictionary = new Dictionary<DirectoryInfo, AreaVM>();
        static private Dictionary<Guid, BranchVM> _branchVMDictionary = new Dictionary<Guid, BranchVM>();
        static private Dictionary<Guid, VersionVM> _versionVMDictionary = new Dictionary<Guid, VersionVM>();
        static private Dictionary<Guid, StatusVM> _statusVMDictionary = new Dictionary<Guid, StatusVM>();
        static private Dictionary<string, StatusEntryVM> _statusEntryVMDictionary = new Dictionary<string, StatusEntryVM>();

        static public AreaVM GetAreaVM(Versionr.Area area, string name)
        {
            AreaVM result = null;
            if (!_areaVMDictionary.TryGetValue(area.AdministrationFolder, out result))
            {
                result = new AreaVM(area, name);
                _areaVMDictionary.Add(area.AdministrationFolder, result);
            }
            else
            {
                result.Name = name;     // update name
            }
            return result;
        }

        static public BranchVM GetBranchVM(Versionr.Area area, Versionr.Objects.Branch branch)
        {
            BranchVM result = null;
            var key = branch.ID;
            if (!_branchVMDictionary.TryGetValue(key, out result))
            {
                result = new BranchVM(area, branch);
                _branchVMDictionary.Add(key, result);
            }
            return result;
        }

        static public VersionVM GetVersionVM(Versionr.Objects.Version version)
        {
            VersionVM result = null;
            var key = version.ID;
            if (!_versionVMDictionary.TryGetValue(key, out result))
            {
                result = new VersionVM(version);
                _versionVMDictionary.Add(key, result);
            }
            return result;
        }

        static public StatusVM GetStatusVM(Versionr.Status status, Area area)
        {
            StatusVM result = null;
            var key = status.CurrentVersion.ID;
            if (!_statusVMDictionary.TryGetValue(key, out result))
            {
                result = new StatusVM(status, area);
                _statusVMDictionary.Add(key, result);
            }
            return result;
        }

        static public StatusEntryVM GetStatusEntryVM(Versionr.Status.StatusEntry statusEntry, Area area)
        {
            StatusEntryVM result = null;
            var key = statusEntry.Hash;
            if (!_statusEntryVMDictionary.TryGetValue(key, out result))
            {
                result = new StatusEntryVM(statusEntry, area);
                _statusEntryVMDictionary.Add(key, result);
            }
            return result;
        }
    }
}
