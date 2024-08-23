using System.Numerics;

namespace PVSGen.Extensions
{
    public static class BinaryExtensions
    {
        public static void WriteAGEString(this BinaryWriter writer, string str)
        {
            if (str.Length == 0)
            {
                writer.Write((byte)0);
            }
            else
            {
                writer.Write((byte)(str.Length + 1));
                for (int i = 0; i < str.Length; i++)
                    writer.Write(str[i]);
                writer.Write((byte)0);
            }
        }

        public static void WriteVector4(this BinaryWriter writer, Vector4 vec)
        {
            writer.Write(vec.X);
            writer.Write(vec.Y);
            writer.Write(vec.Z);
            writer.Write(vec.W);
        }

        public static void WriteVector3(this BinaryWriter writer, Vector3 vec)
        {
            writer.Write(vec.X);
            writer.Write(vec.Y);
            writer.Write(vec.Z);
        }

        public static void WriteVector2(this BinaryWriter writer, Vector2 vec)
        {
            writer.Write(vec.X);
            writer.Write(vec.Y);            
        }

        public static Vector4 ReadVector4(this BinaryReader reader)
        {
            return new Vector4(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
        }

        public static Vector3 ReadVector3(this BinaryReader reader)
        {
            return new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
        }

        public static Vector2 ReadVector2(this BinaryReader reader)
        {
            return new Vector2(reader.ReadSingle(), reader.ReadSingle());
        }
    }
}
