using Dapper;
using Microsoft.Data.Sqlite;
using System.Data;
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


    /// <summary>
    /// 在线备份：VACUUM 压缩后用 SQLite BackupDatabase API 复制到目标文件。
    /// 不需要关连接、WAL 安全，运行中可调。
    /// </summary>
    public static void BackupDatabase(string file)
    {
        using var backupCon = new SqliteConnection($"DataSource={file}; Pooling=False;");
        backupCon.Open();
        using var con = CreateConnection();
        con.Execute("VACUUM;", commandType: CommandType.Text);
        con.BackupDatabase(backupCon);
    }


}
