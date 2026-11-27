// =================================================================================================
//  Lab 6 - From lobby to multiplayer game: DEBUG OVERLAY
//  XJO - Xarxes per a Jocs Online
// =================================================================================================
//
//  Given. Nothing to modify. See your network (slide 19). Add it to one GameObject in your game
//  scene, then feed it from your own code:
//
//    NetDebugOverlay.CountSent(bytes.Length);          // in your SendPacket
//    NetDebugOverlay.CountReceived(bytes.Length);      // in your OnPacketReceived
//    NetDebugOverlay.NetworkedObjects = m_linking.Count;
//    NetDebugOverlay.SetLine("Anna", "id 2, ping 12 ms, silent 0.1 s");     // server, per client
//    NetDebugOverlay.Label(bullet, "12");              // the network ID above an object
//    NetDebugOverlay.AddSlider("Latency (s)", () => m_sim.latency, v => m_sim.latency = v, 0f, 0.5f);
//    NetDebugOverlay.AddButton("Disconnect", Disconnect);
//
//  F1 shows and hides it. Every frame you must call Label again for the labels you want to see.
//
// =================================================================================================

using System;
using System.Collections.Generic;
using UnityEngine;

public class NetDebugOverlay : MonoBehaviour
{
    public bool visible = true;

    public static int NetworkedObjects;

    static int s_sent, s_received, s_sentBytes, s_receivedBytes;           // this second so far
    static int s_sentRate, s_receivedRate, s_sentBytesRate, s_receivedBytesRate;
    static float s_windowStart;
    static readonly SortedDictionary<string, string> s_lines = new SortedDictionary<string, string>();
    static readonly List<(string label, Func<float> get, Action<float> set, float min, float max)> s_sliders =
        new List<(string, Func<float>, Action<float>, float, float)>();
    static readonly List<(string label, Action click)> s_buttons = new List<(string, Action)>();
    static readonly List<(Transform target, string text)> s_labels = new List<(Transform, string)>();

    public static void CountSent(int bytes) { s_sent++; s_sentBytes += bytes; }
    public static void CountReceived(int bytes) { s_received++; s_receivedBytes += bytes; }

    // One line per key. Calling it again with the same key replaces the line. null text removes it.
    public static void SetLine(string key, string text)
    {
        if (text == null) s_lines.Remove(key); else s_lines[key] = text;
    }

    public static void AddSlider(string label, Func<float> get, Action<float> set, float min, float max)
    {
        s_sliders.Add((label, get, set, min, max));
    }

    public static void AddButton(string label, Action click) { s_buttons.Add((label, click)); }

    // Text drawn above an object this frame, for example its network ID.
    public static void Label(GameObject target, string text)
    {
        if (target != null) s_labels.Add((target.transform, text));
    }

    // Statics survive between Play sessions when domain reload is off. Start clean every time.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        NetworkedObjects = 0;
        s_sent = s_received = s_sentBytes = s_receivedBytes = 0;
        s_sentRate = s_receivedRate = s_sentBytesRate = s_receivedBytesRate = 0;
        s_windowStart = 0;
        s_lines.Clear(); s_sliders.Clear(); s_buttons.Clear(); s_labels.Clear();
    }

    void Update()
    {
        // Every second, what was counted becomes the rate shown.
        if (Time.unscaledTime - s_windowStart >= 1f)
        {
            s_sentRate = s_sent; s_receivedRate = s_received;
            s_sentBytesRate = s_sentBytes; s_receivedBytesRate = s_receivedBytes;
            s_sent = s_received = s_sentBytes = s_receivedBytes = 0;
            s_windowStart = Time.unscaledTime;
        }
    }

    void OnGUI()
    {
        // F1 through OnGUI events, so it works with the old and the new Input System.
        Event e = Event.current;
        if (e.type == EventType.KeyDown && e.keyCode == KeyCode.F1) { visible = !visible; e.Use(); }

        if (visible)
        {
            DrawLabels();
            DrawPanel();
        }
        if (e.type == EventType.Repaint) s_labels.Clear();      // labels last one frame
    }

    void DrawLabels()
    {
        Camera cam = Camera.main;
        if (cam == null) return;
        foreach (var (target, text) in s_labels)
        {
            if (target == null) continue;
            Vector3 p = cam.WorldToScreenPoint(target.position + Vector3.up);
            if (p.z < 0) continue;                                  // behind the camera
            GUI.Label(new Rect(p.x - 50, Screen.height - p.y - 10, 100, 20), text,
                      new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter });
        }
    }

    void DrawPanel()
    {
        GUILayout.BeginArea(new Rect(10, 10, 360, Screen.height - 20), GUI.skin.box);
        GUILayout.Label("<b>NETWORK</b>   (F1 hides)", new GUIStyle(GUI.skin.label) { richText = true });
        GUILayout.Label("Sent      " + s_sentRate + " packets/s, " + s_sentBytesRate + " bytes/s");
        GUILayout.Label("Received  " + s_receivedRate + " packets/s, " + s_receivedBytesRate + " bytes/s");
        GUILayout.Label("Networked objects  " + NetworkedObjects);

        foreach (var line in s_lines) GUILayout.Label(line.Key + "  " + line.Value);

        foreach (var (label, get, set, min, max) in s_sliders)
        {
            float v = get();
            GUILayout.Label(label + "  " + v.ToString("0.000"));
            float nv = GUILayout.HorizontalSlider(v, min, max);
            if (!Mathf.Approximately(nv, v)) set(nv);
        }
        foreach (var (label, click) in s_buttons)
            if (GUILayout.Button(label)) click();
        GUILayout.EndArea();
    }
}
