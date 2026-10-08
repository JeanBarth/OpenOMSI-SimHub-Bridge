using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Threading;

namespace OpenOMSI.SimHub
{
    internal sealed class TabletWebServer
    {
        private readonly int port;
        private readonly Func<string> getStateJson;
        private readonly Func<string, TabletCommandResult> dispatchCommand;
        private TcpListener listener;
        private Thread listenerThread;
        private volatile bool stopping;
        private string dashboardHtml;

        public TabletWebServer(int port, Func<string> getStateJson, Func<string, TabletCommandResult> dispatchCommand)
        {
            this.port = port;
            this.getStateJson = getStateJson;
            this.dispatchCommand = dispatchCommand;
        }

        public void Start()
        {
            Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("OpenOMSI.SimHub.TabletDashboard.html");
            if (stream == null) throw new InvalidOperationException("Embedded tablet dashboard was not found.");
            using (stream)
            using (StreamReader reader = new StreamReader(stream, Encoding.UTF8)) dashboardHtml = reader.ReadToEnd();

            listener = new TcpListener(IPAddress.Any, port);
            listener.Start(8);
            listenerThread = new Thread(ListenLoop);
            listenerThread.Name = "OpenOMSI tablet web server";
            listenerThread.IsBackground = true;
            listenerThread.Start();
        }

        public void Stop()
        {
            stopping = true;
            try { if (listener != null) listener.Stop(); } catch { }
            try { if (listenerThread != null && listenerThread.IsAlive) listenerThread.Join(1000); } catch { }
        }

        private void ListenLoop()
        {
            while (!stopping)
            {
                try
                {
                    TcpClient client = listener.AcceptTcpClient();
                    ThreadPool.QueueUserWorkItem(HandleClient, client);
                }
                catch (SocketException) { if (!stopping) Thread.Sleep(100); }
                catch (ObjectDisposedException) { break; }
            }
        }

        private void HandleClient(object state)
        {
            using (TcpClient client = (TcpClient)state)
            {
                try
                {
                    client.ReceiveTimeout = 3000;
                    client.SendTimeout = 3000;
                    using (NetworkStream network = client.GetStream())
                    using (StreamReader reader = new StreamReader(network, Encoding.ASCII, false, 1024, true))
                    {
                        string request = reader.ReadLine();
                        string[] requestParts = string.IsNullOrEmpty(request) ? new string[0] : request.Split(' ');
                        string method = requestParts.Length > 0 ? requestParts[0] : "";
                        string path = requestParts.Length > 1 ? requestParts[1] : "/";
                        int contentLength = 0;
                        string line;
                        while (!string.IsNullOrEmpty(line = reader.ReadLine()))
                        {
                            int separator = line.IndexOf(':');
                            if (separator <= 0) continue;
                            if (string.Equals(line.Substring(0, separator).Trim(), "Content-Length", StringComparison.OrdinalIgnoreCase))
                                int.TryParse(line.Substring(separator + 1).Trim(), out contentLength);
                        }

                        if (method == "POST" && path == "/api/command")
                        {
                            if (contentLength < 2 || contentLength > 512)
                            {
                                WriteResponse(network, 400, "Bad Request", "application/json; charset=utf-8", "{\"ok\":false,\"message\":\"Ungültige Anfrage.\"}");
                                return;
                            }
                            char[] body = new char[contentLength];
                            int read = 0;
                            while (read < body.Length)
                            {
                                int count = reader.Read(body, read, body.Length - read);
                                if (count <= 0) break;
                                read += count;
                            }
                            TabletCommandResult result = read == body.Length
                                ? dispatchCommand(new string(body))
                                : new TabletCommandResult(false, "Unvollständiger Befehl.");
                            string response = "{\"ok\":" + (result.Ok ? "true" : "false") + ",\"message\":\"" + JsonEscape(result.Message) + "\"}";
                            WriteResponse(network, result.Ok ? 200 : 409, result.Ok ? "OK" : "Conflict", "application/json; charset=utf-8", response);
                            return;
                        }

                        if (method == "GET" && path == "/api/state")
                            WriteResponse(network, 200, "OK", "application/json; charset=utf-8", getStateJson());
                        else if (method == "GET" && (path == "/" || path.StartsWith("/?", StringComparison.Ordinal)))
                            WriteResponse(network, 200, "OK", "text/html; charset=utf-8", dashboardHtml);
                        else if (method != "GET")
                            WriteResponse(network, 405, "Method Not Allowed", "text/plain; charset=utf-8", "GET or control POST only");
                        else
                            WriteResponse(network, 404, "Not Found", "text/plain; charset=utf-8", "Not found");
                    }
                }
                catch { }
            }
        }

        private static string JsonEscape(string value)
        {
            return (value ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", " ").Replace("\n", " ");
        }

        private static void WriteResponse(NetworkStream stream, int status, string reason, string contentType, string body)
        {
            byte[] payload = Encoding.UTF8.GetBytes(body ?? "");
            string headers = "HTTP/1.1 " + status + " " + reason + "\r\n" +
                "Content-Type: " + contentType + "\r\n" +
                "Content-Length: " + payload.Length + "\r\n" +
                "Cache-Control: no-store\r\n" +
                "Connection: close\r\n\r\n";
            byte[] headerBytes = Encoding.ASCII.GetBytes(headers);
            stream.Write(headerBytes, 0, headerBytes.Length);
            stream.Write(payload, 0, payload.Length);
            stream.Flush();
        }
    }
}
