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
        static private Dictionary<string, AreaVM> _areaVMDictionary = new Dictionary<string, AreaVM>();
        static private Dictionary<Guid, BranchVM> _branchVMDictionary = new Dictionary<Guid, BranchVM>();
        static private Dictionary<Guid, VersionVM> _versionVMDictionary = new Dictionary<Guid, VersionVM>();
        static private Dictionary<Guid, StatusVM> _statusVMDictionary = new Dictionary<Guid, StatusVM>();
        static private Dictionary<string, StatusEntryVM> _statusEntryVMDictionary = new Dictionary<string, StatusEntryVM>();

        static public AreaVM GetAreaVM(string path, string name, AreaInitMode areaInitMode)
        {
            AreaVM result = null;
            if (!_areaVMDictionary.TryGetValue(path, out result))
            {
                result = new AreaVM(path, name, areaInitMode);
                _areaVMDictionary.Add(path, result);
            }
            else
            {
                result.Name = name;     // update name
            }
            return result;
        }

        static public BranchVM GetBranchVM(Area area, Versionr.Objects.Branch branch)
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

        static public VersionVM GetVersionVM(Versionr.Objects.Version version, Area area)
        {
            VersionVM result = null;
            var key = version.ID;
            if (!_versionVMDictionary.TryGetValue(key, out result))
            {
                result = new VersionVM(version, area);
                _versionVMDictionary.Add(key, result);
            }
            return result;
        }

        static public StatusVM GetStatusVM(AreaVM areaVM)
        {
            StatusVM result = null;
            var key = areaVM.Area.Version.ID;
            if (!_statusVMDictionary.TryGetValue(key, out result))
            {
                result = new StatusVM(areaVM);
                _statusVMDictionary.Add(key, result);
            }
            return result;
        }

        static public StatusEntryVM GetStatusEntryVM(Status.StatusEntry statusEntry, StatusVM statusVM, Area area)
        {
            StatusEntryVM result = null;
            var key = statusEntry.Hash;
            if (!_statusEntryVMDictionary.TryGetValue(key, out result))
            {
                result = new StatusEntryVM(statusEntry, statusVM, area);
                _statusEntryVMDictionary.Add(key, result);
            }
            return result;
        }
    }
}
