using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

public class SocketsUDPServer : MonoBehaviour
{
    public int port = 9050;
    public string serverName = "MyServer";
    public bool autoStart = true;
    public int maxPlayers = 3;

    private bool startRequested = false;
    private bool gameStarted = false;

    const int MaxPacketSize = 64 * 1024;

    struct Packet { public byte[] data; public EndPoint from; }

    Socket m_socket;                                                       // the only socket
    readonly HashSet<EndPoint> m_knownClients = new HashSet<EndPoint>();   // your client list
    readonly Dictionary<EndPoint, string> m_playerNames = new Dictionary<EndPoint, string>();
    readonly Dictionary<EndPoint, float> m_lastHeard = new Dictionary<EndPoint, float>();   // last time each client wrote
    readonly List<Thread> m_threads = new List<Thread>();
    readonly ConcurrentQueue<Packet> m_inbox = new ConcurrentQueue<Packet>();
    readonly List<string> m_log = new List<string>();
    readonly HashSet<int> m_warned = new HashSet<int>();
    volatile bool m_running;

    //WorldReplication
    const byte m_prefabType = 0;
    [SerializeField] GameObject[] m_prefabs; //[0] has to be player
    [SerializeField] Vector3[] m_spawnPositions = { new Vector3(-2f, 1f, 0f), new Vector3(2f, 1f, 0f), new Vector3(0f, 1f, 2f) };
    LinkingContext m_linking = new LinkingContext();
    ReplicationManagerServer m_replication;
    readonly Dictionary<EndPoint, int> m_clientNetId = new Dictionary<EndPoint, int>();   
    bool m_worldReady;

    public Transform m_cube;
    public Transform m_clientCube;
    private float m_sendPlayerDataTimer;
    [SerializeField] private float m_secondsBetweenUpdate = 0.1f;

    //Timeout
    [SerializeField] private float m_clientTimeout = 5f;   // nothing from a client in this time: remove it
    private float m_timeoutCheckTimer;

    public bool IsRunning { get { return m_running; } }
    public int ClientCount { get { lock (m_knownClients) return m_knownClients.Count; } }

    void Start()
    {
        Application.runInBackground = true;
        ParseCommandLine();
        if (autoStart) StartNetwork();
    }

    public void StartNetwork()
    {
        if (m_running) return;
        m_running = true;
        StartThread(ServerThread);
    }

    public void Disconnect()
    {
        if (!m_running) return;
        m_running = false;

        // Closing the socket unblocks the thread waiting in ReceiveFrom() (slide 14).
        // No Shutdown() here: UDP has no connection to shut down.
        if (m_socket != null) { try { m_socket.Close(); } catch { } m_socket = null; }

        lock (m_knownClients)
        {
            m_knownClients.Clear();
            m_playerNames.Clear();
            m_lastHeard.Clear();
        }

        Reset_();

        gameStarted = false;
        startRequested = false;

        Thread[] threads;
        lock (m_threads) { threads = m_threads.ToArray(); m_threads.Clear(); }
        foreach (Thread t in threads) if (t != Thread.CurrentThread) t.Join(500);

        Log("[SERVER] Stopped");
    }

    void Awake()
    {
        DontDestroyOnLoad(gameObject);

        Reset_();

        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    void OnDestroy()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        Disconnect();
    }
    private void Reset_()
    {
        m_linking = new LinkingContext();
        m_replication = new ReplicationManagerServer(m_linking);

        m_clientNetId.Clear();
        m_worldReady = false;
        m_sendPlayerDataTimer = 0f;
    }
    void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (scene.name != "GameScene")
            return;

        Reset_();

        List<EndPoint> clients;
        lock (m_knownClients) { clients = new List<EndPoint>(m_playerNames.Keys); }

        for (int i = 0; i < clients.Count; i++)
        {
            GameObject player = SpawnPlayer(GetSpawnPosition(i));
            if (player == null) continue;

            int netId = player.GetComponent<NetObject>().id;
            m_clientNetId[clients[i]] = netId;

            SendString("YOUR_ID:" + netId, clients[i]);
        }

        m_worldReady = true;
        Debug.Log("[SERVER] GameScene ready, " + m_linking.Count + " network objects");
        //GameObject cubeObject = GameObject.Find("NetworkCube");
        //GameObject externalCubeObject = GameObject.Find("NetworkCube1");

        //if (cubeObject != null)
        //{
        //    m_cube = cubeObject.transform;
        //    m_clientCube = externalCubeObject.transform;

        //    CubeRotate localRotateScript = m_cube.GetComponent<CubeRotate>();
        //    CubeRotate externalRotateScript = m_clientCube.GetComponent<CubeRotate>();

        //    if (localRotateScript != null)
        //    {
        //        localRotateScript.enabled = true;
        //    }
        //    if (externalRotateScript != null)
        //    {
        //        externalRotateScript.enabled = false;
        //    }


        //    Debug.Log("[SERVER] GameScene Cube found!");
        //}
        //else
        //{
        //    m_cube = null;
        //    m_clientCube = null;
        //    Debug.LogError("[SERVER] NetworkCube not found!");
        //}
    }
    Vector3 GetSpawnPosition(int index)
    {
        return m_spawnPositions[index % m_spawnPositions.Length];
    }

    public GameObject SpawnOnServer(byte type, Vector3 position)
    {
        if (m_prefabs == null || type >= m_prefabs.Length || m_prefabs[type] == null)
        {
            Log("[SERVER] No prefab for type " + type);
            return null;
        }

        GameObject gameObject = Instantiate(m_prefabs[type], position, Quaternion.identity);

        NetObject netObject = gameObject.GetComponent<NetObject>();
        netObject.prefabType = type;
        netObject.id = m_linking.Register(gameObject);
        m_replication.MarkCreate(netObject.id);

        return gameObject;
    }
    GameObject SpawnPlayer(Vector3 position)
    {
        GameObject gameObject = SpawnOnServer(m_prefabType, position);

        CubeRotate rotate = gameObject.GetComponent<CubeRotate>();
        rotate.enabled = false;

        Rigidbody rb = gameObject.GetComponent<Rigidbody>();
        rb.isKinematic = true; 

        return gameObject;
    }

    public void DestroyOnServer(int id)
    {
        GameObject gameObject = m_linking.Find(id);
        m_linking.Unregister(id);
        m_replication.MarkDestroy(id);
        if (gameObject != null) 
        {
            Destroy(gameObject);
        }

    }
    void Update()
    {
        Packet packet;
        while (m_inbox.TryDequeue(out packet))
        {
            OnPacketReceived(packet.data, packet.from);
        }
        if (startRequested && !gameStarted && m_running && m_socket != null)
        {
            startRequested = false;
            gameStarted = true;

            Broadcast("START_THE_GAME");

            Debug.Log("[SERVER] Starting GameScene");

            SceneManager.LoadScene("GameScene");
        }
        if (m_worldReady)
        {
            m_sendPlayerDataTimer += Time.deltaTime;

            if (m_sendPlayerDataTimer >= m_secondsBetweenUpdate)
            {
                m_sendPlayerDataTimer = 0f;
                Tick();
            }
        }


        m_timeoutCheckTimer += Time.deltaTime;

        if (m_timeoutCheckTimer >= 0.5f)
        {
            m_timeoutCheckTimer = 0f;
            CheckTimeouts();
        }
    }
    void Tick()
    {
        foreach (KeyValuePair<int, GameObject> keyValuePair in m_linking.Objects) 
        {
            m_replication.MarkUpdate(keyValuePair.Key);
        }


        byte[] packet = m_replication.WritePacket();
        if (packet != null) BroadcastPacket(packet);
    }
    void ServerThread()
    {
        try { m_socket = StartServer(); }                                            // TODO 1
        catch (SocketException e)
        {
            Log("[SERVER] Could not start: " + e.SocketErrorCode +
                (e.SocketErrorCode == SocketError.AddressAlreadyInUse
                    ? " (port " + port + " is still used by another instance, slide 34)" : ""));
            return;
        }

        if (m_socket == null) { WarnTodo(1, "StartServer() returned null"); return; }
        Log("[SERVER] Listening on " + port);

        ReceiveLoop();
    }

    // Sends one datagram. Note the difference with TCP: you must say WHERE every single time.
    public void SendPacket(byte[] payload, EndPoint to)
    {
        if (m_socket == null || to == null || payload == null) return;

        try
        {
            int sent = SendRaw(m_socket, payload, to);                               
            if (sent < 0) WarnTodo(2, "SendRaw() is not implemented yet");
        }
        catch (SocketException e) { Log("[SERVER] Send failed: " + e.SocketErrorCode); }
        catch (ObjectDisposedException) { }
    }

    public void SendString(string text, EndPoint to)
    {
        SendPacket(Serializer.SerializeText(text), to);
    }

    void ReceiveLoop()
    {
        byte[] buffer = new byte[MaxPacketSize];

        while (m_running)
        {
            // A blank endpoint: ReceiveFrom OVERWRITES it with the address of whoever sent the
            // datagram. That is how a UDP server learns where to reply (slides 9 and 10).
            EndPoint from = new IPEndPoint(IPAddress.Any, 0);

            int received;
            try { received = ReceiveRaw(m_socket, buffer, ref from); }                
            catch (SocketException e)
            {
                // Windows reports "a previous SendTo reached a closed port" as an error on receive.
                // Not our problem: keep serving.
                if (e.SocketErrorCode == SocketError.ConnectionReset) continue;
                if (m_running) Log("[SERVER] Receive failed: " + e.SocketErrorCode);
                return;
            }
            catch (ObjectDisposedException) { return; }

            if (received < 0) { WarnTodo(3, "ReceiveRaw() is not implemented yet"); return; }
            if (received == 0) continue;

            byte[] payload = new byte[received];
            Array.Copy(buffer, payload, received);

            m_inbox.Enqueue(new Packet { data = payload, from = from });
        }
    }

    void StartThread(ThreadStart work)
    {
        Thread t = new Thread(work);
        t.IsBackground = true;
        lock (m_threads) m_threads.Add(t);
        t.Start();
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

    void OnGUI()
    {
        GUILayout.BeginArea(new Rect(10, 10, Screen.width - 20, Screen.height - 20));

        GUILayout.Label("UDP SERVER   port " + port);

        GUILayout.Space(10);

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
            GUILayout.Label("Clients joined: " + ClientCount);

            GUILayout.Space(10);

            if (m_running && !gameStarted && GUILayout.Button("START GAME", GUILayout.Width(170), GUILayout.Height(40)))
            {
                startRequested = true;
            }

            GUILayout.Space(10);

            if (GUILayout.Button("Stop Server", GUILayout.Width(150)))
            {
                Disconnect();
            }
        }
              
        GUILayout.Space(10);

        lock (m_log)
        {
            for (int i = Mathf.Max(0, m_log.Count - 5); i < m_log.Count; i++)
            {
                GUILayout.Label(m_log[i]);
            }
        }

        GUILayout.EndArea();
    }

    Socket StartServer()
    {
        Socket socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);

        socket.Bind(new IPEndPoint(IPAddress.Any, port));

        return socket;   
    }

    int SendRaw(Socket socket, byte[] data, EndPoint to)
    {
        return socket.SendTo(data, to); 
    }


    int ReceiveRaw(Socket socket, byte[] buffer, ref EndPoint from)
    {
        return socket.ReceiveFrom(buffer, ref from);  
    }


    
    void Broadcast(string message)
    {
        EndPoint[] targets;

        lock (m_knownClients)
        {
            targets = new List<EndPoint>(m_knownClients).ToArray();
        }

        foreach (EndPoint target in targets)
        {
            SendString(message, target);
        }
    }

    void BroadcastPacket(byte[] packet)
    {
        EndPoint[] targets;

        lock (m_knownClients)
        {
            targets = new List<EndPoint>(m_knownClients).ToArray();
        }
        foreach (EndPoint target in targets)
        {
            SendPacket(packet, target);
        }
    }

    void BroadcastPlayerList()
    {
        string[] names;

        lock (m_knownClients)
        {
            names = new List<string>(m_playerNames.Values).ToArray();
        }

        string message ="PLAYERS:" + string.Join(",", names);

        Broadcast(message);

        BroadcastRoomInfo();
    }

    void BroadcastRoomInfo()
    {
        int currentPlayers;

        lock (m_knownClients)
        {
            currentPlayers = m_playerNames.Count;
        }

        string message = "ROOM:" + currentPlayers + "/" + Mathf.Max(1, maxPlayers);

        Broadcast(message);
    }

    void BroadcastMessage(string player, string message) 
    {
        string text = "CHAT:" + player + ": " + message;
        EndPoint[] targets;

        lock(m_knownClients)
        {
            targets = m_knownClients.ToArray();

        }
        foreach (EndPoint target in targets)
        {
            SendString(text, target);
        }

    }
    void BroadcastPlayerVars(int id_)
    {
        Vector3 rotation = m_cube.transform.rotation.eulerAngles;
        Vector3 position = m_cube.transform.position;

        PlayerVarsDTO dto = new PlayerVarsDTO
        {

            Posx = position.x,
            Posy = position.y,
            Posz = position.z,

            Rotx = rotation.x,
            Roty = rotation.y,
            Rotz = rotation.z
        };

        byte[] packet = Serializer.Serialize(dto);
        BroadcastPacket(packet);
    }
    void OnPacketReceived(byte[] data, EndPoint from)
    {
        string text = Serializer.DeserializeText(data);
        MsgType type = Serializer.GetMessageType(data);

        // Any packet from a known client means it is still there
        lock (m_knownClients)
        {
            if (m_knownClients.Contains(from))
                m_lastHeard[from] = Time.realtimeSinceStartup;
        }

        switch (type)
        {
            case MsgType.Text:
                if (text.StartsWith("PING:"))
                    return;

                Log("[SERVER] Received: " + text + " from " + from);
                if (text.StartsWith("READY:"))
                {
                    int readyId;
                    if (m_clientNetId.TryGetValue(from, out readyId)) 
                    {
                        SendString("YOUR_ID:" + readyId, from);
                    }

                    return;
                }
                if (text.StartsWith("LEAVE:"))
                {
                    RemoveClient(from, "left");
                    return;
                }

                if (text.StartsWith("JOIN:"))
                {
                    string playerName = text.Substring("JOIN:".Length).Trim();

                    if (string.IsNullOrEmpty(playerName))
                        return;

                    bool roomFull;
                    bool isNewClient = false;

                    lock (m_knownClients)
                    {
                        bool alreadyJoined = m_playerNames.ContainsKey(from);

                        roomFull = !alreadyJoined && m_playerNames.Count >= Mathf.Max(1, maxPlayers);

                        if (!roomFull)
                        {
                            isNewClient = m_knownClients.Add(from);
                            m_playerNames[from] = playerName;
                            m_lastHeard[from] = Time.realtimeSinceStartup;
                        }
                    }
                    if (roomFull)
                    {
                        Log("[SERVER] Room is full. Rejected: " + playerName);
                        SendString("FULL:Room is full", from);
                        return;
                    }

                    if (isNewClient)
                    {
                        Log("[SERVER] New client: " + from);
                    }

                    Log("[SERVER] Player joined: " + playerName);

                    BroadcastPlayerList();
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
                break;

                case MsgType.Player:
                {
      
                    int netId;
                    if (!m_clientNetId.TryGetValue(from, out netId)) 
                    break;

                    GameObject player = m_linking.Find(netId);
                    if (player == null) break;

                    PlayerVarsDTO dto = Serializer.DeserializePlayerVars(data);
                    player.transform.SetPositionAndRotation
                        (
                        new Vector3(dto.Posx, dto.Posy, dto.Posz),
                        Quaternion.Euler(dto.Rotx, dto.Roty, dto.Rotz)
                        );

                    break;
                }

            default:

                break;
        }

        


    }

    // Removes every client we have not heard from in m_clientTimeout seconds
    // (closed window or crash: no LEAVE arrives)
    void CheckTimeouts()
    {
        float now = Time.realtimeSinceStartup;
        List<EndPoint> expired = new List<EndPoint>();

        lock (m_knownClients)
        {
            foreach (KeyValuePair<EndPoint, float> entry in m_lastHeard)
            {
                if (now - entry.Value > m_clientTimeout)
                    expired.Add(entry.Key);
            }
        }

        foreach (EndPoint client in expired)
        {
            RemoveClient(client, "timed out");
        }
    }

    void RemoveClient(EndPoint client, string reason)
    {
        string playerName;
        bool removed;

        lock (m_knownClients)
        {
            removed = m_knownClients.Remove(client);
            m_playerNames.TryGetValue(client, out playerName);
            m_playerNames.Remove(client);
            m_lastHeard.Remove(client);
        }

        if (!removed)
            return;

        int netId;
        if (m_clientNetId.TryGetValue(client, out netId))
        {
            DestroyOnServer(netId);
            m_clientNetId.Remove(client);
        }

        Log("[SERVER] Player " + reason + ": " + playerName + " (" + client + ")");

        BroadcastPlayerList();
    }

}