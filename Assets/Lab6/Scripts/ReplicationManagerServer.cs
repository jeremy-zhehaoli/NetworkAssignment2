using UnityEngine;
using System.Collections.Generic;
using System.IO;

public class ReplicationManagerServer : MonoBehaviour
{
    public enum ReplicationAction : byte { Create = 1, Update = 2, Destroy = 3 }
    readonly Dictionary<int, ReplicationAction> m_pending = new Dictionary<int, ReplicationAction>();
    readonly LinkingContext m_linking;

    int m_sequence;
    public ReplicationManagerServer(LinkingContext linking)
    {
        m_linking = linking;
    }
    public void MarkCreate(int id) 
    {
        m_pending[id] = ReplicationAction.Create; 
    }
    public void MarkDestroy(int id) 
    {
        m_pending[id] = ReplicationAction.Destroy; 
    }
    public void MarkUpdate(int id)
    {
        if (!m_pending.ContainsKey(id)) 
            m_pending[id] = ReplicationAction.Update;
    }

    public byte[] WritePacket() 
    {
        MemoryStream stream = new MemoryStream();
        BinaryWriter writer = new BinaryWriter(stream);
        writer.Write((byte)MsgType.Replication); 
        writer.Write(m_sequence++);
        writer.Write(m_pending.Count);
        foreach (KeyValuePair<int, ReplicationAction> entry in m_pending)
        {
            writer.Write(entry.Key); 
            writer.Write((byte)entry.Value);
            if (entry.Value == ReplicationAction.Destroy) continue; 
            NetObject obj = m_linking.Find(entry.Key).GetComponent<NetObject>();
            writer.Write(obj.prefabType); 
            obj.Write(writer); 
        }
        m_pending.Clear();
        return stream.ToArray();
    }

}
