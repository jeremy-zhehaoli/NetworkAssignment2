using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;
using UnityEngine.SceneManagement;
using static UnityEngine.GraphicsBuffer;

public class SocketsUDPClient : MonoBehaviour
{
    public string serverIp = "127.0.0.1";
    public int port = 9050;
    public string userName = "Player";
    public bool autoStart = true;

    private bool gameStarted = false;

    // =============================================================================================
    //  GIVEN CODE - nothing to modify until the TODO area
    // =============================================================================================

    const int MaxPacketSize = 64 * 1024;

    Socket m_socket;                                          // the only socket
    EndPoint m_serverEndPoint;                                // where your messages go
    readonly List<Thread> m_threads = new List<Thread>();
    readonly ConcurrentQueue<byte[]> m_inbox = new ConcurrentQueue<byte[]>();
    readonly List<string> m_log = new List<string>();
    readonly HashSet<int> m_warned = new HashSet<int>();
    volatile bool m_running;
    bool m_joinConfirmed = false;
    string m_playerList = "";
    string m_roomInfo = "";
    string m_joinError = "";

    //Chat
    public string messageInput = "";
    List<string> m_chat = new List<string>();


    //World Replication
    [SerializeField] GameObject[] m_prefabs;
    LinkingContext m_linking = new LinkingContext();
    ReplicationManagerClient m_replication;
    int m_PlayerNetId = 0;
    bool m_sceneReady;
    float m_readyTimer;

    [SerializeField] Transform m_localPlayerCube;
    [SerializeField] Transform m_serverNpcCube;
    private float m_sendPlayerDataTimer;
    [SerializeField] private float m_secondsBetweenUpdate = 0.1f;

    //Ping / timeout
    [SerializeField] float m_pingInterval = 1f;     // send PING: every second
    [SerializeField] float m_serverTimeout = 5f;    // nothing from the server in this time: it is gone
    float m_pingTimer;
    float m_lastHeardFromServer;

    public bool IsRunning { get { return m_running; } }

    void Start()
    {
        Application.runInBackground = true;
        ParseCommandLine();
        if (autoStart) StartNetwork();
    }

    public void StartNetwork()
    {
        if (m_running) return;
        m_joinConfirmed = false;
        m_playerList = "";
        m_running = true;
        m_pingTimer = 0f;
        m_lastHeardFromServer = Time.realtimeSinceStartup;

        m_PlayerNetId = 0;

        StartThread(ClientThread);
    }

    public void Disconnect()
    {
        if (!m_running) return;
        m_running = false;
        m_joinConfirmed = false;
        m_roomInfo = "";
        m_playerList = "";

        m_PlayerNetId = 0;

        if (m_socket != null) { try { m_socket.Close(); } catch { } m_socket = null; }
        ResetWorld();
        Thread[] threads;
        lock (m_threads) { threads = m_threads.ToArray(); m_threads.Clear(); }
        foreach (Thread t in threads) if (t != Thread.CurrentThread) t.Join(500);

        Log("[CLIENT] Stopped");
    }

    void Awake()
    {
        DontDestroyOnLoad(gameObject);
        ResetWorld();
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    void OnDestroy()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        Disconnect();
    }
    void ResetWorld()
    {
        foreach (KeyValuePair<int, GameObject> kv in m_linking.Objects)
            if (kv.Value != null) Destroy(kv.Value);

        m_linking = new LinkingContext();
        m_replication = new ReplicationManagerClient(m_linking, m_prefabs);
        m_replication.OnCreated = ApplyOwnership;
        m_localPlayerCube = null;
        m_sceneReady = false;
        m_sendPlayerDataTimer = 0f;
    }
    void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (scene.name != "GameScene")
            return;

        ResetWorld();
        m_sceneReady = true;
        m_readyTimer = 0.5f;

        //GameObject localCubeObj = GameObject.Find("NetworkCube1");
        //GameObject externalCubeObj = GameObject.Find("NetworkCube");

        //if (localCubeObj != null && externalCubeObj != null)
        //{
        //    m_localPlayerCube = localCubeObj.transform;
        //    m_serverNpcCube = externalCubeObj.transform;

        //    CubeRotate localRotateScript = localCubeObj.GetComponent<CubeRotate>();
        //    CubeRotate externalRotateScript = externalCubeObj.GetComponent<CubeRotate>();


        //    if (localRotateScript != null)
        //    {
        //        localRotateScript.enabled = true;
        //    }
        //    if (externalRotateScript!=null)
        //    {
        //        externalRotateScript.enabled = false;
        //    }

        //    Debug.Log("[CLIENT] GameScene Cube found!");
        //}
        //else
        //{
        //    m_localPlayerCube = null;
        //    m_serverNpcCube = null;
        //    Debug.LogError("[CLIENT] NetworkCube not found!");
        //}
    }
    void ApplyOwnership(NetObject netObject)
    {
        
        bool mine = m_PlayerNetId != 0 && netObject.id == m_PlayerNetId;
        netObject.isMine = mine;

        CubeRotate cubeRotate = netObject.GetComponent<CubeRotate>();
        if (cubeRotate != null) cubeRotate.enabled = mine;

        Rigidbody rb = netObject.GetComponent<Rigidbody>();
        if (rb != null) rb.isKinematic = !mine;
        if (mine) m_localPlayerCube = netObject.transform;
    }
    void Update()
    {
        byte[] data;
        while (m_inbox.TryDequeue(out data)) { OnPacketReceived(data); }

        if (m_running)
        {

            if (m_localPlayerCube != null)
            {
                m_sendPlayerDataTimer += Time.deltaTime;

                if (m_sendPlayerDataTimer >= m_secondsBetweenUpdate)
                {
                    m_sendPlayerDataTimer = 0f;
                    SendLocalPlayerState();
                }
            }

            if (m_sceneReady && m_PlayerNetId == 0)
            {
                m_readyTimer += Time.deltaTime;
                if (m_readyTimer >= 0.5f)
                {
                    m_readyTimer = 0f;
                    SendString("READY:");
                }
            }

            // Keep telling the server we are still here
            m_pingTimer += Time.deltaTime;

            if (m_pingTimer >= m_pingInterval)
            {
                m_pingTimer = 0f;
                SendString("PING:");
            }

            // Same trick the other way: if the server stops sending, it is gone
            if (Time.realtimeSinceStartup - m_lastHeardFromServer > m_serverTimeout)
            {
                Log("[CLIENT] Server not responding, disconnected");
                Disconnect();
                m_joinConfirmed = false;
            }
        }
    }

    void ClientThread()
    {
        m_socket = StartClient();                                                    // TODO 1
        if (m_socket == null) { WarnTodo(1, "StartClient() returned null"); return; }

        m_serverEndPoint = ServerEndPoint();                                         // TODO 2
        if (m_serverEndPoint == null) { WarnTodo(2, "ServerEndPoint() returned null"); return; }

        Log("[CLIENT] Ready, server is " + m_serverEndPoint);

        OnStarted();                                                                 // TODO 3
        ReceiveLoop();
    }

    public void SendPacket(byte[] payload)
    {
        if (m_socket == null || m_serverEndPoint == null || payload == null) return;

        try
        {
            int sent = SendRaw(m_socket, payload, m_serverEndPoint);                 // TODO 4
            if (sent < 0) WarnTodo(4, "SendRaw() is not implemented yet");
        }
        catch (SocketException e) { Log("[CLIENT] Send failed: " + e.SocketErrorCode); }
        catch (ObjectDisposedException) { }
    }

    public void SendString(string text)
    {
        SendPacket(Serializer.SerializeText(text));
    }
    public void SendChat()
    {
        SendString("CHAT:" + messageInput);
        messageInput = "";
    }
    void ReceiveLoop()
    {
        byte[] buffer = new byte[MaxPacketSize];

        while (m_running)
        {
            // Blank endpoint: ReceiveFrom fills it with the sender. Here it is always the server,
            // so you do not really need it, but the API asks for it anyway (slide 10).
            EndPoint from = new IPEndPoint(IPAddress.Any, 0);

            int received;
            try { received = ReceiveRaw(m_socket, buffer, ref from); }                // TODO 5
            catch (SocketException e)
            {
                // Windows reports "nobody is listening there" as an error on receive, which is the
                // closest thing UDP has to "the server is down".
                if (e.SocketErrorCode == SocketError.ConnectionReset)
                {
                    Log("[CLIENT] No server on " + m_serverEndPoint + " (slide 34)");
                    return;
                }
                if (m_running) Log("[CLIENT] Receive failed: " + e.SocketErrorCode);
                return;
            }
            catch (ObjectDisposedException) { return; }

            if (received < 0) { WarnTodo(5, "ReceiveRaw() is not implemented yet"); return; }
            if (received == 0) continue;

            byte[] payload = new byte[received];
            Array.Copy(buffer, payload, received);

            m_inbox.Enqueue(payload);
        }
    }

    void OnPacketReceived(byte[] data)
    {

        m_lastHeardFromServer = Time.realtimeSinceStartup;

        MsgType type = Serializer.GetMessageType(data);
        switch (type) 
        {
            case MsgType.Text:
                string text = Serializer.DeserializeText(data);
                if (text == "START_THE_GAME")
                {
                    if (!gameStarted)
                    {
                        gameStarted = true;

                        Debug.Log("[CLIENT] Starting GameScene");

                        SceneManager.LoadScene("GameScene");
                    }

                    return;
                }
                if (text.StartsWith("YOUR_ID:"))
                {
                    int myId;
                    if (int.TryParse(text.Substring("YOUR_ID:".Length), out myId))
                    {
                        m_PlayerNetId = myId;
                        Log("[CLIENT] My player is network object " + m_PlayerNetId);

                        foreach (KeyValuePair<int, GameObject> keyValuePair in m_linking.Objects)
                        {
                            NetObject existing = keyValuePair.Value.GetComponent<NetObject>();
                            if (existing != null) 
                            {
                                ApplyOwnership(existing);
                            }

                        }
                    }
                    return;
                }
                if (text.StartsWith("ROOM:"))
                {
                    m_roomInfo = text.Substring("ROOM:".Length);
                }


                if (text.StartsWith("FULL:"))
                {
                    m_joinError = text.Substring("FULL:".Length).Trim();

                    Log("[CLIENT] Join rejected: " + m_joinError);

                    m_joinConfirmed = false;

                    Disconnect();

                    return;
                }

                if (text.StartsWith("PLAYERS:"))
                {
                    m_playerList = text.Substring("PLAYERS:".Length);
                    m_joinConfirmed = true;
                }
                if (text.StartsWith("CHAT:"))
                {
                    m_chat.Add(text.Substring("CHAT:".Length));
                    if (m_chat.Count > 5)
                    {
                        m_chat.RemoveAt(0);
                    }
                    break;
                }
                break;
            case MsgType.Player:

                PlayerVarsDTO dto = Serializer.DeserializePlayerVars(data);
                break;

            case MsgType.Replication:
                if (!m_sceneReady) break;
                BinaryReader reader = new BinaryReader(new MemoryStream(data));
                reader.ReadByte();
                m_replication.ReadPacket(reader);
                break;
            default:
                break;

        }

    }
    void SendLocalPlayerState()
    {
        Vector3 position = m_localPlayerCube.position;
        Vector3 rotation = m_localPlayerCube.eulerAngles;

        PlayerVarsDTO dto = new PlayerVarsDTO
        {

            Posx = position.x,
            Posy = position.y,
            Posz = position.z,

            Rotx = rotation.x,
            Roty = rotation.y,
            Rotz = rotation.z
        };

        SendPacket(Serializer.Serialize(dto));
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
            if (args[i] == "-ip" && i + 1 < args.Length) serverIp = args[++i];
            else if (args[i] == "-port" && i + 1 < args.Length) int.TryParse(args[++i], out port);
            else if (args[i] == "-name" && i + 1 < args.Length) userName = args[++i];
            else if (args[i] == "-autostart") autoStart = true;
            else if (args[i] == "-noautostart") autoStart = false;
        }
    }

    void OnGUI()
    {
        GUILayout.BeginArea(new Rect(10, 10, Screen.width - 20, Screen.height - 20));

        GUILayout.Label("UDP CLIENT");

        GUILayout.Space(10);

        if (!m_running)
        {
            GUILayout.Label("Player Name:");

            userName = GUILayout.TextField(userName,GUILayout.Width(200));

            GUILayout.Space(5);

            GUILayout.Label("Server IP:");

            serverIp = GUILayout.TextField(serverIp,GUILayout.Width(200));

            GUILayout.Space(10);

            if (GUILayout.Button("Join Game", GUILayout.Width(150)))
            {
                m_joinError = "";

                IPAddress address;

                if (string.IsNullOrWhiteSpace(userName))
                {
                    Log("[CLIENT] Please enter your name.");
                }
                else if (!IPAddress.TryParse(serverIp, out address) ||
                         address.AddressFamily != AddressFamily.InterNetwork)
                {
                    Log("[CLIENT] Invalid server IP.");
                }
                else
                {
                    userName = userName.Trim();
                    m_joinConfirmed = false;

                    StartNetwork();
                }
            }
            if (!string.IsNullOrEmpty(m_joinError))
            {
                GUILayout.Space(10);
                GUILayout.Label("Join failed: " + m_joinError);
            }
        }
        else
        {
            if (m_joinConfirmed)
            {
                GUILayout.Label("WAITING ROOM");
                GUILayout.Label("Joined as: " + userName);
                GUILayout.Label("Players: " + (string.IsNullOrEmpty(m_roomInfo) ? "Loading..." : m_roomInfo));

                GUILayout.Space(10);
                GUILayout.Label("PLAYERS:");

                if (string.IsNullOrEmpty(m_playerList))
                {
                    GUILayout.Label("No players");
                }
                else
                {
                    string[] players = m_playerList.Split(',');

                    foreach (string player in players)
                    {
                        if (!string.IsNullOrWhiteSpace(player))
                        {
                            GUILayout.Label("- " + player.Trim());
                        }
                    }
                }
                GUILayout.Space(10);


                GUILayout.Label("CHAT");
                foreach (string message in m_chat)
                {
                    GUILayout.Label(message);
                }
                messageInput = GUILayout.TextField(messageInput);
                if (GUILayout.Button("Send", GUILayout.Width(150)))
                {
                    SendChat();
                }
    
            }
            else
            {
                GUILayout.Label("Waiting for server response...");
            }

            GUILayout.Space(10);

            if (GUILayout.Button("Disconnect", GUILayout.Width(150)))
            {
                SendString("LEAVE:");
                Disconnect();
                m_joinConfirmed = false;
            }
        }

        GUILayout.Space(10);

        lock (m_log)
        {
            for (int i = Mathf.Max(0, m_log.Count - 4);i < m_log.Count; i++)
            {
                GUILayout.Label(m_log[i]);
            }
        }

        GUILayout.EndArea();
    }


    Socket StartClient()
    {
        Socket socket = new Socket(AddressFamily.InterNetwork,SocketType.Dgram,ProtocolType.Udp);

        return socket;   
    }

    EndPoint ServerEndPoint()
    {
        return new IPEndPoint(IPAddress.Parse(serverIp),port);   
    }

    void OnStarted()
    {
        SendString("JOIN:" + userName);     
    }

    int SendRaw(Socket socket, byte[] data, EndPoint to)
    {
        return socket.SendTo(data, to);   
    }

    int ReceiveRaw(Socket socket, byte[] buffer, ref EndPoint from)
    {
        return socket.ReceiveFrom(buffer, ref from);   
    }
}
