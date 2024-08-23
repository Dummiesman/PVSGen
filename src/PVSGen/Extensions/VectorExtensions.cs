using PSDL;
using System.Numerics;

namespace PVSGen.Extensions
{
    internal static class VectorExtensions
    {
        public static Vector3 Flipped(this Vector3 vec)
        {
            return new Vector3(-vec.X, vec.Y, vec.Z);
        }

        public static Vector3 MoveTowards(this Vector3 current, Vector3 target, float maxDistanceDelta)
        {
            var toVector = target - current;
            float sqdist = toVector.LengthSquared();

            if (sqdist == 0 || (maxDistanceDelta >= 0 && sqdist <= maxDistanceDelta * maxDistanceDelta))
                return target;

            var dist = MathF.Sqrt(sqdist);
            return current + toVector / dist * maxDistanceDelta;
        }

        public static Vector3 RotateAroundY(this Vector3 vec, Vector3 rotateAround, float angle)
        {
            float Deg2Rad = MathF.PI / 180.0f;
            float s = MathF.Sin(angle * Deg2Rad);
            float c = MathF.Cos(angle * Deg2Rad);

            // translate point back to origin:
            Vector3 p = vec;
            p.X -= rotateAround.X;
            p.Z -= rotateAround.Z;

            // rotate point
            float xnew = p.X * c - p.Z * s;
            float znew = p.X * s + p.Z * c;

            // translate point back:
            p.X = xnew + rotateAround.X;
            p.Z = znew + rotateAround.Z;
            return p;
        }

        public static bool IsNaN(this Vector3 v)
        {
            return (float.IsNaN(v.X) || float.IsNaN(v.Y) || float.IsNaN(v.Z));
        }

        public static Vector3 WithY(this Vector3 v, float y)
        {
            return new Vector3(v.X, y, v.Z);
        }

        public static Vector3 ToZUp(this Vector3 v)
        {
            return new Vector3(v.X, v.Z, v.Y);
        }

        public static Vertex ToVertex(this Vector3 vec)
        {
            return new Vertex(vec.X, vec.Y, vec.Z);
        }

        public static void SetColumn3(this Matrix4x4 mtx, int columnNumber, Vector3 column)
        {
            switch (columnNumber)
            {
                case 0:
                    mtx.M11 = column.X;
                    mtx.M21 = column.Y;
                    mtx.M31 = column.Z;
                    break;
                case 1:
                    mtx.M12 = column.X;
                    mtx.M22 = column.Y;
                    mtx.M32 = column.Z;
                    break;
                case 2:
                    mtx.M13 = column.X;
                    mtx.M23 = column.Y;
                    mtx.M33 = column.Z;
                    break;
                case 3:
                    mtx.M14 = column.X;
                    mtx.M24 = column.Y;
                    mtx.M34 = column.Z;
                    break;
            }
        }

        public static void SetColumn(this Matrix4x4 mtx, int columnNumber, Vector4 column)
        {
            switch (columnNumber)
            {
                case 0:
                    mtx.M11 = column.X;
                    mtx.M21 = column.Y;
                    mtx.M31 = column.Z;
                    mtx.M41 = column.W;
                    break;
                case 1:
                    mtx.M12 = column.X;
                    mtx.M22 = column.Y;
                    mtx.M32 = column.Z;
                    mtx.M42 = column.W;
                    break;
                case 2:
                    mtx.M13 = column.X;
                    mtx.M23 = column.Y;
                    mtx.M33 = column.Z;
                    mtx.M43 = column.W;
                    break;
                case 3:
                    mtx.M14 = column.X;
                    mtx.M24 = column.Y;
                    mtx.M34 = column.Z;
                    mtx.M44 = column.W;
                    break;
            }
        }

        public static Vector3 GetColumn3(this Matrix4x4 mtx, int columnNumber)
        {
            switch (columnNumber)
            {
                case 0:
                    return new Vector3(mtx.M11, mtx.M21, mtx.M31);
                case 1:
                    return new Vector3(mtx.M12, mtx.M22, mtx.M32);
                case 2:
                    return new Vector3(mtx.M13, mtx.M23, mtx.M33);
                case 3:
                    return new Vector3(mtx.M14, mtx.M24, mtx.M34);
            }
            return Vector3.Zero;
        }

        public static Vector4 GetColumn(this Matrix4x4 mtx, int columnNumber)
        {
            switch (columnNumber)
            {
                case 0:
                    return new Vector4(mtx.M11, mtx.M21, mtx.M31, mtx.M41);
                case 1:
                    return new Vector4(mtx.M12, mtx.M22, mtx.M32, mtx.M42);
                case 2:
                    return new Vector4(mtx.M13, mtx.M23, mtx.M33, mtx.M43);
                case 3:
                    return new Vector4(mtx.M14, mtx.M24, mtx.M34, mtx.M44);
            }
            return Vector4.Zero;
        }
    }
}
