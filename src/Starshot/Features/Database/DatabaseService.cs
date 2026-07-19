using Dapper;
using Microsoft.Data.Sqlite;
using System.IO;

namespace Starshot.Features.Database;

internal static class DatabaseService
{


    private static string _connectionString;


    public static SqliteConnection CreateConnection()
    {
        var con = new SqliteConnection(_connectionString);
        con.Open();
        return con;
    }


    public static void SetDatabase(string folder)
    {
        if (Directory.Exists(folder))
        {
            string path = Path.GetFullPath(Path.Combine(folder, "StarshotDatabase.db"));
            _connectionString = $"DataSource={path};";
            using var con = CreateConnection();
            con.Execute("PRAGMA JOURNAL_MODE = WAL;");
            con.Execute("""
                CREATE TABLE IF NOT EXISTS Setting
                (
                    Key   TEXT NOT NULL PRIMARY KEY,
                    Value TEXT
                );
                """);
        }
    }



}
