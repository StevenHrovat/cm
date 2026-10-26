using System.Globalization;
using Microsoft.Data.SqlClient;

namespace cm.io.Database;

/// <summary>
/// One open connection to a SQL Server instance (TDS through Microsoft.Data.SqlClient): run a batch, or run a batch and get its first result set
/// as rows. The caller owns the SQL text and the connection string (a setting, never a value in the repository); nothing here builds SQL.
/// A batch is sent as one command, so a script's own <c>USE</c> or <c>GO</c>-less multi-statement batch behaves as it does in a query window.
/// </summary>
public sealed class SqlServerSession : IDisposable
{
    readonly SqlConnection _connection;
    readonly int _commandTimeoutSeconds;

    SqlServerSession(SqlConnection connection, int commandTimeoutSeconds)
    {
        _connection = connection;
        _commandTimeoutSeconds = commandTimeoutSeconds;
    }

    /// <summary>Opens the connection (throws <see cref="SqlException"/> when the instance does not accept it).</summary>
    public static SqlServerSession Open(string connectionString, int commandTimeoutSeconds = 60)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        var connection = new SqlConnection(connectionString);
        try { connection.Open(); }
        catch { connection.Dispose(); throw; }
        return new SqlServerSession(connection, commandTimeoutSeconds);
    }

    /// <summary>Runs the batch and returns the rows of its first result set; column names are the keys, SQL NULL is <c>null</c>.</summary>
    public IReadOnlyList<IReadOnlyDictionary<string, object?>> Query(string batch)
    {
        using var command = Command(batch);
        using var reader = command.ExecuteReader();
        var rows = new List<IReadOnlyDictionary<string, object?>>();
        while (reader.Read())
        {
            var row = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < reader.FieldCount; i++) row[reader.GetName(i)] = reader.IsDBNull(i) ? null : reader.GetValue(i);
            rows.Add(row);
        }
        return rows;
    }

    /// <summary>Runs the batch for its effect.</summary>
    public void Execute(string batch)
    {
        using var command = Command(batch);
        command.ExecuteNonQuery();
    }

    SqlCommand Command(string batch)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(batch);
        var command = _connection.CreateCommand();
        command.CommandText = batch;
        command.CommandTimeout = _commandTimeoutSeconds;
        return command;
    }

    public void Dispose() => _connection.Dispose();

    /// <summary>The value of a column as text with the invariant culture, for assertions that compare text.</summary>
    public static string? AsText(object? value) => value switch
    {
        null => null,
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString(),
    };
}
