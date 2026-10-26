
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;

public class SocketsTCPClient : MonoBehaviour
{
    public string serverIp = "127.0.0.1";
    public int port = 9050;
    public string userName = "Player";
    public bool autoStart = true;


    const int MaxPacketSize = 64 * 1024;

    Socket m_connection;                                      // the connection to the server
    readonly List<Thread> m_threads = new List<Thread>();
    readonly ConcurrentQueue<byte[]> m_inbox = new ConcurrentQueue<byte[]>();
    readonly List<string> m_log = new List<string>();
    readonly HashSet<int> m_warned = new HashSet<int>();
    volatile bool m_running;
    string m_playerList = "";
    string m_roomInfo = "";
    string m_joinError = "";

    //Chat
    public string messageInput;

    //Cube
    [SerializeField] Transform m_cube;
    Quaternion recievedRot;

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
        StartThread(ClientThread);
    }

    // Closing the socket is what unblocks the thread waiting in Receive() (slide 14).
    public void Disconnect()
    {
        if (!m_running) return;
        m_running = false;
        m_playerList = "";
        m_roomInfo = "";

        CloseSocket(m_connection); m_connection = null;

        Thread[] threads;
        lock (m_threads) { threads = m_threads.ToArray(); m_threads.Clear(); }
        foreach (Thread t in threads) if (t != Thread.CurrentThread) t.Join(500);

        Log("[CLIENT] Disconnected");
    }

    void OnDestroy() { Disconnect(); }

    // Main thread: the only safe place to touch the Unity API.
    void Update()
    {
        byte[] data;
        while (m_inbox.TryDequeue(out data)) { OnPacketReceived(data); }

        m_cube.transform.rotation = recievedRot;
    }

    void ClientThread()
    {
        try { m_connection = StartClient(); }                                       
        catch (SocketException e)
        {
            Log("[CLIENT] Could not connect: " + e.SocketErrorCode +
                (e.SocketErrorCode == SocketError.ConnectionRefused
                    ? " (nobody is listening on " + serverIp + ":" + port + ", slide 34)" : ""));
            return;
        }

        if (m_connection == null) { WarnTodo(1, "StartClient() returned null"); return; }
        Log("[CLIENT] Connected to " + serverIp + ":" + port);

        OnConnected();                                                              
        ReceiveLoop(m_connection);
        Log("[CLIENT] Connection closed");
    }

    public void SendPacket(byte[] payload)
    {
        if (m_connection == null || payload == null) return;

        byte[] framed = new byte[4 + payload.Length];
        BitConverter.GetBytes(payload.Length).CopyTo(framed, 0);
        payload.CopyTo(framed, 4);

        try
        {
            int sent = SendRaw(m_connection, framed);                                
            if (sent < 0) WarnTodo(3, "SendRaw() is not implemented yet");
        }
        catch (SocketException e) { Log("[CLIENT] Send failed: " + e.SocketErrorCode); }
        catch (ObjectDisposedException) { }
    }

    public void SendString(string text)
    {
        SendPacket(Encoding.UTF8.GetBytes(text));
    }
    public void SendChat() 
    {
        SendString("CHAT:" + messageInput);
        messageInput = "";
    }

    void ReceiveLoop(Socket socket)
    {
        byte[] header = new byte[4];
        while (m_running)
        {
            if (!ReadExactly(socket, header, 4)) return;

            int size = BitConverter.ToInt32(header, 0);
            if (size <= 0 || size > MaxPacketSize) { Log("[CLIENT] Invalid packet size: " + size); return; }

            byte[] payload = new byte[size];
            if (!ReadExactly(socket, payload, size)) return;

            m_inbox.Enqueue(payload);
        }
    }

    bool ReadExactly(Socket socket, byte[] buffer, int count)
    {
        int total = 0;
        while (total < count)
        {
            int read;
            try { read = ReceiveRaw(socket, buffer, total, count - total); }         
            catch (SocketException e) { Log("[CLIENT] Connection lost: " + e.SocketErrorCode); return false; }
            catch (ObjectDisposedException) { return false; }

            if (read < 0) { WarnTodo(4, "ReceiveRaw() is not implemented yet"); return false; }
            if (read == 0) return false;   
            total += read;
        }
        return true;
    }

    void OnPacketReceived(byte[] data)
    {
        string text = Encoding.UTF8.GetString(data);

        if (text.StartsWith("ROOM:"))
        {
            m_roomInfo = text.Substring("ROOM:".Length);
        }


        if (text.StartsWith("FULL:"))
        {
            m_joinError = text.Substring("FULL:".Length).Trim();

            Log("[CLIENT] Join rejected: " + m_joinError);

            Disconnect();

            return;
        }

        if (!text.StartsWith("ROT:")) 
        {
            Log("[CLIENT] Received: " + text);
        }//The logs from the rotation would saturate the screen, it's not important
        
        if (text.StartsWith("PLAYERS:"))
        {
            m_playerList = text.Substring("PLAYERS:".Length);
        }
        
        else if (text.StartsWith("ROT:"))
        {
            string rotationText = text.Replace("ROT:", "");
            string[] coordinates = rotationText.Split(',');
            if(float.TryParse(coordinates[0], out float x) && float.TryParse(coordinates[1], out float y) && float.TryParse(coordinates[2], out float z))
            {
                recievedRot = Quaternion.Euler(x, y, z);
            }
        }


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
            if (args[i] == "-ip" && i + 1 < args.Length) serverIp = args[++i];
            else if (args[i] == "-port" && i + 1 < args.Length) int.TryParse(args[++i], out port);
            else if (args[i] == "-name" && i + 1 < args.Length) userName = args[++i];
            else if (args[i] == "-autostart") autoStart = true;
            else if (args[i] == "-noautostart") autoStart = false;
        }
    }

    void OnGUI()
    {
        GUILayout.BeginArea(new Rect(10, 10, 450, 400));

        GUILayout.Label("TCP CLIENT");

        if (!m_running)
        {
            GUILayout.Label("Player Name:");
            userName = GUILayout.TextField(userName, GUILayout.Width(200));

            GUILayout.Label("Server IP:");
            serverIp = GUILayout.TextField(serverIp, GUILayout.Width(200));

            GUILayout.Space(10);

            if (GUILayout.Button("Join Game", GUILayout.Width(150)))
            {
                m_joinError = "";
                StartNetwork();
            }

            if (!string.IsNullOrEmpty(m_joinError))
            {
                GUILayout.Space(10);
                GUILayout.Label("Join failed: " + m_joinError);
            }
        }
        else
        {
            GUILayout.Label("WAITING ROOM");
            GUILayout.Label("Players: " + (string.IsNullOrEmpty(m_roomInfo) ? "Loading..." : m_roomInfo));
            GUILayout.Label("Connecting / Connected to " + serverIp);

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
            messageInput = GUILayout.TextField(messageInput);

            if (GUILayout.Button("Send", GUILayout.Width(150)))
            {
                SendChat();
            }

            if (GUILayout.Button("Disconnect", GUILayout.Width(150)))
            {
                Disconnect();
            }
        }

        GUILayout.Space(10);

        lock (m_log)
        {
            for (int i = Mathf.Max(0, m_log.Count - 4);
                 i < m_log.Count; i++)
            {
                GUILayout.Label(m_log[i]);
            }
        }

        GUILayout.EndArea();
    }

    Socket StartClient()
    {
        Socket socket = new Socket(
            AddressFamily.InterNetwork,
            SocketType.Stream,
            ProtocolType.Tcp);

        socket.Connect(
            new IPEndPoint(
                IPAddress.Parse(serverIp),
                port
            )
        );

        return socket;   
    }

    void OnConnected()
    {
        SendString("JOIN:" + userName);

    }

    int SendRaw(Socket socket, byte[] data)
    {
        return socket.Send(data);
    }


    int ReceiveRaw(Socket socket, byte[] buffer, int offset, int count)
    {
        return socket.Receive(
            buffer,
            offset,
            count,
            SocketFlags.None);
    }
}
