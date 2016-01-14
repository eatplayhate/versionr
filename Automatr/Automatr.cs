using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Diagnostics;

using Versionr;
using Versionr.Objects;
using Versionr.LocalState;
using Versionr.Network;

namespace Automatr
{
    public class Automatr
    {
        public enum BranchStatus
        {
            Unknown, Current, Behind, Ahead
        }

        public AutomatrConfig Config { get; private set; }
        private Area m_Area;
        private Client m_Client;

        public Automatr(AutomatrConfig config)
        {
            Config = config;
        }

        public void Run()
        {
            m_Area = Area.Load(new DirectoryInfo(Config.Path));
            m_Client = new Client(m_Area);

            if (!Connect())
            {
                // Error
                Environment.Exit(1);
            }
            BranchStatus status = GetStatus();
            if (status == BranchStatus.Behind)
                RunTasks();
        }

        private bool Connect()
        {
            RemoteConfig remote = m_Area.GetRemote("default");
            return m_Client.Connect(remote.Host, remote.Port, remote.Module);
        }

        public BranchStatus GetStatus()
        {
            var branches = m_Client.ListBranches();
            if (branches == null)
                return BranchStatus.Unknown;


            Branch remoteBranch = branches.Item1.FirstOrDefault(x => x.Name == Config.BranchName);
            Branch localBranch = m_Client.Workspace.GetBranch(remoteBranch.ID);

            KeyValuePair<Guid, Guid> id = branches.Item2.FirstOrDefault(x => x.Key == remoteBranch.ID);
            if (id.Key != id.Value)
            {
                bool present = m_Client.Workspace.GetVersion(id.Value) != null;
                if (present && localBranch != null)
                {
                    var heads = m_Client.Workspace.GetBranchHeads(localBranch);
                    if (heads.Count == 1 && heads[0].Version != id.Value)
                        return BranchStatus.Ahead;
                }
                return BranchStatus.Behind;
            }
            return BranchStatus.Current;
        }

        private void RunTasks()
        {
            foreach (AutomatrTask task in Config.Tasks)
            {
                var processInfo = new ProcessStartInfo("cmd.exe", "/c " + task.Command);
                processInfo.CreateNoWindow = true;
                processInfo.UseShellExecute = false;
                processInfo.RedirectStandardError = true;
                processInfo.RedirectStandardOutput = true;

                var process = Process.Start(processInfo);

                process.OutputDataReceived += (object sender, DataReceivedEventArgs e) =>
                    Console.WriteLine("output>>" + e.Data);
                process.BeginOutputReadLine();

                process.ErrorDataReceived += (object sender, DataReceivedEventArgs e) =>
                    Console.WriteLine("error>>" + e.Data);
                process.BeginErrorReadLine();

                process.WaitForExit();

                Console.WriteLine("ExitCode: {0}", process.ExitCode);
                process.Close();
            }
        }

    }
}
