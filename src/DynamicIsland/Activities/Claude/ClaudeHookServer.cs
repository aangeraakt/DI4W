using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using DynamicIsland.Core;

namespace DynamicIsland.Activities.Claude;

/// <summary>
/// Tiny HTTP endpoint on 127.0.0.1 that Claude Code hooks POST their JSON payload to.
/// Hand-rolled on a TcpListener so it never needs admin rights or URL ACLs (unlike HttpListener).
/// Routes: /claude/start, /claude/stop, /claude/notify, /claude/end
/// </summary>
public sealed class ClaudeHookServer : IDisposable
{
    private readonly int _port;
    private readonly Action<string, JsonElement> _onEvent;
    private readonly CancellationTokenSource _cts = new();
    private TcpListener? _listener;

    public ClaudeHookServer(int port, Action<string, JsonElement> onEvent)
    {
        _port = port;
        _onEvent = onEvent;
    }

    public void Start()
    {
        try
        {
            _listener = new TcpListener(IPAddress.Loopback, _port);
            _listener.Start();
            _ = AcceptLoop(_cts.Token);
            Log.Info($"Claude hook server listening on http://127.0.0.1:{_port}/");
        }
        catch (Exception ex)
        {
            Log.Error($"Could not listen on port {_port}", ex);
        }
    }

    private async Task AcceptLoop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested && _listener is not null)
        {
            try
            {
                var client = await _listener.AcceptTcpClientAsync(ct);
                _ = Handle(client, ct);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex)
            {
                Log.Error("Accept failed", ex);
            }
        }
    }

    private async Task Handle(TcpClient client, CancellationToken ct)
    {
        using (client)
        {
            try
            {
                client.ReceiveTimeout = 3000;
                using var stream = client.GetStream();
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeout.CancelAfter(TimeSpan.FromSeconds(3));

                var (path, body) = await ReadRequest(stream, timeout.Token);

                var status = "200 OK";
                if (path.StartsWith("/claude/", StringComparison.OrdinalIgnoreCase))
                {
                    JsonElement json;
                    try
                    {
                        json = string.IsNullOrWhiteSpace(body)
                            ? JsonDocument.Parse("{}").RootElement.Clone()
                            : JsonDocument.Parse(body).RootElement.Clone();
                    }
                    catch (JsonException)
                    {
                        json = JsonDocument.Parse("{}").RootElement.Clone();
                    }
                    _onEvent(path["/claude/".Length..].Trim('/').ToLowerInvariant(), json);
                }
                else if (path != "/")
                {
                    status = "404 Not Found";
                }

                var response = Encoding.ASCII.GetBytes(
                    $"HTTP/1.1 {status}\r\nContent-Type: text/plain\r\nContent-Length: 2\r\nConnection: close\r\n\r\nok");
                await stream.WriteAsync(response, timeout.Token);
            }
            catch (Exception ex) when (ex is IOException or OperationCanceledException or SocketException)
            {
                // Client went away; nothing to do.
            }
            catch (Exception ex)
            {
                Log.Error("Hook request failed", ex);
            }
        }
    }

    private static async Task<(string Path, string Body)> ReadRequest(NetworkStream stream, CancellationToken ct)
    {
        var buffer = new byte[8192];
        var data = new List<byte>();
        int headerEnd;
        while (true)
        {
            var n = await stream.ReadAsync(buffer, ct);
            if (n == 0) throw new IOException("closed");
            data.AddRange(buffer.AsSpan(0, n).ToArray());
            headerEnd = IndexOf(data, "\r\n\r\n"u8);
            if (headerEnd >= 0) break;
            if (data.Count > 64 * 1024) throw new IOException("headers too large");
        }

        var headers = Encoding.ASCII.GetString(data.GetRange(0, headerEnd).ToArray());
        var lines = headers.Split("\r\n");
        var parts = lines[0].Split(' ');
        var path = parts.Length > 1 ? parts[1] : "/";

        var contentLength = 0;
        foreach (var line in lines.Skip(1))
        {
            var idx = line.IndexOf(':');
            if (idx > 0 && line[..idx].Trim().Equals("Content-Length", StringComparison.OrdinalIgnoreCase))
            {
                int.TryParse(line[(idx + 1)..].Trim(), out contentLength);
            }
        }
        contentLength = Math.Clamp(contentLength, 0, 4 * 1024 * 1024);

        var bodyStart = headerEnd + 4;
        while (data.Count - bodyStart < contentLength)
        {
            var n = await stream.ReadAsync(buffer, ct);
            if (n == 0) break;
            data.AddRange(buffer.AsSpan(0, n).ToArray());
        }
        var bodyBytes = data.Skip(bodyStart).Take(contentLength).ToArray();
        return (path, Encoding.UTF8.GetString(bodyBytes));
    }

    private static int IndexOf(List<byte> data, ReadOnlySpan<byte> pattern)
    {
        for (var i = 0; i <= data.Count - pattern.Length; i++)
        {
            var match = true;
            for (var j = 0; j < pattern.Length; j++)
            {
                if (data[i + j] != pattern[j]) { match = false; break; }
            }
            if (match) return i;
        }
        return -1;
    }

    public void Dispose()
    {
        _cts.Cancel();
        _listener?.Stop();
    }
}
