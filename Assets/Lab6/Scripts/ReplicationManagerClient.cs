using System;
using System.IO;
using Unity.VisualScripting;
using UnityEngine;

public class ReplicationManagerClient
{
    readonly LinkingContext m_linking;
    readonly GameObject[] m_prefabs; //SAME ORDER!!!
    int m_lastSequence = -1;

    public Action<NetObject> OnCreated;

    public ReplicationManagerClient(LinkingContext linking, GameObject[] prefabs)
    {
        m_linking = linking;
        m_prefabs = prefabs;
    }


    public void ReadPacket(BinaryReader reader)
    {
        int sequence = reader.ReadInt32();
        if (sequence <= m_lastSequence) return; // older than what we have
        m_lastSequence = sequence;
        int count = reader.ReadInt32();
        for (int i = 0; i < count; i++)
        {
            int id = reader.ReadInt32();
            ReplicationAction action = (ReplicationAction)reader.ReadByte();
            GameObject go = m_linking.Find(id);
            if (action == ReplicationAction.Destroy)
            {
                if (go != null)
                { 
                    UnityEngine.Object.Destroy(go);
                    m_linking.Unregister(id); 
                }
                continue; // unknown ID, already gone
            }
            byte type = reader.ReadByte();
            // Create, or an Update whose Create was lost
            if (go == null)
            {
                go = UnityEngine.Object.Instantiate(m_prefabs[type]);
                go.GetComponent<NetObject>().id = id;
                go.GetComponent<NetObject>().prefabType = type;

                m_linking.Register(go, id);

                NetObject netObject = go.GetComponent<NetObject>();
                OnCreated(netObject);
            }
            go.GetComponent<NetObject>().Read(reader);
        }
    }
}
