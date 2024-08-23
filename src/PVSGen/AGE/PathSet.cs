using PVSGen.Extensions;
using System.Numerics;

public class PathSet
{
    public class Path
    {
        public string Name;
        public float Spacing = 0;
        public PathType Type = PathType.Lines;
        public int Flags = 0;
        public List<Vector3> Points = new List<Vector3>();

        private float _length = 0f;
        public float Length
        {
            get
            {
                if (_length == 0f) RecalculateLength();
                return _length;
            }
        }

        public enum PathType
        {
            Points,
            Lines,
            LineStrip
        }

        public void RecalculateLength()
        {
            _length = 0f;
            for (int i = 0; i < Points.Count - 1; i++)
                _length += Vector3.Distance(Points[i], Points[i + 1]);
        }

        public void WriteBinary(BinaryWriter w)
        {
            //write name
            for (int i = 0; i < 32; i++)
            {
                if( i >= Name.Length)
                    w.Write('\x00');
                else
                    w.Write(Name[i]);
            }

            w.Write(Points.Count);
            w.Write(Flags);
            for (int i = 0; i < Points.Count; i++)
            {
                w.Write(0); //TODO: figure out the unknown
                w.WriteVector3(Points[i].Flipped());
            }

            w.Write((byte) Type);
            w.Write((byte)(Spacing * 4f));
            w.Write((ushort)0);
        }

        public void ReadBinary(BinaryReader r)
        {
            string nameNullTerminated = new string(r.ReadChars(32));
            Name = nameNullTerminated.Substring(0, nameNullTerminated.IndexOf('\x00'));
            
            int numPoints = r.ReadInt32();
            Flags = r.ReadInt32();
            for (int i = 0; i < numPoints; i++)
            {
                r.BaseStream.Seek(4, SeekOrigin.Current); //unknown, room?
                Points.Add(r.ReadVector3().Flipped());
            }

            Type = (PathType)r.ReadByte();
            Spacing = (float)r.ReadByte() * 0.25f;

            r.ReadUInt16(); //unused?
        }

        public void Reverse()
        {
            Points.Reverse();
        }

        public Path() { }

        public Path(string name)
        {
            this.Name = name;
        }
    }

    public List<Path> Paths = new List<Path>();

    public void WriteBinary(BinaryWriter w)
    {
        w.Write(0x31485450); //PTH1

        w.Write(Paths.Count);
        w.Write(Paths.Count - 1);

        for (int i = 0; i < Paths.Count; i++)
        {
            Paths[i].WriteBinary(w);
        }
    }

    public static PathSet ReadBinary(BinaryReader r)
    {
        var pathSet = new PathSet();

        int header = r.ReadInt32();
        if (header != 0x31485450)
        {
            throw new Exception("Pathset file has incorrect header");
        }

        int numPaths = r.ReadInt32();
        r.BaseStream.Seek(4, SeekOrigin.Current);

        for (int i = 0; i < numPaths; i++)
        {
            var path = new Path();
            path.ReadBinary(r);
            pathSet.Paths.Add(path);
        }

        return pathSet;
    }
}
