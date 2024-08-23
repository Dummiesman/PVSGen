using BepuPhysics.Collidables;
using System.Numerics;

namespace PVSGen
{
    public class RayMeshBuilder
    {
        public readonly List<Triangle> Triangles = new List<Triangle>();

        public void AddRay(Vector3 start, Vector3 hitPoint, float thickness)
        {
            Vector3 direction = Vector3.Normalize(hitPoint - start);
            Vector3 up = Vector3.UnitY; // arbitrary up direction
            if (Vector3.Dot(direction, up) > 0.99f) // in case direction is parallel to up, choose a different axis
            {
                up = Vector3.UnitZ;
            }

            float halfThickness = thickness / 2.0f;
            Vector3 right = Vector3.Normalize(Vector3.Cross(direction, up)) * halfThickness;
            Vector3 offsetUp = Vector3.Normalize(Vector3.Cross(right, direction)) * halfThickness;

            Vector3 p1 = start + right;
            Vector3 p2 = start - right * 0.5f + offsetUp;
            Vector3 p3 = start - right * 0.5f - offsetUp;

            Vector3 p4 = hitPoint + right;
            Vector3 p5 = hitPoint - right * 0.5f + offsetUp;
            Vector3 p6 = hitPoint - right * 0.5f - offsetUp;

            Triangles.Add(new Triangle(p1, p2, p4));
            Triangles.Add(new Triangle(p2, p5, p4));

            Triangles.Add(new Triangle(p2, p3, p5));
            Triangles.Add(new Triangle(p3, p6, p5));

            Triangles.Add(new Triangle(p3, p1, p6));
            Triangles.Add(new Triangle(p1, p4, p6));
        }
    }
}