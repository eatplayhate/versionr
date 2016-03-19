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
            AutomatrLog.Log("Loaded area " + Config.Path + "... ", false);
            m_Area = Area.Load(new DirectoryInfo(Config.Path));
            AutomatrLog.Log("Done");
            AutomatrLog.Log("Creating Client... ", false);
            m_Client = new Client(m_Area);
            AutomatrLog.Log("Done.");

            if (!Connect())
            {
                AutomatrLog.Log("Connection Failed!", AutomatrLog.LogLevel.Error);
                Environment.Exit(1);
            }
            AutomatrLog.Log("Connection successful.");
            BranchStatus status = GetStatus();
            AutomatrLog.Log("Branch status: " + status);
            if (status == BranchStatus.Behind || Program.Options.Force)
                RunTasks();
        }

        private bool Connect()
        {
            RemoteConfig remote = m_Area.GetRemote("default");
            AutomatrLog.Log("Attempting to connect to remote:");
            AutomatrLog.Log("\tHost = " + remote.Host);
            AutomatrLog.Log("\tPort = " + remote.Port);
            AutomatrLog.Log("\tModule = " + remote.Module);
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
            bool present = m_Client.Workspace.GetVersion(id.Value) != null;
            if (!present)
                return BranchStatus.Behind;
                
            if (present && localBranch != null)
            {
                var heads = m_Client.Workspace.GetBranchHeads(localBranch);
                if (heads.Count == 1 && heads[0].Version != id.Value)
                    return BranchStatus.Ahead;
            }
            return BranchStatus.Current;
        }

        private void RunTasks()
        {
            AutomatrLog.Log("Running tasks... " + (Program.Options.Force ? "(forced)" : ""));
            foreach (AutomatrTask task in Config.Tasks)
            {
                AutomatrLog.Log("Running Task: " + task.Command);
                var processInfo = new ProcessStartInfo("cmd.exe", "/c " + task.Command);
                processInfo.CreateNoWindow = true;
                processInfo.UseShellExecute = false;
                processInfo.RedirectStandardError = true;
                processInfo.RedirectStandardOutput = true;

                var process = Process.Start(processInfo);

                process.OutputDataReceived += (object sender, DataReceivedEventArgs e) => AutomatrLog.Log(e.Data, AutomatrLog.LogLevel.Info);
                process.BeginOutputReadLine();

                process.ErrorDataReceived += (object sender, DataReceivedEventArgs e) => AutomatrLog.Log(e.Data, AutomatrLog.LogLevel.Error);
                process.BeginErrorReadLine();

                process.WaitForExit();

                Console.WriteLine("Exit Code: {0}", process.ExitCode);
                process.Close();
            }
        }

    }
}
