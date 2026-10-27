using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace RickAndMortyTogether
{
    public sealed class NetworkEvent
    {
        public int Peer;
        public string Kind;
        public byte[] Data;
        public string Message;
    }

    // Length-prefixed frames over TCP for LAN / direct play (and the lab). The game runs .NET 3.5, so the queues are
    // plain locked queues. No Unity calls are permitted on the socket threads.
    public sealed partial class Transport : IDisposable
    {
        public const int MaxFrame = 65536;
        // The host plus one other player (the user's call: the game is built around one Morty and Rick's attention).
        public const int MaxPlayers = 2;
        public static readonly string FullMessage = "The game is full (" + MaxPlayers + " players).";   // process-scoped (a constant)
        // Lab only: simulated one-way delay and jitter on every send, to test desync on slow links.
        public static int LagMs, JitterMs;   // lab-only
        readonly Queue<NetworkEvent> events = new Queue<NetworkEvent>();
        readonly Dictionary<int, Link> links = new Dictionary<int, Link>();
        readonly object gate = new object();
        // Packets waiting for the main thread, per peer id (0 = the host on a client, 1-3 = clients on the host).
        readonly int[] pending = new int[4];
        // About 30 s of the busiest stream (poses at 20 Hz plus item states): a long scene load must not cost the link.
        const int MaxPending = 4096;
        TcpListener listener;
        volatile bool disposed;
        public bool Hosting { get; private set; }
        public int Port { get; private set; }

        void Emit(int peer, string kind, byte[] data, string message)
        {
            if (disposed) return;
            // Only data packets count against the cap; dropping a "disconnected" event would leave a ghost peer.
            if (kind == "packet" && peer >= 0 && peer < pending.Length && Interlocked.Increment(ref pending[peer]) > MaxPending)
            {
                Interlocked.Decrement(ref pending[peer]);
                Drop(peer, null, MaxPending + " packets were waiting for the game (it stalled)");
                return;
            }
            lock (events) events.Enqueue(new NetworkEvent { Peer = peer, Kind = kind, Data = data, Message = message });
        }

        public bool Poll(out NetworkEvent e)
        {
            if (OnSteam && !disposed) PumpSteam();
            lock (events)
            {
                if (events.Count == 0) { e = null; return false; }
                e = events.Dequeue();
            }
            if (e.Kind == "packet" && e.Peer >= 0 && e.Peer < pending.Length) Interlocked.Decrement(ref pending[e.Peer]);
            return true;
        }

        // A full game tells the joiner why, instead of closing on it.
        static void RefuseFull(TcpClient client)
        {
            try
            {
                client.SendTimeout = 2000;
                byte[] packet = Wire.Packet(Op.Reject, w => Wire.Text(w, FullMessage));
                byte[] header = { (byte)packet.Length, (byte)(packet.Length >> 8), (byte)(packet.Length >> 16), (byte)(packet.Length >> 24) };
                NetworkStream stream = client.GetStream();
                stream.Write(header, 0, 4); stream.Write(packet, 0, packet.Length);
            }
            catch (Exception) { }   // the joiner is refused either way; the close below ends it
            finally { client.Close(); }
        }

        public void Host(int port, bool loopback)
        {
            if (listener != null || Hosting || disposed) throw new InvalidOperationException("Transport already used.");
            Hosting = true;
            listener = new TcpListener(loopback ? IPAddress.Loopback : IPAddress.Any, port);
            listener.Start(4);
            Port = ((IPEndPoint)listener.LocalEndpoint).Port;
            StartThread(delegate
            {
                while (!disposed)
                {
                    TcpClient client;
                    try { client = listener.AcceptTcpClient(); }
                    catch (Exception ex)
                    {
                        if (disposed) break;
                        // One failed accept (a joiner that reset at once) must not end hosting for everyone else.
                        Emit(-1, "log", null, "Accept failed: " + ex.Message);
                        Thread.Sleep(200);
                        continue;
                    }
                    AcceptClient(client);
                }
            });
        }

        public void Join(string address, int port)
        {
            if (disposed) throw new ObjectDisposedException("Transport");
            StartThread(delegate
            {
                TcpClient client = new TcpClient();
                try
                {
                    IAsyncResult attempt = client.BeginConnect(address, port, null, null);
                    if (!attempt.AsyncWaitHandle.WaitOne(5000)) throw new IOException("Connection timed out.");
                    client.EndConnect(attempt);
                    lock (gate)
                    {
                        if (disposed) { client.Close(); return; }
                        Attach(0, client);
                    }
                }
                catch (Exception ex) { client.Close(); Emit(0, "error", null, ex.Message); }
            });
        }

        void AcceptClient(TcpClient client)
        {
            bool full = false;
            lock (gate)
            {
                int id = 1;
                while (links.ContainsKey(id)) id++;
                if (disposed) { client.Close(); return; }
                if (id > MaxPlayers - 1) full = true;
                else
                {
                    try { Attach(id, client); }
                    catch (Exception ex) { links.Remove(id); client.Close(); Emit(-1, "log", null, "A joiner's connection failed: " + ex.Message); }
                }
            }
            if (full) RefuseFull(client);
        }

        void Attach(int id, TcpClient client)
        {
            client.NoDelay = true;
            // Liveness comes from the writer thread's keepalives, not the game loop: a 20 s scene load must not drop anyone.
            client.ReceiveTimeout = 30000;
            client.SendTimeout = 10000;
            Link link = new Link(this, id, client);
            links.Add(id, link);
            Emit(id, "connected", null, null);
            link.Start();
        }

        public void Send(int id, byte[] packet)
        {
            if (packet == null || packet.Length == 0 || packet.Length > MaxFrame) throw new ArgumentException("Invalid packet size.");
            if (OnSteam) { if (!disposed) SendSteam(id, packet, false); return; }
            lock (gate) { Link link; if (links.TryGetValue(id, out link)) link.Send(packet); }
        }

        public void Drop(int id) { Drop(id, null, null); }

        public void SendAndClose(int id, byte[] packet)
        {
            if (packet == null || packet.Length < 1 || packet.Length > MaxFrame) throw new ArgumentException("Invalid final packet size.");
            if (OnSteam) { if (!disposed) SendSteam(id, packet, true); return; }
            lock (gate) { Link link; if (links.TryGetValue(id, out link)) link.SendAndClose(packet); }
        }

        // reason: why the link ended, for the log (null for a normal close).
        void Drop(int id, Link expected, string reason)
        {
            if (OnSteam) { if (!disposed) DropSteam(id, reason); return; }
            lock (gate)
            {
                Link link;
                if (!links.TryGetValue(id, out link)) return;
                if (expected != null && link != expected) return;
                links.Remove(id);
                link.Close();
                Emit(id, "disconnected", null, reason);
            }
        }

        public void Dispose()
        {
            lock (gate)
            {
                if (disposed) return;
                disposed = true;
                if (listener != null) listener.Stop();
                DisposeSteam();
                foreach (Link link in links.Values) link.Close();
                links.Clear();
                lock (events) events.Clear();
            }
        }

        static void StartThread(ThreadStart action) { new Thread(action) { IsBackground = true, Name = "RickAndMortyTogether network" }.Start(); }

        sealed class Link
        {
            // A zero-length frame: keeps the peer's receive timeout from firing while the game itself is busy loading.
            static readonly byte[] keepalive = { 0, 0, 0, 0 };   // process-scoped (a constant)
            readonly Transport owner;
            readonly int id;
            readonly TcpClient client;
            readonly NetworkStream stream;
            readonly Queue<byte[]> outgoing = new Queue<byte[]>();
            readonly Queue<long> due = new Queue<long>();
            readonly System.Diagnostics.Stopwatch clock = System.Diagnostics.Stopwatch.StartNew();
            readonly Random jitter = new Random();
            long lastDue;
            int queuedBytes;
            readonly AutoResetEvent wake = new AutoResetEvent(false);
            volatile bool closed;
            volatile bool closeAfterSend;
            public Link(Transport owner, int id, TcpClient client)
            { this.owner = owner; this.id = id; this.client = client; stream = client.GetStream(); }
            public void Start() { StartThread(Read); StartThread(Write); }
            public void Close() { closed = true; client.Close(); wake.Set(); }
            public void Send(byte[] packet)
            {
                if (closed || closeAfterSend) return;
                lock (outgoing)
                {
                    // A link that cannot take 4 MB for many seconds is dropped rather than buffering without limit.
                    if (queuedBytes > 4 * 1024 * 1024) { owner.Drop(id, this, "4 MB could not be sent (the connection stalled)"); return; }
                    if (LagMs > 0)
                    {
                        long when = clock.ElapsedMilliseconds + LagMs + (JitterMs > 0 ? jitter.Next(JitterMs) : 0);
                        lastDue = Math.Max(lastDue, when);
                        due.Enqueue(lastDue);
                    }
                    outgoing.Enqueue(packet);
                    queuedBytes += packet.Length;
                }
                wake.Set();
            }
            public void SendAndClose(byte[] packet) { Send(packet); closeAfterSend = true; wake.Set(); }
            void Read()
            {
                string reason = null;
                try
                {
                    while (!closed)
                    {
                        byte[] header = ReadExactly(stream, 4);
                        int size = header[0] | header[1] << 8 | header[2] << 16 | header[3] << 24;
                        if (size == 0) continue;   // keepalive
                        if (size < 0 || size > MaxFrame) throw new IOException("Invalid frame length.");
                        byte[] data = ReadExactly(stream, size);
                        // A packet read just before this link closed must not reach a new link given the same id.
                        if (!closed) owner.Emit(id, "packet", data, null);
                    }
                }
                catch (EndOfStreamException) { if (!closed) reason = "closed by the other side"; }
                catch (Exception ex) { if (!closed) reason = "receive failed: " + ex.GetType().Name + ": " + ex.Message; }
                finally { owner.Drop(id, this, reason); }
            }
            void Write()
            {
                string reason = null;
                long lastWrite = clock.ElapsedMilliseconds;
                try
                {
                    while (!closed)
                    {
                        byte[] packet = null;
                        long wait = 0;
                        lock (outgoing)
                        {
                            if (outgoing.Count > 0)
                            {
                                if (LagMs > 0 && due.Count > 0) wait = due.Peek() - clock.ElapsedMilliseconds;
                                if (wait <= 0)
                                {
                                    if (due.Count > 0) due.Dequeue();
                                    packet = outgoing.Dequeue();
                                    queuedBytes -= packet.Length;
                                }
                            }
                        }
                        if (packet == null)
                        {
                            if (closeAfterSend) break;
                            if (clock.ElapsedMilliseconds - lastWrite > 5000) { stream.Write(keepalive, 0, 4); lastWrite = clock.ElapsedMilliseconds; }
                            wake.WaitOne(wait > 0 ? (int)Math.Min(wait, 50) : 1000);
                            continue;
                        }
                        int size = packet.Length;
                        byte[] header = { (byte)size, (byte)(size >> 8), (byte)(size >> 16), (byte)(size >> 24) };
                        stream.Write(header, 0, 4); stream.Write(packet, 0, size);
                        lastWrite = clock.ElapsedMilliseconds;
                    }
                }
                catch (Exception ex) { if (!closed) reason = "send failed: " + ex.GetType().Name + ": " + ex.Message; }
                finally { owner.Drop(id, this, reason); }
            }
        }

        public static byte[] ReadExactly(Stream stream, int size)
        {
            byte[] data = new byte[size];
            int offset = 0;
            while (offset < size)
            {
                int read = stream.Read(data, offset, size - offset);
                if (read == 0) throw new EndOfStreamException();
                offset += read;
            }
            return data;
        }
    }
}
