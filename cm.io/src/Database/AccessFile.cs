using System.Data;
using System.Data.OleDb;
using System.Runtime.Versioning;

namespace cm.io.Database;

/// <summary>
/// Read-only access to an Access database file through the ACE OLE DB provider (Windows only, the provider must be installed).
/// The connection is opened with Mode=Read, so the file is never written; nothing here builds SQL from user text beyond the
/// identifiers the caller passes, which the caller takes from the file's own schema.
/// </summary>
[SupportedOSPlatform("windows")]
public static class AccessFile
{
    const string Provider = "Microsoft.ACE.OLEDB.16.0";

    static OleDbConnection Open(string path)
    {
        var c = new OleDbConnection($"Provider={Provider};Data Source={path};Mode=Read");
        c.Open();
        return c;
    }

    public static IReadOnlyList<string> Tables(string path)
    {
        using var c = Open(path);
        using var t = c.GetOleDbSchemaTable(OleDbSchemaGuid.Tables, [null, null, null, "TABLE"])!;
        return t.Rows.Cast<DataRow>().Select(r => (string)r["TABLE_NAME"]).OrderBy(x => x, StringComparer.Ordinal).ToList();
    }

    public static IReadOnlyList<(string Name, string Type)> Columns(string path, string table)
    {
        using var c = Open(path);
        using var t = c.GetOleDbSchemaTable(OleDbSchemaGuid.Columns, [null, null, table, null])!;
        return t.Rows.Cast<DataRow>().OrderBy(r => Convert.ToInt32(r["ORDINAL_POSITION"], System.Globalization.CultureInfo.InvariantCulture))
            .Select(r => ((string)r["COLUMN_NAME"], ((OleDbType)Convert.ToInt32(r["DATA_TYPE"], System.Globalization.CultureInfo.InvariantCulture)).ToString())).ToList();
    }

    /// <summary>Runs a SELECT and returns the rows; the caller passes identifiers it read from <see cref="Tables"/> and <see cref="Columns"/>.</summary>
    public static IReadOnlyList<IReadOnlyDictionary<string, object?>> Select(string path, string sql)
    {
        using var c = Open(path);
        using var cmd = new OleDbCommand(sql, c);
        using var rd = cmd.ExecuteReader();
        var rows = new List<IReadOnlyDictionary<string, object?>>();
        while (rd.Read())
        {
            var d = new Dictionary<string, object?>(StringComparer.Ordinal);
            for (var i = 0; i < rd.FieldCount; i++) d[rd.GetName(i)] = rd.IsDBNull(i) ? null : rd.GetValue(i);
            rows.Add(d);
        }
        return rows;
    }
}
