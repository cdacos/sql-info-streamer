#region

using System.Data;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;

#endregion

[assembly: InternalsVisibleTo("SqlInfoStreamer.Tests")]

namespace SqlInfoStreamer;

// ReSharper disable MemberCanBePrivate.Global
// ReSharper disable once ClassNeverInstantiated.Global
// ReSharper disable AccessToDisposedClosure
internal class Program
{
    // Compiled regex for detecting OUTPUT parameters in SQL
    internal static readonly Regex OutputParameterRegex = new(
        @"(?<pre>^.*?)(?<param>@\w+)\s*OUTPUT\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Multiline
    );

    /// <summary>
    ///     Extracts output parameter names from SQL text, filtering out those in comments or quoted strings
    /// </summary>
    /// <param name="sql">The SQL text to analyze</param>
    /// <returns>List of unique output parameter names found</returns>
    internal static List<string> ExtractOutputParameters(string sql)
    {
        var outputParams = new HashSet<string>();
        var matches = OutputParameterRegex.Matches(sql);

        foreach (Match match in matches)
        {
            var preText = match.Groups["pre"].Value;
            var paramName = match.Groups["param"].Value;

            // Skip if this match is in a comment (line starts with --)
            if (preText.Contains("--"))
                continue;

            // Skip if this match is inside a quoted string (odd number of single quotes before it)
            var quoteCount = preText.Count(c => c == '\'');
            if (quoteCount % 2 == 1)
                continue;

            outputParams.Add(paramName);
        }

        return outputParams.ToList();
    }

    private static async Task<int> Main(string[] args)
    {
        // Setup cancellation for Ctrl+C
        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true; // Prevent immediate termination
            WriteEvent("info", "Cancellation requested, shutting down gracefully...");
            cts.Cancel();
        };

        try
        {
            var options = ParseArguments(args);

            string sql;
            if (!string.IsNullOrWhiteSpace(options.SqlInput))
            {
                // SQL provided via --sql argument
                if (File.Exists(options.SqlInput))
                    sql = await File.ReadAllTextAsync(options.SqlInput, cts.Token);
                else
                    sql = options.SqlInput;
            }
            else
            {
                // SQL provided via stdin
                sql = await Console.In.ReadToEndAsync(cts.Token);
            }

            if (string.IsNullOrWhiteSpace(sql))
            {
                WriteEvent("error", "No SQL provided via --sql argument or stdin");
                return 1;
            }

            await ExecuteSqlWithInfoMessages(options.ConnectionString, sql, options.Timeout, cts.Token);
            return 0;
        }
        catch (OperationCanceledException)
        {
            WriteEvent("info", "Operation cancelled by user");
            return 130; // Standard exit code for Ctrl+C
        }
        catch (Exception ex)
        {
            WriteEvent("error", ex.Message);
            return 1;
        }
    }

    private static async Task ExecuteSqlWithInfoMessages(string connectionString, string sql, int timeoutSeconds,
        CancellationToken cancellationToken = default)
    {
        await using var connection = new SqlConnection(connectionString);

        // Capture InfoMessage events for real-time progress
        connection.InfoMessage += (_, e) =>
        {
            foreach (SqlError error in e.Errors) WriteEvent("info", error.Message.Trim(), error.Class, error.Number);
        };

        try
        {
            await connection.OpenAsync(cancellationToken);
            WriteEvent("started", "SQL execution began");

            await using var command = new SqlCommand(sql, connection);
            command.CommandTimeout = timeoutSeconds;

            // Use helper method to identify output parameters in SQL
            var outputParamNames = ExtractOutputParameters(sql);
            var outputParams = new Dictionary<string, SqlParameter>();

            foreach (var paramName in outputParamNames)
            {
                var param = new SqlParameter(paramName, SqlDbType.NVarChar, -1)
                {
                    Direction = ParameterDirection.Output
                };
                command.Parameters.Add(param);
                outputParams[paramName] = param;
            }

            // Register cancellation to cancel the SQL command on the server
            await using var registration = cancellationToken.Register(() =>
            {
                try
                {
                    command.Cancel(); // Send cancellation to SQL Server
                    WriteEvent("info", "Cancellation sent to SQL Server");
                }
                catch
                {
                    // Ignore errors during cancellation
                }
            });

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);

            var resultSetIndex = 0;

            // Process all result sets and stream data
            do
            {
                // Skip empty result sets (from statements like USE database)
                if (reader.FieldCount == 0)
                    continue;

                // Get column information and emit result set start event
                var columns = new List<string>();
                for (var i = 0; i < reader.FieldCount; i++) 
                    columns.Add(reader.GetName(i));

                await WriteResultSetStart(resultSetIndex, columns, cancellationToken);

                // Stream rows one by one
                var rowIndex = 0;
                while (await reader.ReadAsync(cancellationToken))
                {
                    var row = new Dictionary<string, string?>();
                    for (var i = 0; i < reader.FieldCount; i++)
                    {
                        string? value = null;
                        if (!reader.IsDBNull(i))
                        {
                            // Convert all values to strings for reliable JSON serialization
                            var rawValue = reader.GetValue(i);
                            value = rawValue switch
                            {
                                DateTime dt => dt.ToString("yyyy-MM-ddTHH:mm:ss.fffZ"),
                                DateTimeOffset dto => dto.ToString("yyyy-MM-ddTHH:mm:ss.fffZ"),
                                TimeSpan ts => ts.ToString(),
                                byte[] bytes => Convert.ToBase64String(bytes),
                                _ => rawValue.ToString()
                            };
                        }

                        row[reader.GetName(i)] = value;
                    }

                    await WriteRow(resultSetIndex, rowIndex, row, cancellationToken);
                    rowIndex++;
                }

                await WriteResultSetEnd(resultSetIndex, rowIndex, cancellationToken);
                resultSetIndex++;
            } while (await reader.NextResultAsync(cancellationToken));

            // Close the reader to access output parameters
            await reader.CloseAsync();

            // Collect output parameter values
            Dictionary<string, string?>? outputParamValues = null;
            if (outputParams.Count > 0)
            {
                outputParamValues = new Dictionary<string, string?>();
                foreach (var (paramName, param) in outputParams)
                {
                    var value = param.Value;
                    string? stringValue = null;
                    if (value != null && value != DBNull.Value)
                        stringValue = value switch
                        {
                            DateTime dt => dt.ToString("yyyy-MM-ddTHH:mm:ss.fffZ"),
                            DateTimeOffset dto => dto.ToString("yyyy-MM-ddTHH:mm:ss.fffZ"),
                            _ => value.ToString()
                        };
                    outputParamValues[paramName] = stringValue;
                }
            }

            // Output parameters if any exist
            if (outputParamValues != null)
            {
                await WriteOutputParameters(outputParamValues, cancellationToken);
            }

            WriteEvent("completed", "SQL execution completed successfully");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            WriteEvent("info", "SQL execution cancelled");
            throw;
        }
        catch (SqlException ex)
        {
            WriteEvent("error", $"SQL Error: {ex.Message}", ex.Class, ex.Number);
            throw;
        }
        catch (Exception ex)
        {
            WriteEvent("error", $"Execution failed: {ex.Message}");
            throw;
        }
    }

    private static async Task WriteResultSetStart(int resultSetIndex, List<string> columns, CancellationToken cancellationToken = default)
    {
        var data = new ResultSetStartData
        {
            Timestamp = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ"),
            Type = "result_set_start",
            ResultSetIndex = resultSetIndex,
            Columns = columns
        };

        var json = JsonSerializer.Serialize(data, EventDataContext.Default.ResultSetStartData);
        Console.WriteLine(json);
        await Console.Out.FlushAsync(cancellationToken);
    }

    private static async Task WriteRow(int resultSetIndex, int rowIndex, Dictionary<string, string?> row, CancellationToken cancellationToken = default)
    {
        var data = new RowData
        {
            Timestamp = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ"),
            Type = "row",
            ResultSetIndex = resultSetIndex,
            RowIndex = rowIndex,
            Data = row
        };

        var json = JsonSerializer.Serialize(data, EventDataContext.Default.RowData);
        Console.WriteLine(json);
        await Console.Out.FlushAsync(cancellationToken);
    }

    private static async Task WriteResultSetEnd(int resultSetIndex, int totalRows, CancellationToken cancellationToken = default)
    {
        var data = new ResultSetEndData
        {
            Timestamp = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ"),
            Type = "result_set_end",
            ResultSetIndex = resultSetIndex,
            TotalRows = totalRows
        };

        var json = JsonSerializer.Serialize(data, EventDataContext.Default.ResultSetEndData);
        Console.WriteLine(json);
        await Console.Out.FlushAsync(cancellationToken);
    }

    private static async Task WriteOutputParameters(Dictionary<string, string?> outputParameters, CancellationToken cancellationToken = default)
    {
        var data = new OutputParametersData
        {
            Timestamp = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ"),
            Type = "output_parameters",
            OutputParameters = outputParameters
        };

        var json = JsonSerializer.Serialize(data, EventDataContext.Default.OutputParametersData);
        Console.WriteLine(json);
        await Console.Out.FlushAsync(cancellationToken);
    }

    private static void WriteEvent(string type, string message, byte? severity = null, int? errorNumber = null)
    {
        var eventData = new EventData
        {
            Timestamp = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ"),
            Type = type,
            Message = message,
            Severity = severity,
            ErrorNumber = errorNumber
        };

        var json = JsonSerializer.Serialize(eventData, EventDataContext.Default.EventData);

        Console.WriteLine(json);
        Console.Out.Flush();
    }

    private static Options ParseArguments(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("SQL_CONNECTION");
        var timeout = 0; // No timeout by default
        string? sqlInput = null;

        for (var i = 0; i < args.Length; i++)
            switch (args[i])
            {
                case "--connection":
                case "-c":
                    if (i + 1 < args.Length)
                        connectionString = args[++i];
                    break;
                case "--timeout":
                case "-t":
                    if (i + 1 < args.Length && int.TryParse(args[++i], out var timeoutValue))
                        timeout = timeoutValue;
                    break;
                case "--sql":
                case "-s":
                    if (i + 1 < args.Length)
                        sqlInput = args[++i];
                    break;
                case "--help":
                case "-h":
                    PrintUsage();
                    Environment.Exit(0);
                    break;
            }

        if (string.IsNullOrWhiteSpace(connectionString))
            throw new ArgumentException(
                "Connection string must be provided via --connection argument or SQL_CONNECTION environment variable");

        return new Options(connectionString, timeout, sqlInput);
    }

    private static void PrintUsage()
    {
        Console.Error.WriteLine("Usage: sql-info-streamer [options]");
        Console.Error.WriteLine();
        Console.Error.WriteLine("Executes SQL and streams InfoMessage events to stdout as JSON.");
        Console.Error.WriteLine();
        Console.Error.WriteLine("Options:");
        Console.Error.WriteLine(
            "  -c, --connection <string>    SQL Server connection string (or set SQL_CONNECTION env var)");
        Console.Error.WriteLine("  -s, --sql <sql|file>         SQL statement or path to SQL file");
        Console.Error.WriteLine("  -t, --timeout <seconds>      Command timeout in seconds (0 = no timeout, default)");
        Console.Error.WriteLine("  -h, --help                   Show this help message");
        Console.Error.WriteLine();
        Console.Error.WriteLine("Examples:");
        Console.Error.WriteLine(
            "  sql-info-streamer -c \"Server=localhost;Database=MyDB;...\" --sql \"EXEC MyStoredProc\"");
        Console.Error.WriteLine("  sql-info-streamer --sql script.sql");
        Console.Error.WriteLine(
            "  echo \"EXEC MyStoredProc\" | sql-info-streamer -c \"Server=localhost;Database=MyDB;...\"");
        Console.Error.WriteLine("  export SQL_CONNECTION=\"Server=...\"");
        Console.Error.WriteLine("  echo \"EXEC LongRunningProc\" | sql-info-streamer");
        Console.Error.WriteLine();
        Console.Error.WriteLine("Output format:");
        Console.Error.WriteLine(
            "  Each line is a JSON object with timestamp, type, message, and optional severity/errorNumber");
        Console.Error.WriteLine("  Types: started, info, completed, error");
    }

    private record Options(string ConnectionString, int Timeout, string? SqlInput);
}

public class EventData
{
    [JsonPropertyName("timestamp")] public string Timestamp { get; set; } = "";

    [JsonPropertyName("type")] public string Type { get; set; } = "";

    [JsonPropertyName("message")] public string Message { get; set; } = "";

    [JsonPropertyName("severity")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public byte? Severity { get; set; }

    [JsonPropertyName("errorNumber")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? ErrorNumber { get; set; }

    [JsonPropertyName("resultSet")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public SqlResultSet? ResultSet { get; set; }
}

public class ResultSetStartData
{
    [JsonPropertyName("timestamp")] public string Timestamp { get; set; } = "";

    [JsonPropertyName("type")] public string Type { get; set; } = "";

    [JsonPropertyName("resultSetIndex")] public int ResultSetIndex { get; set; }

    [JsonPropertyName("columns")] public List<string> Columns { get; set; } = new();
}

public class RowData
{
    [JsonPropertyName("timestamp")] public string Timestamp { get; set; } = "";

    [JsonPropertyName("type")] public string Type { get; set; } = "";

    [JsonPropertyName("resultSetIndex")] public int ResultSetIndex { get; set; }

    [JsonPropertyName("rowIndex")] public int RowIndex { get; set; }

    [JsonPropertyName("data")] public Dictionary<string, string?> Data { get; set; } = new();
}

public class ResultSetEndData
{
    [JsonPropertyName("timestamp")] public string Timestamp { get; set; } = "";

    [JsonPropertyName("type")] public string Type { get; set; } = "";

    [JsonPropertyName("resultSetIndex")] public int ResultSetIndex { get; set; }

    [JsonPropertyName("totalRows")] public int TotalRows { get; set; }
}

public class OutputParametersData
{
    [JsonPropertyName("timestamp")] public string Timestamp { get; set; } = "";

    [JsonPropertyName("type")] public string Type { get; set; } = "";

    [JsonPropertyName("outputParameters")] public Dictionary<string, string?> OutputParameters { get; set; } = new();
}

public class SqlResultSet
{
    [JsonPropertyName("columns")] public List<string> Columns { get; set; } = new();

    [JsonPropertyName("rows")] public List<Dictionary<string, string?>> Rows { get; set; } = new();

    [JsonPropertyName("rowCount")] public int RowCount { get; set; }
}

public class SqlResultData
{
    [JsonPropertyName("timestamp")] public string Timestamp { get; set; } = "";

    [JsonPropertyName("type")] public string Type { get; set; } = "";

    [JsonPropertyName("resultSets")] public List<SqlResultSet> ResultSets { get; set; } = new();

    [JsonPropertyName("totalResultSets")] public int TotalResultSets { get; set; }

    [JsonPropertyName("outputParameters")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Dictionary<string, string?>? OutputParameters { get; set; }
}

[JsonSerializable(typeof(EventData))]
[JsonSerializable(typeof(SqlResultSet))]
[JsonSerializable(typeof(SqlResultData))]
[JsonSerializable(typeof(ResultSetStartData))]
[JsonSerializable(typeof(RowData))]
[JsonSerializable(typeof(ResultSetEndData))]
[JsonSerializable(typeof(OutputParametersData))]
internal partial class EventDataContext : JsonSerializerContext
{
}