using CommandLine;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Versionr.Network;

namespace Versionr.Commands
{
    class AdminVerbOptions : VerbOptionBase
    {
        public override BaseCommand GetCommand()
        {
            return new Admin();
        }

        public override string[] Description
        {
            get
            {
                return new string[]
                {
                    "Runs a special administration command. For advanced users only."
                };
            }
        }

        public override string Verb
        {
            get
            {
                return "admin";
            }
        }
        [Option("sql-local", Required = false, HelpText = "Runs an JSON-wrapped SQL command on the local cache DB")]
        public string SQLLocal { get; set; }
        [Option("sql", Required = false, HelpText = "Runs an JSON-wrapped SQL command on the main DB")]
        public string SQL { get; set; }
        [Option("blob", Required = false, HelpText = "Extracts a record blob from the database")]
        public string Blob { get; set; }
        [Option("replicate", Required = false, HelpText = "Marks the admin command as replicatable (if possible).")]
        public bool Replicate { get; set; }
        [Option("check", Required = false, HelpText = "Runs a general purpose DB consistency check and repair function.")]
        public bool Check { get; set; }
        [Option("vacuum", Required = false, HelpText = "Runs the SQLite VACUUM instruction on the master DB.")]
        public bool Vacuum { get; set; }
        [Option("rawblob", Required = false, HelpText = "When outputting blobs, do not decompress the blob data.")]
        public bool RawBlob { get; set; }
        [Option("echo", Required = false, HelpText = "Echos additional information about what is being run.")]
        public bool Echo { get; set; }
    }
    class Admin : BaseWorkspaceCommand
    {
        protected override bool RunInternal(object options)
        {
            AdminVerbOptions localOptions = options as AdminVerbOptions;
            Printer.EnableDiagnostics = localOptions.Verbose;
            if (!string.IsNullOrEmpty(localOptions.Blob))
            {
                if (localOptions.RawBlob)
                {
                    List<string> dataIds;
                    Workspace.ObjectStore.GetAvailableStreams(localOptions.Blob, out dataIds);
                    if (dataIds != null && dataIds.Count > 0)
                    {
                        for (int i = 0; i < dataIds.Count; i++)
                        {
                            using (var fout = System.IO.File.Create(string.Format("RecordData-{0}.out", i)))
                            {
                                Workspace.ObjectStore.ExportDataBlob(dataIds[i], localOptions.RawBlob, fout);
                            }
                            Printer.PrintMessage("DataID: #b#{0}", dataIds[i]);
                            Printer.PrintMessage(" - Wrote record to: RecordData-{0}.out", i);
                        }
                    }
                    else
                        Printer.PrintError("#w#Warning:## Couldn't find data record.");
                }
                else
                {
                    using (var fout = System.IO.File.Create(string.Format("RecordData-{0}.out", 0)))
                    {
                        Printer.PrintMessage("DataID: #b#{0}", localOptions.Blob);
                        Workspace.ObjectStore.ExportDataStream(localOptions.Blob, fout);
                        Printer.PrintMessage(" - Wrote record to: RecordData-{0}.out", 0);
                    }
                }
            }
            if (localOptions.Check)
            {
                if (localOptions.Replicate)
                    Printer.PrintMessage("#w#Warning:## Database commands are not replicatable.");
                Workspace.RunConsistencyCheck();
            }
            if (localOptions.Vacuum)
            {
                if (localOptions.Replicate)
                    Printer.PrintMessage("#w#Warning:## Database commands are not replicatable.");
                Workspace.RunVacuum();
            }
            if (!string.IsNullOrEmpty(localOptions.SQL))
            {
                if (localOptions.Replicate)
                    Printer.PrintMessage("#w#Warning:## SQL commands are not replicatable.");
                return RunSQL(true, localOptions.Echo, localOptions.SQL);
            }
            if (!string.IsNullOrEmpty(localOptions.SQLLocal))
            {
                if (localOptions.Replicate)
                    Printer.PrintMessage("#w#Warning:## SQL commands are not replicatable.");
                return RunSQL(false, localOptions.Echo, localOptions.SQLLocal);
            }
            return true;
        }

        private bool RunSQL(bool mainDB, bool echo, string SQL)
        {
            System.IO.FileInfo fi = new System.IO.FileInfo(SQL);
            if (!fi.Exists)
            {
                Printer.PrintMessage("#e#Error:## Can't load JSON-wrapped SQL file at \"{0}\"", SQL);
                return false;
            }
            using (var fs = fi.OpenRead())
            using (var sr = new System.IO.StreamReader(fs))
            using (var jr = new JsonTextReader(sr))
            {
                JsonSerializer js = new JsonSerializer();
                JObject obj;
                try
                {
                    obj = js.Deserialize<JObject>(jr);
                }
                catch
                {
                    Printer.PrintMessage("#e#Error:## Couldn't load JSON data at \"{0}\"", SQL);
                    return false;
                }
                JArray statements = obj.GetValue("SQLStatements") as JArray;
                Printer.PrintMessage("Loaded {1} SQL statements from \"{0}\"", SQL, statements.Count);
                if (Printer.Prompt("Apply SQL statments to " + (mainDB ? "master" : "client") + " database?"))
                {
                    try
                    {
                        int totalCount = 0;
                        if (mainDB)
                            Workspace.BeginDatabaseTransaction();
                        else
                            Workspace.BeginLocalDBTransaction();
                        int runcount = 0;
                        System.Diagnostics.Stopwatch sw = new System.Diagnostics.Stopwatch();
                        sw.Start();
                        foreach (var x in statements)
                        {
                            if (echo)
                                Printer.PrintMessage(Printer.Escape(x.ToString()));
                            try
                            {
                                int results = 0;
                                if (mainDB)
                                    results = Workspace.ExecuteDatabaseSQL(x.ToString());
                                else
                                    results = Workspace.ExecuteLocalDBSQL(x.ToString());
                                if (echo)
                                    Printer.PrintMessage("#s#+ {0} rows modified", results);
                                totalCount += results;
                            }
                            catch (Exception e)
                            {
                                if (!echo)
                                    Printer.PrintMessage("#e#{0}", Printer.Escape(x.ToString()));
                                Printer.PrintMessage("Error in SQL statement: {0}", e.ToString());
                                if (Printer.Prompt("Abort?"))
                                    throw;
                            }
                            runcount++;
                            if (sw.ElapsedMilliseconds > 5000)
                            {
                                sw.Restart();
                                Printer.PrintMessage("Executed {0} of {1} statements...", runcount, statements.Count);
                            }
                        }
                        Printer.PrintMessage("SQL statements have modified {0} rows in the target database.", totalCount);
                        if (!Printer.Prompt("Write changes to DB?"))
                            throw new Exception("Aborted");
                        if (mainDB)
                            Workspace.CommitDatabaseTransaction();
                        else
                            Workspace.CommitLocalDBTransaction();
                        Printer.PrintMessage("Done.");
                        return true;
                    }
                    catch (Exception e)
                    {
                        if (mainDB)
                            Workspace.RollbackDatabaseTransaction();
                        else
                            Workspace.RollbackLocalDBTransaction();
                        Printer.PrintMessage("#e#Error:## Couldn't apply SQL - exception {0}", e);
                        return false;
                    }
                }
            }
            return false;
        }
    }
}
