using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Versionr.Utilities
{
    static class SQLiteExtensions
    {
        public static bool UpdateSafe(this SQLite.SQLiteConnection conn, object obj)
        {
            while (true)
            {
                try
                {
                    conn.Update(obj);
                    return true;
                }
                catch (SQLite.SQLiteException ex)
                {
                    if (ex.Result == SQLite.SQLite3.Result.Constraint)
                        return false;
                    if (ex.Result != SQLite.SQLite3.Result.Busy)
                        throw ex;
                    System.Threading.Thread.Sleep(10);
                }
            }
        }
        public static bool DeleteSafe(this SQLite.SQLiteConnection conn, object obj)
        {
            while (true)
            {
                try
                {
                    conn.Delete(obj);
                    return true;
                }
                catch (SQLite.SQLiteException ex)
                {
                    if (ex.Result == SQLite.SQLite3.Result.Constraint)
                        return false;
                    if (ex.Result != SQLite.SQLite3.Result.Busy)
                        throw ex;
                    System.Threading.Thread.Sleep(10);
                }
            }
        }
        public static bool InsertOrReplaceSafe(this SQLite.SQLiteConnection conn, object obj)
        {
            while (true)
            {
                try
                {
                    conn.InsertOrReplace(obj);
                    return true;
                }
                catch (SQLite.SQLiteException ex)
                {
                    if (ex.Result == SQLite.SQLite3.Result.Constraint)
                        return false;
                    if (ex.Result != SQLite.SQLite3.Result.Busy)
                        throw ex;
                    System.Threading.Thread.Sleep(10);
                }
            }
        }
        public static bool InsertSafe(this SQLite.SQLiteConnection conn, object obj)
        {
            while (true)
            {
                try
                {
                    conn.Insert(obj);
                    return true;
                }
                catch (SQLite.SQLiteException ex)
                {
                    if (ex.Result == SQLite.SQLite3.Result.Constraint)
                        return false;
                    if (ex.Result != SQLite.SQLite3.Result.Busy)
                        throw ex;
                    System.Threading.Thread.Sleep(10);
                }
            }
        }
    }
}
