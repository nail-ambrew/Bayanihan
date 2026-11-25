using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;

namespace BayanihanHQ
{
    /// <summary>
    /// Listens on http://127.0.0.1:47821/ for events POSTed by the Claude Code hook. HttpListener blocks, so it runs on a
    /// background thread and only queues the raw JSON; the main thread drains the queue in HqDirector.Update, because
    /// Unity objects may only be touched there.
    /// </summary>
    public sealed class HqReceiver : IDisposable
    {
        const int MaxBody = 16 * 1024;     // the hook's lines are well under 1 KB; anything bigger is not ours
        const int MaxQueued = 5000;        // if the scene is paused for a long time, keep the newest work bounded

        readonly ConcurrentQueue<string> queue = new ConcurrentQueue<string>();
        HttpListener listener;
        Thread thread;
        volatile bool running;

        /// <summary>True while the listener is bound and accepting.</summary>
        public bool Listening => running && listener != null && listener.IsListening;

        /// <summary>Why the listener couldn't start (port in use, etc.), or null.</summary>
        public string Error { get; private set; }

        /// <summary>Number of events received since Start.</summary>
        public int Received;

        /// <summary>Binds to the loopback address only, so nothing on the network can reach it.</summary>
        public bool Start(int port)
        {
            Stop();
            Error = null;
            try
            {
                listener = new HttpListener();
                listener.Prefixes.Add($"http://127.0.0.1:{port}/");
                listener.Start();
                running = true;
                thread = new Thread(Loop) { IsBackground = true, Name = "BayanihanHQ.Receiver" };
                thread.Start();
                return true;
            }
            catch (Exception e)
            {
                Error = e.Message;
                running = false;
                try { listener?.Close(); } catch (Exception) { }
                listener = null;
                return false;
            }
        }

        /// <summary>Takes the next raw JSON line, if any. Main thread only.</summary>
        public bool TryDequeue(out string json) => queue.TryDequeue(out json);

        /// <summary>Lines waiting to be applied (grows while the office is paused).</summary>
        public int Pending => queue.Count;

        void Loop()
        {
            while (running)
            {
                HttpListenerContext ctx;
                try { ctx = listener.GetContext(); }
                catch (Exception) { break; }   // Stop() closes the listener, which ends the wait with an exception
                try { Handle(ctx); }
                catch (Exception) { try { ctx.Response.Abort(); } catch (Exception) { } }
            }
        }

        void Handle(HttpListenerContext ctx)
        {
            var req = ctx.Request;
            var res = ctx.Response;
            if (!IPAddress.IsLoopback(req.RemoteEndPoint.Address))
            {
                res.StatusCode = 403;
                res.Close();
                return;
            }
            if (req.HttpMethod == "POST" && req.Url.AbsolutePath == "/event" && req.ContentLength64 <= MaxBody)
            {
                string body;
                using (var reader = new StreamReader(req.InputStream, Encoding.UTF8)) body = reader.ReadToEnd();
                if (body.Length <= MaxBody)
                {
                    if (queue.Count >= MaxQueued) queue.TryDequeue(out _);
                    queue.Enqueue(body);
                    Interlocked.Increment(ref Received);
                }
                res.StatusCode = 204;
            }
            else
            {
                res.StatusCode = req.HttpMethod == "GET" ? 200 : 400;   // GET / answers "alive" for a quick curl check
            }
            res.Close();
        }

        /// <summary>Closes the port. Safe to call twice; called on Play-mode exit and before a domain reload.</summary>
        public void Stop()
        {
            running = false;
            try { listener?.Stop(); } catch (Exception) { }
            try { listener?.Close(); } catch (Exception) { }
            listener = null;
            if (thread != null && thread.IsAlive) thread.Join(200);
            thread = null;
        }

        public void Dispose() => Stop();
    }
}
