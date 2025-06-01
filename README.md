# Event Streamer for Microsoft SQL Server

A lightweight .NET 9 AOT console application that executes SQL Server queries and streams real-time InfoMessage events, result sets, and output parameters as JSON.

Designed for applications and languages that need real-time SQL execution feedback but cannot access native SQL Server driver events.

## Features

- **Real-time progress**: Captures SQL Server InfoMessage events during execution
- **Result set streaming**: Streams query results row-by-row as JSON events
- **Output parameter detection**: Automatically extracts OUTPUT parameters from SQL
- **Multiple result sets**: Handles SQL with multiple result sets
- **Lightweight**: Native AOT compiled to ~28MB single binary
- **Unix-style**: Reads SQL from stdin, writes JSON events to stdout
- **Docker buildable**: No need for local .NET SDK installation
- **Fast startup**: Native compilation means instant startup

## Usage

### Basic Usage

```bash
# Using --sql argument with SQL text
sql-info-streamer -c "Server=localhost;Database=MyDB;..." --sql "EXEC MyStoredProc @param1='value'"

# Using --sql argument with SQL file
sql-info-streamer --sql script.sql

# Using environment variable and stdin
export SQL_CONNECTION="Server=localhost;Database=MyDB;Integrated Security=true;"
echo "EXEC LongRunningProc" | sql-info-streamer
```

### With Timeout and Cancellation

```bash
# 30 minute timeout
sql-info-streamer -t 1800 --sql "EXEC VeryLongProc"

# Press Ctrl+C to cancel gracefully at any time
sql-info-streamer --sql long-running-script.sql
# ^C
# {"timestamp":"2024-01-15T10:30:52.123Z","type":"info","message":"Cancellation requested, shutting down gracefully..."}
# {"timestamp":"2024-01-15T10:30:52.124Z","type":"info","message":"Operation cancelled by user"}
```

### Building with Docker

```bash
# Build the image
docker build -t sql-info-streamer .

# Run with Docker
echo "EXEC MyProc" | docker run -i --rm \
  -e SQL_CONNECTION="Server=host.docker.internal;..." \
  sql-info-streamer
```

## Output Format

Each line is a JSON object. The application streams various types of events during SQL execution:

### Basic Events
```json
{"timestamp":"2024-01-15T10:30:45.123Z","type":"started","message":"SQL execution began"}
{"timestamp":"2024-01-15T10:30:47.456Z","type":"info","message":"Processing 1000 records...","severity":0}
{"timestamp":"2024-01-15T10:31:15.012Z","type":"completed","message":"SQL execution completed successfully"}
```

### Result Set Streaming Events
```json
{"timestamp":"2024-01-15T10:30:46.123Z","type":"result_set_start","resultSetIndex":0,"columns":["id","name","email"]}
{"timestamp":"2024-01-15T10:30:46.125Z","type":"row","resultSetIndex":0,"rowIndex":0,"data":{"id":"1","name":"John Doe","email":"john@example.com"}}
{"timestamp":"2024-01-15T10:30:46.127Z","type":"row","resultSetIndex":0,"rowIndex":1,"data":{"id":"2","name":"Jane Smith","email":null}}
{"timestamp":"2024-01-15T10:30:46.130Z","type":"result_set_end","resultSetIndex":0,"totalRows":2}
```

### Output Parameters
```json
{"timestamp":"2024-01-15T10:31:14.500Z","type":"output_parameters","outputParameters":{"@result":"success","@count":"42","@error":null}}
```

### Event Types

- `started`: SQL execution began
- `info`: InfoMessage from SQL statements (PRINT statements, RAISERROR, etc.)
- `result_set_start`: Beginning of a result set with column information
- `row`: Individual row data from a result set
- `result_set_end`: End of a result set with total row count
- `output_parameters`: Output parameter values from stored procedures
- `completed`: Successful execution completion
- `error`: SQL error or execution failure

### Properties

#### Common Properties (all events)
- `timestamp`: UTC timestamp in ISO 8601 format (e.g., "2024-01-15T10:30:45.123Z")
- `type`: Event type (see Event Types above)

#### Basic Event Properties
- `message`: The actual message text (for started/info/completed/error events)
- `severity`: SQL Server severity level (for info/error events)
- `errorNumber`: SQL Server error number (for error events)

#### Result Set Properties
- `resultSetIndex`: Zero-based index of the result set (for result_set_start/row/result_set_end events)
- `columns`: Array of column names (for result_set_start events)
- `rowIndex`: Zero-based row number within the result set (for row events)
- `data`: Dictionary of column names to string values (for row events)
- `totalRows`: Total number of rows processed in the result set (for result_set_end events)

#### Output Parameter Properties
- `outputParameters`: Dictionary of parameter names to string values (for output_parameters events)

## Integration Examples

### PHP Integration

```php
// PHP integration with full event handling
$process = proc_open('sql-info-streamer', [
    0 => ['pipe', 'r'], // stdin
    1 => ['pipe', 'r'], // stdout
    2 => ['pipe', 'r']  // stderr
], $pipes, null, ['SQL_CONNECTION' => $connectionString]);

fwrite($pipes[0], "EXEC MyLongStoredProc");
fclose($pipes[0]);

$resultSets = [];
$currentResultSet = null;

while (!feof($pipes[1])) {
    $line = fgets($pipes[1]);
    if ($line) {
        $event = json_decode(trim($line), true);
        
        switch ($event['type']) {
            case 'info':
                updateTaskProgress($taskId, $event['message']);
                break;
            case 'result_set_start':
                $currentResultSet = ['columns' => $event['columns'], 'rows' => []];
                break;
            case 'row':
                $currentResultSet['rows'][] = $event['data'];
                break;
            case 'result_set_end':
                $resultSets[] = $currentResultSet;
                break;
            case 'output_parameters':
                handleOutputParameters($event['outputParameters']);
                break;
            case 'completed':
                processResults($resultSets);
                break;
        }
    }
}
```

### Node.js Integration

```javascript
const { spawn } = require('child_process');

const proc = spawn('sql-info-streamer', ['-c', connectionString], {
    stdio: ['pipe', 'pipe', 'pipe'],
    env: { ...process.env }
});

proc.stdin.write('SELECT * FROM users');
proc.stdin.end();

proc.stdout.on('data', (data) => {
    const lines = data.toString().split('\n').filter(line => line.trim());
    
    lines.forEach(line => {
        const event = JSON.parse(line);
        
        if (event.type === 'row') {
            console.log('Row received:', event.data);
        } else if (event.type === 'info') {
            console.log('Progress:', event.message);
        }
    });
});
```

## Testing

A test SQL file is provided to demonstrate real-time progress streaming:

```bash
# Test with the provided test SQL
cat test-progress.sql | sql-info-streamer -c "your-connection-string"

# Expected output: JSON events showing progress messages every few seconds
```

## Easy Multi-Platform Building with Docker

### build.sh

```bash
# Build for multiple platforms at once
./build.sh
```

This creates ready-to-deploy binaries in the `build/` directory:
- `build/sql-info-streamer-linux-x64` - Linux x64 (servers, containers)
- `build/sql-info-streamer-linux-arm64` - Linux ARM64 (Raspberry Pi, ARM servers)
- `build/sql-info-streamer-macos-arm64` - macOS ARM64 (if running on macOS with .NET SDK)

### What the Build Does

1. **Linux x64** - Built using Docker with `--platform linux/amd64`
2. **Linux ARM64** - Built using Docker with `--platform linux/arm64` 
3. **macOS ARM64** - Built locally using dotnet CLI (if available)
4. **Windows** - Build locally using dotnet CLI (see instructions below)

### Platform-Specific Instructions

**For Windows builds** (run on Windows):

It's not enough to install .NET 9 SDK, Microsoft VisualStudio 2022 is also required 
with the "Desktop development with C++" workload.

```cmd
dotnet publish SqlInfoStreamer/SqlInfoStreamer.csproj -c Release -r win-x64 --self-contained -o build
```

**For macOS arm64 builds** (run on macOS):

Install the .NET 9 SDK.

```bash
dotnet publish SqlInfoStreamer/SqlInfoStreamer.csproj -c Release -r osx-arm64 --self-contained -o build
```
