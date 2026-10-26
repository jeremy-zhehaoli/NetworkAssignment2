using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;

public class SocketsTCPServer : MonoBehaviour
{
    public int port = 9050;
    public string serverName = "MyServer";
    public bool autoStart = true;
    public int maxPlayers = 3;

    const int MaxPacketSize = 64 * 1024;

    struct Packet { public byte[] data; public Socket from; }

    Socket m_listener;                                        // only accepts connections
    readonly List<Socket> m_clients = new List<Socket>();     // one socket per connected client

    readonly Dictionary<Socket, string> m_playerNames = new Dictionary<Socket, string>();

    readonly List<Thread> m_threads = new List<Thread>();
    readonly ConcurrentQueue<Packet> m_inbox = new ConcurrentQueue<Packet>();
    readonly List<string> m_log = new List<string>();
    readonly HashSet<int> m_warned = new HashSet<int>();
    volatile bool m_running;

    //Cube
    public Transform m_cube;
    private float m_rotationTimer;
    [SerializeField]private float m_secondsBetweenUpdate = 0.1f;
    public bool IsRunning { get { return m_running; } }

    void Start()
    {
        Application.runInBackground = true;   // or the window without focus is paused
        ParseCommandLine();
        if (autoStart) StartNetwork();
    }

    public void StartNetwork()
    {
        if (m_running) return;
        m_running = true;
        StartThread(ServerThread);
    }

    // Closing the sockets is what unblocks the threads waiting in Accept() or Receive() 
    public void Disconnect()
    {
        if (!m_running) return;
        m_running = false;

        Socket[] clients;
        lock (m_clients) { clients = m_clients.ToArray(); m_clients.Clear(); }
        foreach (Socket c in clients) CloseSocket(c);

        CloseSocket(m_listener); m_listener = null;

        Thread[] threads;
        lock (m_threads) { threads = m_threads.ToArray(); m_threads.Clear(); }
        foreach (Thread t in threads) if (t != Thread.CurrentThread) t.Join(500);

        Log("[SERVER] Stopped");
    }

    void OnDestroy() { Disconnect(); }

    // Main thread: the only safe place to touch the Unity API.
    void Update()
    {
        Packet packet;
        while (m_inbox.TryDequeue(out packet))
        {
            if (packet.data == null)
            {
                bool removed;
                lock (m_playerNames)
                {
                    removed = m_playerNames.Remove(packet.from);
                }

                if (removed)
                {
                    BroadcastPlayerList();
                }
            }
            else
            {
                OnPacketReceived(packet.data, packet.from);
            }
        }
        m_rotationTimer += Time.deltaTime;

        if (m_rotationTimer >= m_secondsBetweenUpdate) 
        {
            m_rotationTimer = 0f;
            BroadcastRotation();
        }
    }

    void ServerThread()
    {
        try { m_listener = StartServer(); }                                         
        catch (SocketException e)
        {
            Log("[SERVER] Could not start: " + e.SocketErrorCode +
                (e.SocketErrorCode == SocketError.AddressAlreadyInUse
                    ? " (port " + port + " is still used by another instance, slide 34)" : ""));
            return;
        }

        if (m_listener == null) { WarnTodo(1, "StartServer() returned null"); return; }
        Log("[SERVER] Listening on " + port);

        while (m_running)
        {
            Socket client;
            try { client = AcceptClient(); }                                        
            catch (SocketException) { break; }
            catch (ObjectDisposedException) { break; }

            if (client == null) { WarnTodo(2, "AcceptClient() returned null"); return; }

            lock (m_clients) m_clients.Add(client);
            Log("[SERVER] Client connected: " + client.RemoteEndPoint);

            Socket captured = client;
            StartThread(delegate { ClientThread(captured); });
        }
    }

    // One of these per connected client: in TCP every client has its own socket 
    void ClientThread(Socket client)
    {
        ReceiveLoop(client);
        lock (m_clients)
        { 
            m_clients.Remove(client); 
        }
        m_inbox.Enqueue(new Packet
        {
            data = null,
            from = client
        });

        Log("[SERVER] Client disconnected");
        CloseSocket(client);
    }

    // Sends one packet to one client. Lab 3 will call it with serialized data instead of text.
    public void SendPacket(byte[] payload, Socket to)
    {
        if (to == null || payload == null) return;

        byte[] framed = new byte[4 + payload.Length];
        BitConverter.GetBytes(payload.Length).CopyTo(framed, 0);
        payload.CopyTo(framed, 4);

        try
        {
            int sent = SendRaw(to, framed);                                          
            if (sent < 0) WarnTodo(3, "SendRaw() is not implemented yet");
        }
        catch (SocketException e) { Log("[SERVER] Send failed: " + e.SocketErrorCode); }
        catch (ObjectDisposedException) { }
    }

    public void SendString(string text, Socket to)
    {
        SendPacket(Encoding.UTF8.GetBytes(text), to);
    }

    void ReceiveLoop(Socket socket)
    {
        byte[] header = new byte[4];
        while (m_running)
        {
            if (!ReadExactly(socket, header, 4)) return;

            int size = BitConverter.ToInt32(header, 0);
            if (size <= 0 || size > MaxPacketSize) { Log("[SERVER] Invalid packet size: " + size); return; }

            byte[] payload = new byte[size];
            if (!ReadExactly(socket, payload, size)) return;

            m_inbox.Enqueue(new Packet { data = payload, from = socket });
        }
    }

    // One Receive() can return less than you asked for, so we insist until we have it all.
    bool ReadExactly(Socket socket, byte[] buffer, int count)
    {
        int total = 0;
        while (total < count)
        {
            int read;
            try { read = ReceiveRaw(socket, buffer, total, count - total); }         
            catch (SocketException e) { Log("[SERVER] Connection lost: " + e.SocketErrorCode); return false; }
            catch (ObjectDisposedException) { return false; }

            if (read < 0) { WarnTodo(4, "ReceiveRaw() is not implemented yet"); return false; }
            if (read == 0) return false;   // 0 bytes = the client closed the connection (slide 8)
            total += read;
        }
        return true;
    }

    void StartThread(ThreadStart work)
    {
        Thread t = new Thread(work);
        t.IsBackground = true;
        lock (m_threads) m_threads.Add(t);
        t.Start();
    }

    void CloseSocket(Socket socket)
    {
        if (socket == null) return;
        try { socket.Shutdown(SocketShutdown.Both); } catch { }
        try { socket.Close(); } catch { }
    }

    public void Log(string message)
    {
        Debug.Log(message);
        lock (m_log) { m_log.Add(message); if (m_log.Count > 100) m_log.RemoveAt(0); }
    }

    void WarnTodo(int number, string detail)
    {
        lock (m_warned) { if (!m_warned.Add(number)) return; }
        Log("[TODO " + number + "] not done yet: " + detail);
    }

    void ParseCommandLine()
    {
        string[] args = Environment.GetCommandLineArgs();
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "-port" && i + 1 < args.Length) int.TryParse(args[++i], out port);
            else if (args[i] == "-name" && i + 1 < args.Length) serverName = args[++i];
            else if (args[i] == "-autostart") autoStart = true;
            else if (args[i] == "-noautostart") autoStart = false;
        }
    }

    // Temporary debug view, so you can also see what happens in a build.
    // In the deliverable you replace it with your own scenes and UI.
    void OnGUI()
    {
        GUILayout.BeginArea(new Rect(10, 10, 450, 400));

        GUILayout.Label("TCP SERVER   port " + port);

        if (!m_running)
        {
            if (GUILayout.Button("Create Game", GUILayout.Width(150)))
            {
                StartNetwork();
            }
        }
        else
        {
            GUILayout.Label("WAITING ROOM");
            GUILayout.Label("Waiting for players...");

            if (GUILayout.Button("Stop Server", GUILayout.Width(150)))
            {
                Disconnect();
            }
        }

        GUILayout.Space(10);

        lock (m_log)
        {
            for (int i = Mathf.Max(0, m_log.Count - 10);
                 i < m_log.Count; i++)
            {
                GUILayout.Label(m_log[i]);
            }
        }

        GUILayout.EndArea();
    }

    Socket StartServer()
    {
        Socket socket = new Socket(
            AddressFamily.InterNetwork,
            SocketType.Stream,
            ProtocolType.Tcp);

        socket.Bind(
            new IPEndPoint(IPAddress.Any, port));

        socket.Listen(10);
        return socket;  
    }

    Socket AcceptClient()
    {
        return m_listener.Accept();  
    }

    int SendRaw(Socket socket, byte[] data)
    {
        return socket.Send(data);   
    }

    int ReceiveRaw(Socket socket, byte[] buffer, int offset, int count)
    {
        return socket.Receive(buffer, offset, count, SocketFlags.None);   
    }


    void BroadcastPlayerList()
    {
        Socket[] targets;
        string[] names;

        lock (m_playerNames)
        {
            targets = new List<Socket>(m_playerNames.Keys).ToArray();

            names = new List<string>(m_playerNames.Values).ToArray();
        }

        string message =
            "PLAYERS:" + string.Join(",", names);

        foreach (Socket target in targets)
        {
            SendString(message, target);
        }

        BroadcastRoomInfo();
    }

    void BroadcastRoomInfo()
    {
        Socket[] targets;
        int currentPlayers;

        lock (m_playerNames)
        {
            currentPlayers = m_playerNames.Count;
            targets = new List<Socket>(m_playerNames.Keys).ToArray();
        }

        string message = "ROOM:" + currentPlayers + "/" + Mathf.Max(1, maxPlayers);

        foreach (Socket target in targets)
        {
            SendString(message, target);
        }
    }

    void BroadcastMessage(string player, string message)
    {
        string text = "CHAT:" + player + ": " + message;
        Socket[] targets;
        lock (m_clients)
        {
            targets = m_clients.ToArray();
        }

        foreach (Socket c in targets)
        {
            SendString(text, c);
        }

    }

    void BroadcastRotation() 
    {
        Vector3 rotation = m_cube.transform.rotation.eulerAngles;
        string text = "ROT:" + rotation.x + "," + rotation.y + "," + rotation.z; ;
        Socket[] targets;
        lock (m_clients)
        {
            targets = m_clients.ToArray();
        }

        foreach (Socket c in targets)
        {
            SendString(text, c);
        }
    }

    void HandleJoin(Socket socket, string playerName)
    {
        if (string.IsNullOrWhiteSpace(playerName))
            return;

        bool roomFull = false;

        lock (m_playerNames)
        {
            bool alreadyJoined = m_playerNames.ContainsKey(socket);

            if (!alreadyJoined && m_playerNames.Count >= Mathf.Max(1, maxPlayers))
            {
                roomFull = true;
            }
            else
            {
                m_playerNames[socket] = playerName;
            }
        }

        if (roomFull)
        {
            Log("[SERVER] Room is full. Rejected: " + playerName);

            SendString("FULL:Room is full", socket);

            CloseSocket(socket);
            return;
        }

        Log("[SERVER] Player joined: " + playerName);

        BroadcastPlayerList();
    }
     
    void OnPacketReceived(byte[] data, Socket from)
    {
        string text = Encoding.UTF8.GetString(data);
        Log("[SERVER] Received: " + text);

        if (text.StartsWith("JOIN:"))
        {
            string playerName = text.Substring("JOIN:".Length).Trim();

            HandleJoin(from, playerName);
        }
        else if (text.StartsWith("CHAT:")) 
        {
            string message = text.Substring("CHAT:".Length).Trim();

            string playerName = "ERRORNoNameFound";


            if (m_playerNames.TryGetValue(from, out string name)) 
            {
                playerName = name;
            }

            BroadcastMessage(playerName, message);
        }
    }
}
