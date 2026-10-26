using System;
using System.Collections.Generic;

public struct PlayerVarsDTO
{
    public float Posx; public float Posy; public float Posz;

    public float Rotx; public float Roty; public float Rotz;

    public int id;
}


public enum MsgType : byte
{
    Text = 1,    
    Player = 2,   
}
