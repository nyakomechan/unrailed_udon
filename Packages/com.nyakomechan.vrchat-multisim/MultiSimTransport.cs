#if UNITY_EDITOR
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace MultiSim
{
    internal enum TransportEventType
    {
        Connected,
        Disconnected,
        Message,
    }

    internal struct TransportEvent
    {
        public TransportEventType Type;
        public int ConnectionId;
        public string Message;
    }

    /// <summary>
    /// Minimal length-prefixed (4 byte little-endian) JSON-over-TCP transport on localhost.
    /// The ParrelSync original project listens as the host; clones connect as clients.
    /// Reader threads enqueue events which must be drained on the main thread via Poll().
    /// </summary>
    internal class MultiSimTransport : IDisposable
    {
        private const int MaxMessageSize = 16 * 1024 * 1024;

        private class Connection
        {
            public int Id;
            public TcpClient Client;
            public NetworkStream Stream;
            public Thread ReadThread;
            public readonly object SendLock = new object();
            public volatile bool Closed;
        }

        private readonly ConcurrentQueue<TransportEvent> _events = new ConcurrentQueue<TransportEvent>();
        private readonly Dictionary<int, Connection> _connections = new Dictionary<int, Connection>();
        private readonly object _connectionsLock = new object();

        private TcpListener _listener;
        private Thread _acceptThread;
        private volatile bool _running;
        private int _nextConnectionId = 1;

        public bool IsHost { get; private set; }
        public bool IsConnected
        {
            get
            {
                lock (_connectionsLock)
                {
                    return _connections.Count > 0;
                }
            }
        }

        public bool StartHost(int port)
        {
            try
            {
                _listener = new TcpListener(IPAddress.Loopback, port);
                _listener.Start();
            }
            catch (SocketException e)
            {
                MultiSimLog.Error($"Failed to listen on port {port}: {e.Message}. " +
                                  "Is another host instance already running?");
                _listener = null;
                return false;
            }

            IsHost = true;
            _running = true;
            _acceptThread = new Thread(AcceptLoop) { IsBackground = true, Name = "MultiSim Accept" };
            _acceptThread.Start();
            MultiSimLog.Info($"Host listening on 127.0.0.1:{port}");
            return true;
        }

        /// <summary>
        /// Connects to the host on a background thread, retrying until the timeout.
        /// Emits a Connected event on success, or a Disconnected event with ConnectionId -1 on failure.
        /// </summary>
        public void ConnectToHostAsync(int port, float timeoutSeconds)
        {
            IsHost = false;
            _running = true;

            Thread connectThread = new Thread(() =>
            {
                var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(timeoutSeconds);
                while (_running && DateTime.UtcNow < deadline)
                {
                    TcpClient client = new TcpClient();
                    try
                    {
                        client.Connect(IPAddress.Loopback, port);
                        client.NoDelay = true;
                        AddConnection(client);
                        return;
                    }
                    catch (SocketException)
                    {
                        client.Close();
                        Thread.Sleep(500);
                    }
                }

                _events.Enqueue(new TransportEvent
                {
                    Type = TransportEventType.Disconnected,
                    ConnectionId = -1
                });
            })
            {
                IsBackground = true,
                Name = "MultiSim Connect"
            };
            connectThread.Start();
        }

        private void AcceptLoop()
        {
            while (_running)
            {
                TcpClient client;
                try
                {
                    client = _listener.AcceptTcpClient();
                }
                catch (Exception)
                {
                    // Listener stopped.
                    return;
                }

                client.NoDelay = true;
                AddConnection(client);
            }
        }

        private void AddConnection(TcpClient client)
        {
            Connection conn = new Connection
            {
                Client = client,
                Stream = client.GetStream(),
            };

            lock (_connectionsLock)
            {
                conn.Id = _nextConnectionId++;
                _connections[conn.Id] = conn;
            }

            conn.ReadThread = new Thread(() => ReadLoop(conn))
            {
                IsBackground = true,
                Name = $"MultiSim Read {conn.Id}"
            };
            conn.ReadThread.Start();

            _events.Enqueue(new TransportEvent
            {
                Type = TransportEventType.Connected,
                ConnectionId = conn.Id
            });
        }

        private void ReadLoop(Connection conn)
        {
            byte[] header = new byte[4];
            try
            {
                while (_running && !conn.Closed)
                {
                    ReadExact(conn.Stream, header, 4);
                    int length = BitConverter.ToInt32(header, 0);
                    if (length <= 0 || length > MaxMessageSize)
                    {
                        throw new IOException($"Invalid message length {length}");
                    }

                    byte[] body = new byte[length];
                    ReadExact(conn.Stream, body, length);

                    _events.Enqueue(new TransportEvent
                    {
                        Type = TransportEventType.Message,
                        ConnectionId = conn.Id,
                        Message = Encoding.UTF8.GetString(body)
                    });
                }
            }
            catch (Exception)
            {
                // Connection closed or broken; fall through to cleanup.
            }

            CloseConnection(conn.Id, true);
        }

        private static void ReadExact(NetworkStream stream, byte[] buffer, int count)
        {
            int offset = 0;
            while (offset < count)
            {
                int read = stream.Read(buffer, offset, count - offset);
                if (read <= 0)
                {
                    throw new IOException("Connection closed");
                }
                offset += read;
            }
        }

        public void Send(int connectionId, string message)
        {
            Connection conn;
            lock (_connectionsLock)
            {
                if (!_connections.TryGetValue(connectionId, out conn))
                {
                    return;
                }
            }

            byte[] body = Encoding.UTF8.GetBytes(message);
            byte[] header = BitConverter.GetBytes(body.Length);
            try
            {
                lock (conn.SendLock)
                {
                    conn.Stream.Write(header, 0, 4);
                    conn.Stream.Write(body, 0, body.Length);
                }
            }
            catch (Exception)
            {
                CloseConnection(connectionId, true);
            }
        }

        public void Broadcast(string message, int exceptConnectionId = -1)
        {
            List<int> ids;
            lock (_connectionsLock)
            {
                ids = new List<int>(_connections.Keys);
            }

            foreach (int id in ids)
            {
                if (id != exceptConnectionId)
                {
                    Send(id, message);
                }
            }
        }

        /// <summary>Sends to the host. Only valid on clients (which have exactly one connection).</summary>
        public void SendToHost(string message)
        {
            Broadcast(message);
        }

        public bool TryDequeueEvent(out TransportEvent evt)
        {
            return _events.TryDequeue(out evt);
        }

        private void CloseConnection(int connectionId, bool notify)
        {
            Connection conn;
            lock (_connectionsLock)
            {
                if (!_connections.TryGetValue(connectionId, out conn))
                {
                    return;
                }
                _connections.Remove(connectionId);
            }

            if (conn.Closed)
            {
                return;
            }
            conn.Closed = true;

            try { conn.Stream?.Close(); } catch { /* ignore */ }
            try { conn.Client?.Close(); } catch { /* ignore */ }

            if (notify)
            {
                _events.Enqueue(new TransportEvent
                {
                    Type = TransportEventType.Disconnected,
                    ConnectionId = connectionId
                });
            }
        }

        public void Dispose()
        {
            _running = false;

            try { _listener?.Stop(); } catch { /* ignore */ }
            _listener = null;

            List<int> ids;
            lock (_connectionsLock)
            {
                ids = new List<int>(_connections.Keys);
            }
            foreach (int id in ids)
            {
                CloseConnection(id, false);
            }
        }
    }
}
#endif
