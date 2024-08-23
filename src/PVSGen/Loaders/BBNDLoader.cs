using BepuPhysics.Collidables;
using PVSGen.Extensions;
using System.Numerics;

namespace PVSGen.BoundLoader
{
    internal class BBNDLoader
    {
        public readonly List<Triangle> Triangles = new List<Triangle>();

        public void Load(Stream stream)
        {
            var reader = new BinaryReader(stream);
            byte version = reader.ReadByte();
            if(version != 1)
            {
                throw new Exception("Malformed BBND file!");
            }

            int nverts = reader.ReadInt32();
            int nmaterials = reader.ReadInt32();
            int npolys = reader.ReadInt32();

            List<Vector3> verts = new List<Vector3>(nverts);
            for(int i=0; i < nverts; i++)
            {
                var pos = reader.ReadVector3().Flipped();
                verts.Add(pos);
            }

            reader.BaseStream.Seek(nmaterials * 104, SeekOrigin.Current); // skip materials

            for(int i=0; i < npolys; i++)
            {
                int i0 = reader.ReadUInt16();
                int i1 = reader.ReadUInt16();
                int i2 = reader.ReadUInt16();
                int i3 = reader.ReadUInt16();
                int material = reader.ReadUInt16();

                Triangles.Add(new Triangle(verts[i0], verts[i1], verts[i2]));
                if (i3 != 0)
                {
                    Triangles.Add(new Triangle(verts[i0], verts[i2], verts[i3]));
                }
            }
        }

        public BBNDLoader() 
        { 
        }
    }
}
