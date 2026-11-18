using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace GameHarness
{
    // One request per connection: the client sends one UTF-8 line (command and arguments separated by tabs), the
    // server answers with one JSON line and closes. Requests are queued and run on Unity's main thread in Update.
    internal sealed class Server
    {
        internal sealed class Request
        {
            internal string Command;
            internal string[] Args;
            private string _reply;
            private readonly ManualResetEvent _done = new ManualResetEvent(false);

            internal void Reply(string json)
            {
                if (_reply != null) return;
                _reply = json;
                _done.Set();
            }

            internal string Wait(int ms) => _done.WaitOne(ms) ? _reply : Json.Error("timed out on the game side (main thread busy or paused?)");
        }

        private readonly int _port;
        private readonly Queue<Request> _queue = new Queue<Request>();
        private TcpListener _listener;
        private Thread _thread;
        private volatile bool _running;

        internal Server(int port) => _port = port;

        internal void Start()
        {
            _listener = new TcpListener(IPAddress.Loopback, _port);
            _listener.Start();
            _running = true;
            _thread = new Thread(Loop) { IsBackground = true, Name = "GameHarness" };
            _thread.Start();
        }

        internal void Stop()
        {
            _running = false;
            try { _listener?.Stop(); } catch { }
        }

        internal void Drain(Action<Request> handle)
        {
            while (true)
            {
                Request r;
                lock (_queue)
                {
                    if (_queue.Count == 0) return;
                    r = _queue.Dequeue();
                }
                try { handle(r); }
                catch (Exception e) { r.Reply(Json.Error(e.GetType().Name + ": " + e.Message)); Plugin.Log.LogWarning(e); }
            }
        }

        private void Loop()
        {
            while (_running)
            {
                TcpClient client;
                try { client = _listener.AcceptTcpClient(); }
                catch { if (_running) Thread.Sleep(100); continue; }
                ThreadPool.QueueUserWorkItem(_ => Serve(client));
            }
        }

        private void Serve(TcpClient client)
        {
            using (client)
            {
                try
                {
                    client.ReceiveTimeout = 10000;
                    var stream = client.GetStream();
                    var reader = new StreamReader(stream, new UTF8Encoding(false));
                    string line = reader.ReadLine();
                    if (string.IsNullOrEmpty(line)) return;
                    string[] parts = line.Split('\t');
                    var req = new Request { Command = parts[0].Trim().ToLowerInvariant(), Args = new string[parts.Length - 1] };
                    Array.Copy(parts, 1, req.Args, 0, parts.Length - 1);
                    lock (_queue) _queue.Enqueue(req);
                    string reply = req.Wait(120000);
                    byte[] bytes = new UTF8Encoding(false).GetBytes(reply + "\n");
                    stream.Write(bytes, 0, bytes.Length);
                    stream.Flush();
                }
                catch (Exception e) { Plugin.Log.LogWarning("Connection failed: " + e.Message); }
            }
        }
    }
}
