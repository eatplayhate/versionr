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
        [Option("replicate", Required = false, HelpText = "Marks the admin command as replicatable (if possible).")]
        public bool Replicate { get; set; }
        [Option("check", Required = false, HelpText = "Runs a general purpose DB consistency check and repair function.")]
        public bool Check { get; set; }
        [Option("vacuum", Required = false, HelpText = "Runs the SQLite VACUUM instruction on the master DB.")]
        public bool Vacuum { get; set; }
        [Option("echo", Required = false, HelpText = "Echos additional information about what is being run.")]
        public bool Echo { get; set; }
    }
    class Admin : BaseWorkspaceCommand
    {
        protected override bool RunInternal(object options)
        {
            AdminVerbOptions localOptions = options as AdminVerbOptions;
            Printer.EnableDiagnostics = localOptions.Verbose;
            if (localOptions.Check)
                Workspace.RunConsistencyCheck();
            if (localOptions.Vacuum)
                Workspace.RunVacuum();
            if (!string.IsNullOrEmpty(localOptions.SQL))
            {
                System.IO.FileInfo fi = new System.IO.FileInfo(localOptions.SQL);
                if (!fi.Exists)
                {
                    Printer.PrintMessage("#e#Error:## Can't load JSON-wrapped SQL file at \"{0}\"", localOptions.SQL);
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
                        Printer.PrintMessage("#e#Error:## Couldn't load JSON data at \"{0}\"", localOptions.SQL);
                        return false;
                    }
                    JArray statements = obj.GetValue("SQLStatements") as JArray;
                    Printer.PrintMessage("Loaded {1} SQL statements from \"{0}\"", localOptions.SQL, statements.Count);
                    if (Printer.Prompt("Apply?"))
                    {
                        try
                        {
                            int totalCount = 0;
                            Workspace.BeginDatabaseTransaction();
                            foreach (var x in statements)
                            {
                                if (localOptions.Echo)
                                    Printer.PrintMessage(Printer.Escape(x.ToString()));
                                try
                                {
                                    int results = Workspace.ExecuteDatabaseSQL(x.ToString());
                                    if (localOptions.Echo)
                                        Printer.PrintMessage("#s#+ {0} rows modified", results);
                                    totalCount += results;
                                }
                                catch (Exception e)
                                {
                                    if (!localOptions.Echo)
                                        Printer.PrintMessage("#e#{0}", Printer.Escape(x.ToString()));
                                    Printer.PrintMessage("Error in SQL statement: {0}", e.ToString());
                                    if (Printer.Prompt("Abort?"))
                                        throw;
                                }
                            }
                            Printer.PrintMessage("SQL statements have modified {0} rows in the master database.", totalCount);
                            if (!Printer.Prompt("Write changes to DB?"))
                                throw new Exception("Aborted");
                            Workspace.CommitDatabaseTransaction();
                            Printer.PrintMessage("Done.");
                        }
                        catch (Exception e)
                        {
                            Workspace.RollbackDatabaseTransaction();
                            Printer.PrintMessage("#e#Error:## Couldn't apply SQL - exception {0}", e);
                            return false;
                        }
                    }
                }
            }
            return true;
        }
    }
}
