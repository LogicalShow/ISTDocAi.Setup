using System.Data;
using System.Globalization;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;

namespace ISTDocAi.OperatorTool;

public sealed class SqlRunner(string server, string database, bool trust) : IAsyncDisposable
{
    private SqlConnection? connection;
    public static string ConnectionString(string server, string database, bool trust)
    {
        ValidateNames(server, database);
        return new SqlConnectionStringBuilder
        {
            DataSource = server, InitialCatalog = database, IntegratedSecurity = true, Encrypt = SqlConnectionEncryptOption.Mandatory,
            TrustServerCertificate = trust, ConnectTimeout = 10, ApplicationName = "ISTDocAi.OperatorTool"
        }.ConnectionString;
    }
    public static void ValidateNames(string server, string database)
    {
        if (string.IsNullOrWhiteSpace(server) || server.Length > 256 || server.IndexOfAny([';', '=', '\r', '\n', '\0']) >= 0 || string.IsNullOrWhiteSpace(database) || database.Length > 128 || database.IndexOfAny([';', '=', '\r', '\n', '\0']) >= 0)
            throw new ArgumentException("Supply SQL Server and database names, never a connection string.");
    }
    public async Task<List<List<Dictionary<string, object?>>>> Execute(string sql)
    {
        if (connection is null)
        {
            connection = new SqlConnection(ConnectionString(server, database, trust));
            try { await connection.OpenAsync(); }
            catch (SqlException) { throw new InvalidOperationException($"Cannot connect to SQL Server '{server}'. Check Windows permissions, database, encryption and certificate trust."); }
        }
        var sets = new List<List<Dictionary<string, object?>>>();
        foreach (var batch in Regex.Split(sql, @"^\s*GO\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(batch)) continue;
            using var command = new SqlCommand(batch, connection) { CommandTimeout = 30 };
            await using var reader = await command.ExecuteReaderAsync();
            do
            {
                var rows = new List<Dictionary<string, object?>>();
                while (await reader.ReadAsync())
                {
                    var row = new Dictionary<string, object?>();
                    for (var i = 0; i < reader.FieldCount; i++) row[reader.GetName(i).Length == 0 ? "Value" + i : reader.GetName(i)] = await reader.IsDBNullAsync(i) ? null : reader.GetValue(i);
                    rows.Add(row);
                }
                if (reader.FieldCount > 0) sets.Add(rows);
            } while (await reader.NextResultAsync());
        }
        return sets;
    }
    public async Task<JsonNode> Report(PageRequest r)
    {
        var sets = await Execute(PageSql.Generate(r));
        var chunks = sets.SelectMany(s => s).SelectMany(row => row.Values).Select(v => Convert.ToString(v, CultureInfo.InvariantCulture));
        var text = string.Concat(chunks);
        if (text.Length == 0) throw new ArgumentException("Cycle or cursor not found for this consumer.");
        return Reports.Parse(text, r.Customer);
    }
    public async ValueTask DisposeAsync() { if (connection is not null) await connection.DisposeAsync(); }
}
