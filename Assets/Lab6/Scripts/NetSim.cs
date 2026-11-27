// =================================================================================================
//  Lab 6 - From lobby to multiplayer game: NETWORK SIMULATOR
//  XJO - Xarxes per a Jocs Online
// =================================================================================================
//
//  THE EXERCISE
//    In class the network is perfect. At home it is not (slide 20). NetSim wraps SendTo and
//    makes your own packets late (latency), late by a different amount each (jitter, which
//    reorders them) and sometimes lost (dropChance). Same code as slide 21.
//
//  HOW TO RUN IT
//    Open the scene S_LinkingTest and press Play. The NetSim tests run after the linking ones,
//    on your own PC (127.0.0.1), and take about 2 seconds.
//
//  WHAT YOU HAVE TO DO
//    TODO 4 and 5 below. Delete the Lab6Todo.NotDone(...) line and write yours.
//
//  THEN, IN YOUR GAME
//    Copy this file into your Deliverable 3 project. In your network layer (slide 5):
//      m_sim = new NetSim(m_socket);              // after creating the socket
//      m_sim.Send(bytes, to);                     // instead of m_socket.SendTo(bytes, to)
//      m_sim.Update();                            // in your Update, every frame
//    On both sides, so both directions suffer. For sliders, NetDebugOverlay.AddSlider (slide 23).
//
// =================================================================================================

using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using UnityEngine;

public class NetSim
{
    public float latency = 0.1f, jitter = 0.03f, dropChance = 0.02f;   // seconds, seconds, 0 to 1

    readonly Socket m_socket;
    readonly List<(float at, byte[] data, EndPoint to)> m_queue = new List<(float, byte[], EndPoint)>();

    public NetSim(Socket socket) { m_socket = socket; }

    // Packets waiting to be sent. For the debug overlay.
    public int Pending { get { return m_queue.Count; } }

    // --------------------------------------------------------------------------------- TODO 4 ---
    //  Goal: do not send now. Maybe drop the packet, otherwise queue it with the time it must leave.
    //  To drop, Random.value is between 0 and 1. Below dropChance, return without queueing.
    //  The delay is latency plus a random amount between -jitter and +jitter. Leaves at Time.time + delay.
    //  Look it up: UnityEngine.Random.value and Random.Range(float, float) in the docs.
    //  Test scene when it works:  dropChance 0.5, about 50 of 100 packets arrive
    public void Send(byte[] data, EndPoint to)
    {
        if (Random.value < dropChance) return;                        // lost
        float delay = latency + Random.Range(-jitter, jitter);         // jitter, may reorder
        m_queue.Add((Time.time + delay, data, to));
    }

    // --------------------------------------------------------------------------------- TODO 5 ---
    //  Goal: send every queued packet whose time has come, and take it out of the queue.
    //  Go through the queue FORWARD. RemoveAt(i) moves the next packet into i, so only i++ when you
    //  did not remove. A backwards loop would send packets due in the same frame in reverse order
    //  (slide 21 notes).
    //  Test scene when it works:  jitter 0, 50 of 50 arrive, in order
    public void Update()
    {
        for (int i = 0; i < m_queue.Count;)                         // in order, so no jitter means no reordering
            if (Time.time >= m_queue[i].at)
            {
                m_socket.SendTo(m_queue[i].data, m_queue[i].to);
                m_queue.RemoveAt(i);                                   // the next one moves into i
            }
            else i++;
    }
}
