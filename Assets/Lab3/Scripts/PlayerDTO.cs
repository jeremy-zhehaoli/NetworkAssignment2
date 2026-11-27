using System;
using System.Collections.Generic;

public struct PlayerVarsDTO
{
    public float Posx, Posy, Posz;
    public float Rotx, Roty, Rotz;
}

public enum MsgType : byte
{
    Text = 1,    
    Player = 2,   
    Replication = 3
}
public enum ReplicationAction : byte 
{ 
    Create = 1, 
    Update = 2,
    Destroy = 3 
}
