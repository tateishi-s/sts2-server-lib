using System.Net;
using System.Net.Sockets;
using System.Text;
using Xunit;
using Sts2ServerLib;

namespace Sts2ServerLib.Tests;

/// <summary>
/// StateServer の統合テスト。
/// 各テストクラスで異なるポートを使用して競合を回避する。
/// </summary>

// ─────────────────────────────────────────────────────────────
// /state エンドポイント
// ─────────────────────────────────────────────────────────────
public class StateEndpointTests : IDisposable
{
    private const int Port = 21401;
    private readonly StateServer _server;
    private readonly HttpClient _client = new();

    public StateEndpointTests()
    {
        _server = new StateServer(
            getStateJson: () => """{"hp":72}""",
            port: Port
        );
        _server.Start();
    }

    [Fact]
    public async Task State_JSON文字列を返すこと()
    {
        var res = await _client.GetAsync($"http://localhost:{Port}/state");

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.Equal("application/json; charset=utf-8", res.Content.Headers.ContentType?.ToString());
        var body = await res.Content.ReadAsStringAsync();
        Assert.Equal("""{"hp":72}""", body);
    }

    [Fact]
    public async Task State_getStateJson関数が毎回呼ばれること()
    {
        var callCount = 0;
        using var server = new StateServer(
            getStateJson: () => { callCount++; return $"{{\"call\":{callCount}}}"; },
            port: Port + 10
        );
        server.Start();

        await _client.GetAsync($"http://localhost:{Port + 10}/state");
        await _client.GetAsync($"http://localhost:{Port + 10}/state");

        Assert.Equal(2, callCount);
    }

    public void Dispose()
    {
        _client.Dispose();
        _server.Dispose();
    }
}

// ─────────────────────────────────────────────────────────────
// CORS・OPTIONS
// ─────────────────────────────────────────────────────────────
public class CorsTests : IDisposable
{
    private const int Port = 21402;
    private readonly StateServer _server;
    private readonly HttpClient _client = new();

    public CorsTests()
    {
        _server = new StateServer(getStateJson: () => "{}", port: Port);
        _server.Start();
    }

    [Fact]
    public async Task State_CORSヘッダーが付与されること()
    {
        var res = await _client.GetAsync($"http://localhost:{Port}/state");

        Assert.Equal("*", res.Headers.GetValues("Access-Control-Allow-Origin").First());
    }

    [Fact]
    public async Task Options_204で応答すること()
    {
        var req = new HttpRequestMessage(HttpMethod.Options, $"http://localhost:{Port}/state");
        var res = await _client.SendAsync(req);

        Assert.Equal(HttpStatusCode.NoContent, res.StatusCode);
    }

    public void Dispose()
    {
        _client.Dispose();
        _server.Dispose();
    }
}

// ─────────────────────────────────────────────────────────────
// 静的ファイル配信
// ─────────────────────────────────────────────────────────────
public class StaticFileTests : IDisposable
{
    private const int Port = 21403;
    private readonly string _webRoot;
    private readonly StateServer _server;
    private readonly HttpClient _client = new();

    public StaticFileTests()
    {
        // 一時ディレクトリを webRoot として使用
        _webRoot = Path.Combine(Path.GetTempPath(), $"Sts2ServerLibTest_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_webRoot);

        File.WriteAllText(Path.Combine(_webRoot, "index.html"), "<html>test</html>");
        File.WriteAllText(Path.Combine(_webRoot, "style.css"), "body{}");

        _server = new StateServer(
            getStateJson: () => "{}",
            port: Port,
            webRoot: _webRoot
        );
        _server.Start();
    }

    [Fact]
    public async Task ルートパス_indexHtmlを返すこと()
    {
        var res = await _client.GetAsync($"http://localhost:{Port}/");

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.Contains("text/html", res.Content.Headers.ContentType?.ToString() ?? "");
        var body = await res.Content.ReadAsStringAsync();
        Assert.Equal("<html>test</html>", body);
    }

    [Fact]
    public async Task Css_正しいContentTypeで返すこと()
    {
        var res = await _client.GetAsync($"http://localhost:{Port}/style.css");

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.Contains("text/css", res.Content.Headers.ContentType?.ToString() ?? "");
    }

    [Fact]
    public async Task 存在しないファイル_404を返すこと()
    {
        var res = await _client.GetAsync($"http://localhost:{Port}/notfound.html");

        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
    }

    [Fact]
    public async Task パストラバーサル_403を返すこと()
    {
        // HttpClient は URL 内の ".." を正規化してしまうため TcpClient で生 HTTP リクエストを送信する
        var statusLine = await SendRawHttpGetAsync("localhost", Port, "/%2e%2e/secret.txt");

        Assert.Contains("403", statusLine);
    }

    /// <summary>HttpClient を使わず生の HTTP GET リクエストを送り、ステータス行を返す</summary>
    private static async Task<string> SendRawHttpGetAsync(string host, int port, string path)
    {
        using var tcp = new TcpClient(host, port);
        var stream = tcp.GetStream();
        var request = $"GET {path} HTTP/1.1\r\nHost: {host}\r\nConnection: close\r\n\r\n";
        var requestBytes = Encoding.ASCII.GetBytes(request);
        await stream.WriteAsync(requestBytes);
        await stream.FlushAsync();

        var buffer = new byte[4096];
        var read = await stream.ReadAsync(buffer);
        return Encoding.ASCII.GetString(buffer, 0, read).Split('\n')[0];
    }

    [Fact]
    public async Task WebRootなし_静的ファイルリクエストが404を返すこと()
    {
        using var server = new StateServer(
            getStateJson: () => "{}",
            port: Port + 10
            // webRoot を指定しない
        );
        server.Start();

        var res = await _client.GetAsync($"http://localhost:{Port + 10}/index.html");

        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
    }

    public void Dispose()
    {
        _client.Dispose();
        _server.Dispose();
        if (Directory.Exists(_webRoot))
            Directory.Delete(_webRoot, recursive: true);
    }
}

// ─────────────────────────────────────────────────────────────
// ログ注入
// ─────────────────────────────────────────────────────────────
public class LoggingTests : IDisposable
{
    private const int Port = 21404;

    [Fact]
    public void Start_カスタムログ関数が呼ばれること()
    {
        var logs = new List<string>();
        using var server = new StateServer(
            getStateJson: () => "{}",
            port: Port,
            log: msg => logs.Add(msg)
        );

        server.Start();

        Assert.Contains(logs, msg => msg.Contains("HTTPサーバー起動"));
    }

    public void Dispose() { }
}

// ─────────────────────────────────────────────────────────────
// Dispose
// ─────────────────────────────────────────────────────────────
public class DisposeTests
{
    private const int Port = 21405;

    [Fact]
    public async Task Dispose後_接続が拒否されること()
    {
        var server = new StateServer(getStateJson: () => "{}", port: Port);
        server.Start();
        server.Dispose();

        // サーバー停止が伝播するまで少し待つ
        await Task.Delay(100);

        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
        await Assert.ThrowsAnyAsync<Exception>(
            () => client.GetAsync($"http://localhost:{Port}/state")
        );
    }
}
