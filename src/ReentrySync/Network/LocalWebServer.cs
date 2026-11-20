using System;
using System.Collections.Generic;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Fleck;

namespace ReentrySync.Network
{
    public class LocalWebServer
    {
        private HttpListener? _httpListener;
        private WebSocketServer? _wsServer;
        private CancellationTokenSource? _cts;
        private readonly List<IWebSocketConnection> _sockets = new List<IWebSocketConnection>();
        private readonly int _httpPort;
        private readonly int _wsPort;

        public bool IsRunning { get; private set; }

        public event Action<string>? OnMessageReceived;
        public event Action? OnClientConnected;
        public event Action<string>? OnLog;

        public LocalWebServer(int httpPort = 8080, int wsPort = 8081)
        {
            _httpPort = httpPort;
            _wsPort = wsPort;
        }

        public void Start()
        {
            if (IsRunning) return;
            _cts = new CancellationTokenSource();

            // 1. Start HTTP Server
            try
            {
                _httpListener = new HttpListener();
                _httpListener.Prefixes.Add($"http://127.0.0.1:{_httpPort}/");
                _httpListener.Prefixes.Add($"http://localhost:{_httpPort}/");
                _httpListener.Start();
                Task.Run(() => AcceptHttpConnectionsAsync(_cts.Token));
                OnLog?.Invoke($"[HTTP] Dashboard ready at http://localhost:{_httpPort}/");
            }
            catch (Exception ex)
            {
                OnLog?.Invoke($"[HTTP] Listener failed on port {_httpPort}: {ex.Message}");
            }

            // 2. Start Fleck WebSocket Server
            try
            {
                FleckLog.LogAction = (level, message, ex) => { }; // Silence verbose Fleck logs
                _wsServer = new WebSocketServer($"ws://0.0.0.0:{_wsPort}");
                _wsServer.Start(socket =>
                {
                    socket.OnOpen = () =>
                    {
                        lock (_sockets) _sockets.Add(socket);
                        OnLog?.Invoke($"[WebSocket] Client connected. Total: {_sockets.Count}");
                        OnClientConnected?.Invoke();
                    };
                    socket.OnClose = () =>
                    {
                        lock (_sockets) _sockets.Remove(socket);
                        OnLog?.Invoke($"[WebSocket] Client disconnected. Total: {_sockets.Count}");
                    };
                    socket.OnMessage = message =>
                    {
                        OnMessageReceived?.Invoke(message);
                    };
                });

                IsRunning = true;
                OnLog?.Invoke($"[WebSocket] Server running on ws://0.0.0.0:{_wsPort}");
            }
            catch (Exception ex)
            {
                OnLog?.Invoke($"[WebSocket] Failed on port {_wsPort}: {ex.Message}");
            }
        }

        private async Task AcceptHttpConnectionsAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested && _httpListener != null && _httpListener.IsListening)
            {
                try
                {
                    var context = await _httpListener.GetContextAsync();
                    ProcessHttpRequest(context);
                }
                catch (HttpListenerException) when (token.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    OnLog?.Invoke($"[HTTP] Connection error: {ex.Message}");
                }
            }
        }

        private void ProcessHttpRequest(HttpListenerContext context)
        {
            try
            {
                var response = context.Response;
                string path = context.Request.Url?.AbsolutePath ?? "/";

                if (path == "/" || path == "/index.html")
                {
                    string html = Web.DashboardHtml.GetHtml(_httpPort, _wsPort);
                    byte[] buffer = Encoding.UTF8.GetBytes(html);
                    response.ContentType = "text/html; charset=utf-8";
                    response.ContentLength64 = buffer.Length;
                    response.OutputStream.Write(buffer, 0, buffer.Length);
                }
                else if (path == "/api/status")
                {
                    int count;
                    lock (_sockets) count = _sockets.Count;
                    string json = $"{{\"status\":\"online\",\"clients\":{count}}}";
                    byte[] buffer = Encoding.UTF8.GetBytes(json);
                    response.ContentType = "application/json";
                    response.ContentLength64 = buffer.Length;
                    response.OutputStream.Write(buffer, 0, buffer.Length);
                }
                else
                {
                    response.StatusCode = 404;
                }

                response.OutputStream.Close();
            }
            catch (Exception ex)
            {
                OnLog?.Invoke($"[HTTP] Request error: {ex.Message}");
            }
        }

        public void Broadcast(string message)
        {
            lock (_sockets)
            {
                if (_sockets.Count == 0) return;
                for (int i = _sockets.Count - 1; i >= 0; i--)
                {
                    var socket = _sockets[i];
                    if (socket.IsAvailable)
                    {
                        socket.Send(message);
                    }
                    else
                    {
                        _sockets.RemoveAt(i);
                    }
                }
            }
        }

        public void Stop()
        {
            IsRunning = false;
            _cts?.Cancel();

            lock (_sockets)
            {
                foreach (var socket in _sockets)
                {
                    try { socket.Close(); } catch { }
                }
                _sockets.Clear();
            }

            try { _wsServer?.Dispose(); } catch { }
            try { _httpListener?.Stop(); _httpListener?.Close(); } catch { }
            OnLog?.Invoke("[WebServer] Stopped.");
        }
    }
}
