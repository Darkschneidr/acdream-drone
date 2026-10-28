[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Pipe,
    [ValidateSet('status', 'start', 'stop', 'reload-client')][string]$Command = 'status',
    [string]$Server,
    [string]$Account,
    [string]$Session
)

$ErrorActionPreference = 'Stop'
$request = @{ command = $Command }
if ($Server) { $request.server = $Server }
if ($Account) { $request.account = $Account }
if ($Session) { $request.session = $Session }
$payload = [Text.Encoding]::UTF8.GetBytes(($request | ConvertTo-Json -Compress))
$connection = [IO.Pipes.NamedPipeClientStream]::new('.', $Pipe, [IO.Pipes.PipeDirection]::InOut, [IO.Pipes.PipeOptions]::Asynchronous)
$deadline = [Threading.CancellationTokenSource]::new([TimeSpan]::FromSeconds(35))
try {
    $connection.ConnectAsync(5000, $deadline.Token).GetAwaiter().GetResult()
    $prefix = [BitConverter]::GetBytes([int]$payload.Length)
    $connection.WriteAsync($prefix, 0, 4, $deadline.Token).GetAwaiter().GetResult()
    $connection.WriteAsync($payload, 0, $payload.Length, $deadline.Token).GetAwaiter().GetResult()
    function Read-Exact([byte[]]$Buffer) {
        $offset = 0
        while ($offset -lt $Buffer.Length) {
            $count = $connection.ReadAsync($Buffer, $offset, $Buffer.Length - $offset, $deadline.Token).GetAwaiter().GetResult()
            if ($count -eq 0) { throw 'Control connection closed; inspect status before repeating a command.' }
            $offset += $count
        }
    }
    Read-Exact $prefix
    $length = [BitConverter]::ToInt32($prefix, 0)
    if ($length -le 0 -or $length -gt 1048576) { throw 'Invalid response length.' }
    $response = [byte[]]::new($length)
    Read-Exact $response
    $reply = [Text.Encoding]::UTF8.GetString($response) | ConvertFrom-Json
    if (-not $reply.ok) { throw $reply.error }
    $reply.data
} finally {
    $connection.Dispose()
    $deadline.Dispose()
}
