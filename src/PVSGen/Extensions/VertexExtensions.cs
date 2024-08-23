using PSDL;
using System.Numerics;

namespace PVSGen.Extensions
{
    internal static class VertexExtensions
    {
        public static Vector3 ToVector3(this Vertex vert)
        {
            return new Vector3(vert.x, vert.y, vert.z);
        }
    }
}
