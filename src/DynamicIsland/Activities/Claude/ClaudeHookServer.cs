using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using DynamicIsland.Core;

namespace DynamicIsland.Activities.Claude;

/// <summary>A request the server hands to its handlers; the JSON body is "{}" when empty or invalid.</summary>
public sealed record LocalRequest(string Method, string Path, JsonElement Body);

/// <summary>What a handler answers: HTTP status and a JSON body.</summary>
public sealed record LocalResponse(int Status, string Json)
{
    public static LocalResponse Ok { get; } = new(200, """{"ok":true}""");
    public static LocalResponse Error(int status, string message) => new(status, ToJson(new { error = message }));

    /// <summary>Readable JSON for people trying the API by hand ("title", not "title").</summary>
    public static string ToJson(object value) => JsonSerializer.Serialize(value, Readable);

    private static readonly JsonSerializerOptions Readable = new()
    {
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };
}

/// <summary>
/// Tiny HTTP endpoint on 127.0.0.1 for Claude Code hooks and the Island API.
/// Hand-rolled on a TcpListener so it never needs admin rights or URL ACLs (unlike HttpListener).
/// Requests from web pages are refused (they carry an Origin header, or a foreign Host after DNS rebinding),
/// so only programs on this PC, such as hook scripts and curl, can talk to it.
/// </summary>
public sealed class ClaudeHookServer : IDisposable
{
    private readonly int _port;
    private readonly Func<LocalRequest, LocalResponse?> _handler;
    private readonly CancellationTokenSource _cts = new();
    private TcpListener? _listener;

    /// <param name="handler">Answers a request, or returns null for "not found". Called on a background thread.</param>
    public ClaudeHookServer(int port, Func<LocalRequest, LocalResponse?> handler)
    {
        _port = port;
        _handler = handler;
    }

    public void Start()
    {
        try
        {
            _listener = new TcpListener(IPAddress.Loopback, _port);
            _listener.Start();
            _ = AcceptLoop(_cts.Token);
            Log.Info($"Local server listening on http://127.0.0.1:{_port}/");
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

                var request = await ReadRequest(stream, timeout.Token);
                var response = Answer(request);

                var body = Encoding.UTF8.GetBytes(response.Json);
                var head = Encoding.ASCII.GetBytes(
                    $"HTTP/1.1 {response.Status} {Reason(response.Status)}\r\nContent-Type: application/json\r\n" +
                    $"Content-Length: {body.Length}\r\nConnection: close\r\n\r\n");
                await stream.WriteAsync(head, timeout.Token);
                await stream.WriteAsync(body, timeout.Token);
            }
            catch (Exception ex) when (ex is IOException or OperationCanceledException or SocketException)
            {
                // Client went away; nothing to do.
            }
            catch (Exception ex)
            {
                Log.Error("Local request failed", ex);
            }
        }
    }

    private LocalResponse Answer(RawRequest request)
    {
        // Browsers add Origin to cross-site requests, and a DNS-rebinding page would arrive with its own Host.
        if (request.Headers.ContainsKey("Origin")) return LocalResponse.Error(403, "Requests from web pages are not allowed.");
        if (request.Headers.TryGetValue("Host", out var host) && !IsLocalHost(host)) return LocalResponse.Error(403, "Unexpected Host header.");

        JsonElement json;
        try
        {
            json = JsonDocument.Parse(string.IsNullOrWhiteSpace(request.Body) ? "{}" : request.Body).RootElement.Clone();
        }
        catch (JsonException)
        {
            return LocalResponse.Error(400, "The body isn't valid JSON.");
        }

        try
        {
            return _handler(new LocalRequest(request.Method, request.Path, json)) ?? LocalResponse.Error(404, "No such endpoint.");
        }
        catch (Exception ex)
        {
            Log.Error($"Local request {request.Method} {request.Path} failed", ex);
            return LocalResponse.Error(500, ex.Message);
        }
    }

    private bool IsLocalHost(string host)
    {
        var name = host.Contains(':') ? host[..host.LastIndexOf(':')] : host;
        return name is "127.0.0.1" or "localhost" or "[::1]";
    }

    private static string Reason(int status) => status switch
    {
        200 => "OK",
        400 => "Bad Request",
        403 => "Forbidden",
        404 => "Not Found",
        429 => "Too Many Requests",
        _ => "Internal Server Error",
    };

    private sealed record RawRequest(string Method, string Path, Dictionary<string, string> Headers, string Body);

    private static async Task<RawRequest> ReadRequest(NetworkStream stream, CancellationToken ct)
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

        var lines = Encoding.ASCII.GetString(data.GetRange(0, headerEnd).ToArray()).Split("\r\n");
        var parts = lines[0].Split(' ');
        var method = parts[0].ToUpperInvariant();
        var path = parts.Length > 1 ? parts[1].Split('?')[0] : "/";

        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in lines.Skip(1))
        {
            var idx = line.IndexOf(':');
            if (idx > 0) headers[line[..idx].Trim()] = line[(idx + 1)..].Trim();
        }
        var contentLength = headers.TryGetValue("Content-Length", out var cl) && int.TryParse(cl, out var len) ? len : 0;
        contentLength = Math.Clamp(contentLength, 0, 1024 * 1024);

        var bodyStart = headerEnd + 4;
        while (data.Count - bodyStart < contentLength)
        {
            var n = await stream.ReadAsync(buffer, ct);
            if (n == 0) break;
            data.AddRange(buffer.AsSpan(0, n).ToArray());
        }
        var bodyBytes = data.Skip(bodyStart).Take(contentLength).ToArray();
        return new RawRequest(method, path, headers, Encoding.UTF8.GetString(bodyBytes));
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
