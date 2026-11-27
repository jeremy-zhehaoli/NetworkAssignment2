using System.IO;
using UnityEngine;
public class NetObject : MonoBehaviour
{
    public int id;            
    public byte prefabType;
    public bool isMine;
    bool m_receivedFirstState;
    public virtual void Write(BinaryWriter w)
    {
        Vector3 position = transform.position;
        Vector3 rotation = transform.eulerAngles;

        w.Write(position.x);
        w.Write(position.y);
        w.Write(position.z);

        w.Write(rotation.x);
        w.Write(rotation.y);
        w.Write(rotation.z);
    }

    public virtual void Read(BinaryReader r)
    {
        float Posx = r.ReadSingle();
        float Posy = r.ReadSingle();
        float Posz = r.ReadSingle();

        float Rotx = r.ReadSingle();
        float Roty = r.ReadSingle();
        float Rotz = r.ReadSingle();

        if (isMine && m_receivedFirstState) return; //To spawn with spawnPoint Coordinates
        m_receivedFirstState = true;

        transform.position = new Vector3(Posx, Posy, Posz);
        transform.rotation = Quaternion.Euler(Rotx, Roty, Rotz);
    }
}
public class NetPlayer : NetObject
{
    public int health = 100;

    public override void Write(BinaryWriter w)
    {
        base.Write(w);
        w.Write(health);
    }

    public override void Read(BinaryReader r)
    {
        base.Read(r);
        health = r.ReadInt32();
    }
}
