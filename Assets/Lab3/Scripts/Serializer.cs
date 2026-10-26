using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering.Universal;

public static class Serializer
{

    public static byte[] Serialize(PlayerVarsDTO dto)
    {
        MemoryStream stream = new MemoryStream();
        BinaryWriter writer = new BinaryWriter(stream);
        writer.Write((byte)MsgType.Player);

        writer.Write(dto.Posx);
        writer.Write(dto.Posy);
        writer.Write(dto.Posz);

        writer.Write(dto.Rotx);
        writer.Write(dto.Roty);
        writer.Write(dto.Rotz);

        writer.Write(dto.id);

        return stream.ToArray();
    }

    public static PlayerVarsDTO DeserializePlayerVars(byte[] data)
    {
        BinaryReader reader = new BinaryReader(new MemoryStream(data));
        PlayerVarsDTO dto = new PlayerVarsDTO();
        MsgType type = (MsgType)reader.ReadByte();

        dto.Posx = reader.ReadSingle();
        dto.Posy = reader.ReadSingle();
        dto.Posz = reader.ReadSingle();

        dto.Rotx = reader.ReadSingle();
        dto.Roty = reader.ReadSingle();
        dto.Rotz = reader.ReadSingle();

        dto.id = reader.ReadInt32();


        return dto;
    }

    public static MsgType GetMessageType(byte[] data)
    {
        return (MsgType)data[0];
    }

    public static byte[] SerializeText(string text)
    {
        byte[] textConverted = System.Text.Encoding.UTF8.GetBytes(text);
        byte[] finalText = new byte[textConverted.Length + 1];

        finalText[0] = (byte)MsgType.Text;

        //for (int i = 0; i < finalText.Length; i++)
        //{
        //    finalText[i + 1] = textConverted[i];
        //}
        Buffer.BlockCopy(textConverted, 0, finalText, 1, textConverted.Length);
        return finalText;
    }
    public static string DeserializeText(byte[] data)
    {
        return Encoding.UTF8.GetString(data, 1, data.Length - 1);
    }

    public static string ToHex(byte[] data)
    {
        if (data == null) return "(null)";
        StringBuilder sb = new StringBuilder();
        for (int i = 0; i < data.Length; i++)
        {
            if (i > 0) sb.Append(' ');
            sb.Append(data[i].ToString("X2"));
        }
        return sb.ToString();
    }
}
