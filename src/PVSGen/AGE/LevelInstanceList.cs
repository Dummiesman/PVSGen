using System.Numerics;
using PVSGen.Extensions;

[Flags]
public enum InstanceFlags : byte
{
    Landmark = 1 << 0, // Landmark, no backfacing, has bound
    Banger = 1 << 1, // Instance is a banger instance
    SimpleCollision = 1 << 2, // No wheel collision, only in combination with Landmark flag
    Multiroom = 1 << 5 // Multiroom, has bound
}

public class LevelInstanceList
{
    public readonly List<Instance> Instances = new List<Instance>();
    private Dictionary<int, List<int>> instanceRoomMap;

    public abstract class Instance
    {
        public string Name;
        public int RoomIndex;
        public byte Variant;
        public InstanceFlags Flags;

        public bool NeedsBound => ((Flags & InstanceFlags.Landmark) != 0) || ((Flags & InstanceFlags.Multiroom) != 0);

        public abstract void ReadBinary(BinaryReader reader);
        public abstract void WriteBinary(BinaryWriter writer);
        public abstract void GetTransform(out Vector3 position, out Quaternion rotation, out Vector3 scale);
        public abstract void SetTransform(Vector3 location, Quaternion rotation, Vector3 scale);        
    }

    public class MatrixComponent : Instance
    {
        public Vector3 origin;
        public Vector3 xAxis;
        public Vector3 yAxis;
        public Vector3 zAxis;

        public override void GetTransform(out Vector3 position, out Quaternion rotation, out Vector3 scale)
        {
            position = origin;
            rotation = Quaternion.CreateFromRotationMatrix(Matrix4x4.CreateWorld(Vector3.Zero, -zAxis, yAxis));
            scale = new Vector3(xAxis.Length(), yAxis.Length(), zAxis.Length());
        }

        public override void SetTransform(Vector3 location, Quaternion rotation, Vector3 scale)
        {
            Matrix4x4 matrix = Matrix4x4.CreateFromQuaternion(rotation);
            origin = location;
            xAxis = matrix.GetColumn3(0) * scale;
            yAxis = matrix.GetColumn3(1) * scale;
            zAxis = matrix.GetColumn3(2) * scale;
        }

        public override void ReadBinary(BinaryReader r)
        {
            xAxis = r.ReadVector3().Flipped();
            yAxis = r.ReadVector3().Flipped();
            zAxis = r.ReadVector3().Flipped();
            origin = r.ReadVector3().Flipped();
        }

        public override void WriteBinary(BinaryWriter w)
        {
            w.WriteVector3(xAxis.Flipped());
            w.WriteVector3(yAxis.Flipped());
            w.WriteVector3(zAxis.Flipped());
            w.WriteVector3(origin.Flipped());
        }

        public MatrixComponent() { }
    }

    public class SimpleComponent : Instance
    {
        public Vector3 origin;
        public float xDelta;
        public float zDelta;

        public override void GetTransform(out Vector3 position, out Quaternion rotation, out Vector3 scale)
        {
            position = origin;

            var facing = Vector3.Cross(-Vector3.UnitY, new Vector3(xDelta, 0.0f, zDelta));
            rotation = Quaternion.CreateFromRotationMatrix(Matrix4x4.CreateWorld(Vector3.Zero, facing, Vector3.UnitY));
            scale = (Vector3.One * new Vector2(xDelta, zDelta).Length()).WithY(1.0f);
        }

        public override void SetTransform(Vector3 location, Quaternion rotation, Vector3 scale)
        {
            Matrix4x4 matrix = Matrix4x4.CreateFromQuaternion(rotation);
            origin = location;

            Vector3 side = matrix.GetColumn3(0) * new Vector2(scale.X, scale.Y).Length();
            xDelta = side.X;
            zDelta = side.Z;
        }

        public override void ReadBinary(BinaryReader r)
        {
            xDelta = -r.ReadSingle();
            zDelta = r.ReadSingle();
            origin = r.ReadVector3().Flipped();
        }

        public override void WriteBinary(BinaryWriter w)
        {
            w.Write(-xDelta);
            w.Write(zDelta);
            w.WriteVector3(origin.Flipped());
        }

        public SimpleComponent() { }
    }

    public void BuildInstanceRoomMap()
    {
        instanceRoomMap = new Dictionary<int, List<int>>();
        for (int i = 0; i < Instances.Count; i++)
        {
            var instance = Instances[i];
            if (instance.RoomIndex < 0)
                continue;

            List<int> roomInstanceIndices;
            if (!instanceRoomMap.TryGetValue(instance.RoomIndex, out roomInstanceIndices))
            {
                roomInstanceIndices = new List<int>();
                instanceRoomMap[instance.RoomIndex] = roomInstanceIndices;
            }
            roomInstanceIndices.Add(i);
        }
    }

    public IEnumerable<Instance> GetInstancesInRoom(int room)
    {
        if (instanceRoomMap == null)
        {
            BuildInstanceRoomMap();
        }

        if (instanceRoomMap.TryGetValue(room, out var mapIndices))
        {
            foreach (var mapIndex in mapIndices)
                yield return Instances[mapIndex];
        }
    }

    public void WriteBinary(BinaryWriter w)
    {
        foreach (var instance in Instances)
        {
            // write header
            w.Write((short)(instance.RoomIndex));
            w.Write(instance.Variant);
            w.Write((byte)instance.Flags);

            // write name / type field
            byte nameLength = (byte)(instance.Name.Length + 1);
            if (instance is SimpleComponent)
                nameLength += 128;
            w.Write(nameLength);

            for (int i = 0; i < instance.Name.Length; i++)
                w.Write(instance.Name[i]);
            w.Write((byte)0x00);

            // write component
            instance.WriteBinary(w);
        }
    }

    public void ReadBinary(BinaryReader r)
    {
        while (r.BaseStream.Position < r.BaseStream.Length)
        {
            int roomIndex = r.ReadUInt16();
            byte variant = r.ReadByte();
            byte flags = r.ReadByte();

            // read name and type
            byte nameAndType = r.ReadByte();
            int nameLength = nameAndType & 0x7f;
            int type = Math.Max(0, (nameAndType & 0x80) - 127);
            string name = new string(r.ReadChars(nameLength)).Replace("\x00", "");

            // read instance
            Instance instance = null;
            switch (type)
            {
                case 1:
                    {
                        instance = new SimpleComponent();
                        break;
                    }
                case 0:
                    {
                        instance = new MatrixComponent();
                        break;
                    }
                default:
                    {
                        throw new Exception($"Failed to read instance {name} because it has an invalid type. ({type})");
                    }
            }
            instance.ReadBinary(r);

            // set data and add to list
            instance.Variant = variant;
            instance.Flags = (InstanceFlags)flags;
            instance.RoomIndex = roomIndex;
            instance.Name = name;

            Instances.Add(instance);
        }
    }

}
