using RoadToPro.Core.Hashing;
using RoadToPro.Core.Math;
using RoadToPro.Core.Serialization;
using RoadToPro.MatchEngine.Match.Events;
using RoadToPro.MatchEngine.Match.Pipeline;

namespace RoadToPro.MatchEngine.Actions
{
    /// <summary>
    /// 예약 킥 (specs/008 FR-65·66): 단계 5의 <see cref="BallAction"/>을 물리 단계가 받아 킥 준비 시간 뒤(<see cref="Due"/>) 공이 도달 거리 안인 서브스텝에 실행한다.
    /// 틱 경계를 넘을 수 있어 상태(해시·스냅샷)다.
    /// </summary>
    public struct PendingKick
    {
        public bool Active;
        public BallAction Action;
        /// <summary>대체 태클이면 그 순간 공을 가진 상대 슬롯, 아니면 −1.</summary>
        public int Victim;
        public KickPlan Plan;
        /// <summary>실행 가능 첫 절대 서브스텝, 취소 절대 서브스텝.</summary>
        public long Due, Expire;
        /// <summary>예약한 결정 틱.</summary>
        public long Tick;

        public void AddTo(ref StableHasher h)
        {
            h.Add(Active);
            if (!Active) return;
            var a = Action;
            h.Add((byte)a.Kind); h.Add(a.KickerSlot); h.Add(a.TargetSlot);
            h.Add(a.Velocity.X); h.Add(a.Velocity.Y); h.Add(a.Velocity.Z); h.Add(a.Spin.X); h.Add(a.Spin.Y); h.Add(a.Spin.Z);
            h.Add((byte)a.Part); h.Add(a.IsRestart); h.Add(a.IsPenalty); h.Add(a.IsKickoff);
            h.Add(a.HasIntent); h.Add(a.Aim.X); h.Add(a.Aim.Y); h.Add(a.Speed); h.Add(a.Elevation); h.Add(a.XPass); h.Add(a.IsOneTouch);
            h.Add(Victim);
            h.Add((byte)Plan.Foot); h.Add(Plan.WeakFoot); h.Add((byte)Plan.Tech); h.Add(Plan.BodyAngle); h.Add(Plan.Windup); h.Add(Plan.SigmaMul);
            h.Add(Due); h.Add(Expire); h.Add(Tick);
        }

        public void Write(StateWriter w)
        {
            w.Write(Active);
            if (!Active) return;
            var a = Action;
            w.Write((byte)a.Kind); w.Write(a.KickerSlot); w.Write(a.TargetSlot);
            w.Write(a.Velocity); w.Write(a.Spin);
            w.Write((byte)a.Part); w.Write(a.IsRestart); w.Write(a.IsPenalty); w.Write(a.IsKickoff);
            w.Write(a.HasIntent); w.Write(a.Aim); w.Write(a.Speed); w.Write(a.Elevation); w.Write(a.XPass); w.Write(a.IsOneTouch);
            w.Write(Victim);
            w.Write((byte)Plan.Foot); w.Write(Plan.WeakFoot); w.Write((byte)Plan.Tech); w.Write(Plan.BodyAngle); w.Write(Plan.Windup); w.Write(Plan.SigmaMul);
            w.Write(Due); w.Write(Expire); w.Write(Tick);
        }

        public static PendingKick Read(StateReader r)
        {
            var k = new PendingKick { Active = r.ReadBool() };
            if (!k.Active) return k;
            var a = new BallAction
            {
                Kind = (BallActionKind)r.ReadByte(), KickerSlot = r.ReadInt(), TargetSlot = r.ReadInt(),
                Velocity = r.ReadVec3(), Spin = r.ReadVec3(),
                Part = (BodyPart)r.ReadByte(), IsRestart = r.ReadBool(), IsPenalty = r.ReadBool(), IsKickoff = r.ReadBool(),
                HasIntent = r.ReadBool(), Aim = r.ReadVec2(), Speed = r.ReadFloat(), Elevation = r.ReadFloat(), XPass = r.ReadFloat(), IsOneTouch = r.ReadBool(),
            };
            k.Action = a;
            k.Victim = r.ReadInt();
            k.Plan = new KickPlan
            {
                Foot = (BodyPart)r.ReadByte(), WeakFoot = r.ReadBool(), Tech = (KickTech)r.ReadByte(), BodyAngle = r.ReadFloat(), Windup = r.ReadFloat(), SigmaMul = r.ReadFloat(),
            };
            k.Due = r.ReadLong(); k.Expire = r.ReadLong(); k.Tick = r.ReadLong();
            return k;
        }
    }
}
