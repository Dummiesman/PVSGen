using BepuPhysics;
using BepuPhysics.Collidables;
using System.Numerics;

namespace PVSGen.Physics
{
    public unsafe struct RoomIdSweepHandler : ISweepHitHandler
    {
        public readonly HashSet<int> HitRooms;
        private CollidableProperty<int> RoomIdProp;

        public bool AllowTest(CollidableReference collidable)
        {
            return true;
        }

        public bool AllowTest(CollidableReference collidable, int child)
        {
            return true;
        }

        public void OnHit(ref float maximumT, float t, in Vector3 hitLocation, in Vector3 hitNormal, CollidableReference collidable)
        {
            int hitRoom = RoomIdProp[collidable.StaticHandle];
            HitRooms.Add(hitRoom);
        }

        public void OnHitAtZeroT(ref float maximumT, CollidableReference collidable)
        {

            int hitRoom = RoomIdProp[collidable.StaticHandle];
            HitRooms.Add(hitRoom);
        }

        public RoomIdSweepHandler(CollidableProperty<int> idProp)
        {
            HitRooms = new HashSet<int>();
            this.RoomIdProp= idProp;
        }
    }
}
