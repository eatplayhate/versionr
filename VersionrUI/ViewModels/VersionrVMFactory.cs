using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace VersionrUI.ViewModels
{
    class VersionrVMFactory
    {
        // Weary traveler, find here dictionaries of something unique to each versionr type
        // and the view model associated with that object.
        static private Dictionary<DirectoryInfo, AreaVM> _areaVMDictionary = new Dictionary<DirectoryInfo, AreaVM>();
        static private Dictionary<Guid, BranchVM> _branchVMDictionary = new Dictionary<Guid, BranchVM>();

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
            if (!_branchVMDictionary.TryGetValue(branch.ID, out result))
            {
                result = new BranchVM(area, branch);
                _branchVMDictionary.Add(branch.ID, result);
            }
            return result;
        }
    }
}
