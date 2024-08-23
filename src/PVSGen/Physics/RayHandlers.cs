using BepuPhysics.Collidables;
using BepuPhysics.Trees;
using BepuPhysics;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace PVSGen.Physics
{
    unsafe struct SinglePointHitHandler : IRayHitHandler
    {
        public bool Hit;
        public Vector3 HitPos;
        public Vector3 HitNormal;

        private int restrictRoom;
        private CollidableProperty<int> RoomIdProp;
        private float lowestT;
        private bool onlyCountClosest;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool AllowTest(CollidableReference collidable)
        {
            return RoomIdProp[collidable.StaticHandle] == restrictRoom;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool AllowTest(CollidableReference collidable, int childIndex)
        {
            return RoomIdProp[collidable.StaticHandle] == restrictRoom;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void OnRayHit(in RayData ray, ref float maximumT, float t, in Vector3 normal, CollidableReference collidable, int childIndex)
        {
            if (onlyCountClosest && t < maximumT) maximumT = t;

            if (!onlyCountClosest || t < lowestT)
            {
                Hit = true;
                HitPos = ray.Origin + (ray.Direction * t);
                HitNormal = normal;
                lowestT = t;
            }
        }

        public SinglePointHitHandler(CollidableProperty<int> idProp, int restrictRoom, bool onlyCountClosest)
        {
            this.Hit = false;
            this.HitPos = Vector3.Zero;
            this.HitNormal = Vector3.Zero;
            this.restrictRoom = restrictRoom;
            this.onlyCountClosest = onlyCountClosest;
            RoomIdProp = idProp;
            lowestT = float.MaxValue;
        }
    }

    struct RoomIdRay
    {
        public Vector3 Origin;
        public Vector3 Direction;
        public Vector3 HitPoint;
        public int HitRoom;
    }

    unsafe struct RoomIdHitHandler : IRayHitHandler
    {
        public bool Hit;
        public int HitRoom;
        public RoomIdRay RayDebug;
        
        private CollidableProperty<int> RoomIdProp;
        private float lowestT;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool AllowTest(CollidableReference collidable)
        {
            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool AllowTest(CollidableReference collidable, int childIndex)
        {
            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void OnRayHit(in RayData ray, ref float maximumT, float t, in Vector3 normal, CollidableReference collidable, int childIndex)
        {
            //We are only interested in the earliest hit. This callback is executing within the traversal, so modifying maximumT informs the traversal
            //that it can skip any AABBs which are more distant than the new maximumT.
            if (t < maximumT) maximumT = t;

            if (t <= lowestT)
            {
                lock (RoomIdProp)
                {
                    int hitRoom = RoomIdProp[collidable.StaticHandle];
                    Hit = true;
                    HitRoom = hitRoom;

                    RayDebug = new RoomIdRay()
                    {
                        Direction = ray.Direction,
                        Origin = ray.Origin,
                        HitRoom = hitRoom,
                        HitPoint = ray.Origin + (ray.Direction * t)
                    };

                    lowestT = t;
                }
            }
        }

        public RoomIdHitHandler(CollidableProperty<int> idProp)
        {
            RoomIdProp = idProp;
            HitRoom = default;
            Hit = false;
            RayDebug = default;
            lowestT = float.MaxValue;
        }
    }
}
