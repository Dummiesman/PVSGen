using BepuPhysics.Collidables;
using PVSGen.Extensions;
using System.Numerics;
using System.Text;

namespace PVSGen.BoundLoader
{
    [Flags]
    public enum FVF
    {
        D3DFVFRESERVED0 = 1 << 0,
        D3DFVF_XYZ = 1 << 1,
        D3DFVF_XYZRHW = 1 << 2,
        D3DFVF_UNUSED = 1 << 3,
        D3DFVF_NORMAL = 1 << 4,
        D3DFVF_RESERVED1 = 1 << 5,
        D3DFVF_DIFFUSE = 1 << 6,
        D3DFVF_SPECULAR = 1 << 7,
        D3DFVF_TEX1 = 1 << 8,
        D3DFVF_TEX2 = 1 << 9,
        D3DFVF_TEX3 = 1 << 10,
        D3DFVF_TEX4 = 1 << 11,
        D3DFVF_TEX5 = 1 << 12,
        D3DFVF_TEX6 = 1 << 13,
        D3DFVF_TEX7 = 1 << 14,
        D3DFVF_TEX8 = 1 << 15
    }

    internal class PKGLoader
    {
        public readonly List<Triangle> Triangles = new List<Triangle>();
        
        private int FVFSize(FVF fvf)
        {
            int size = 0;
            if ((fvf & (FVF.D3DFVF_XYZ | FVF.D3DFVF_XYZRHW)) != 0) size += 12;
            if ((fvf & (FVF.D3DFVF_NORMAL)) != 0) size += 12;
            if ((fvf & (FVF.D3DFVF_DIFFUSE)) != 0) size += 4;
            if ((fvf & (FVF.D3DFVF_SPECULAR)) != 0) size += 4;
            if ((fvf & (FVF.D3DFVF_TEX1)) != 0) size += 8;
            if ((fvf & (FVF.D3DFVF_TEX2)) != 0) size += 8;
            if ((fvf & (FVF.D3DFVF_TEX3)) != 0) size += 8;
            if ((fvf & (FVF.D3DFVF_TEX4)) != 0) size += 8;
            if ((fvf & (FVF.D3DFVF_TEX5)) != 0) size += 8;
            if ((fvf & (FVF.D3DFVF_TEX6)) != 0) size += 8;
            if ((fvf & (FVF.D3DFVF_TEX7)) != 0) size += 8;
            if ((fvf & (FVF.D3DFVF_TEX8)) != 0) size += 8;
            return size;
        }

        public void Load(Stream stream, params string[] objectNames)
        {
            var reader = new BinaryReader(stream);

            int magic = reader.ReadInt32();
            if(magic != 0x33474B50)
            {
                throw new Exception("Outdated or invalid PKG file.");
            }

            long streamLength = reader.BaseStream.Length;
            while(reader.BaseStream.Position < streamLength)
            {
                int fileHeader = reader.ReadInt32();
                if(fileHeader != 0x454C4946)
                {
                    throw new Exception("Failed to find FILE magic. (Previous reader didn't consume all data?)");
                }

                int nameLen = reader.ReadByte();
                string name = Encoding.ASCII.GetString(reader.ReadBytes(nameLen - 1));
                reader.ReadByte(); // null terminator

                int fileLength = reader.ReadInt32();
                if(!objectNames.Contains(name))
                {
                    reader.BaseStream.Seek(fileLength, SeekOrigin.Current);
                }
                else
                {
                    int nSections = reader.ReadInt32();
                    int nVerticesTot = reader.ReadInt32();
                    int nIndicesTot = reader.ReadInt32();
                    int nSections2 = reader.ReadInt32();

                    Triangles.Capacity = nIndicesTot / 3;
                    FVF fvf = (FVF)reader.ReadInt32();

                    for (int i=0; i < nSections; i++)
                    {
                        int nStrips = reader.ReadUInt16();
                        int flags = reader.ReadUInt16();
                        int shaderOffset = reader.ReadInt32();

                        for(int j=0; j < nStrips; j++)
                        {
                            int primType = reader.ReadInt32();
                            int nVertices = reader.ReadInt32();

                            List<Vector3> vertices = new List<Vector3>();
                            int skipBytesPerVert = FVFSize(fvf) - 12;

                            for(int k=0; k < nVertices; k++)
                            {
                                var pos = reader.ReadVector3().Flipped();
                                vertices.Add(pos);
                                reader.BaseStream.Seek(skipBytesPerVert, SeekOrigin.Current);
                            }

                            int nIndices = reader.ReadInt32();
                            for(int k=0; k < nIndices / 3; k++)
                            {
                                int i0 = reader.ReadUInt16();
                                int i1 = reader.ReadUInt16();
                                int i2 = reader.ReadUInt16();

                                Triangles.Add(new Triangle(vertices[i0], vertices[i1], vertices[i2]));
                            }
                        }
                    }

                    return;
                }
            }
        }

        public PKGLoader()
        {
        }
    }
}
