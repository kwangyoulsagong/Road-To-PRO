using System;
using System.Collections.Generic;
using RoadToPro.Core.Math;
using RoadToPro.Core.Pitch;
using RoadToPro.MatchEngine.Ball;
using RoadToPro.MatchEngine.Kinematics;
using RoadToPro.MatchEngine.Match.Events;
using RoadToPro.Presentation.Players.Logic;

namespace RoadToPro.Verification.Samples.Motion
{
    /// <summary>합성 장면 한 개 (specs/054 "이번 구현 범위"). 시뮬 기록 + 100 Hz 원본 궤적 + 측정 키 시각.</summary>
    public sealed class MotionScene
    {
        public string Id;
        public HighlightData Data;
        /// <summary>움직이는 엔티티. 측정 대상.</summary>
        public int[] Actors;
        /// <summary>100 Hz 원본 위치 [substep * Entities + e] (AC-11).</summary>
        public float[] RawX, RawZ;
        public const float RawDt = 0.01f;
        /// <summary>E2E 캡처 시각(출발·감속·디딤·접촉·접촉 후 등): 이름 → 시뮬 초.</summary>
        public readonly List<KeyValuePair<string, float>> Keys = new List<KeyValuePair<string, float>>();
        /// <summary>캡처 키별 카메라 대상 엔티티. 없으면 첫 배우.</summary>
        public readonly Dictionary<string, int> Focus = new Dictionary<string, int>();
        /// <summary>시뮬 결과를 후처리한 장면: 100 Hz 원본과 비교할 수 없다 (AC-11 대상 아님).</summary>
        public bool RawEdited;
        /// <summary>시뮬 쪽 오류를 일부러 담은 장면 (FR-80). 측정기가 054 실패와 구분한다.</summary>
        public bool ExpectSimFault;
    }

    /// <summary>
    /// 증거 장면 8종 + 경계 장면 (specs/054 § 검증·증거, AC-18). 궤적은 005 운동학, 공 비행은 004 공 물리로 만든다 — 손으로 쓴 궤적이 아니라 시뮬 한계 안의 값.
    /// 사건·판정 결과는 008·011·012 전까지 손으로 정한다. RNG 없음, 고정 시드와 같다.
    /// 좌표: 시뮬 (x 길이, y 폭, z 높이) → 월드 (X = x − L/2, Y = z, Z = y − W/2), yaw 0 = +Z.
    /// </summary>
    public sealed class MotionSceneBuilder
    {
        public const float Start = 100f;

        public static readonly string[] EvidenceIds = { "straight50", "stopCut", "turnBackSide", "longPass", "volley", "tackle", "header", "keeper" };
        public static readonly string[] EdgeIds = { "simFault", "oneTouch", "chainTouch", "sameFrame", "flyingStart", "substitution", "inNet", "fastSlow", "curveRun", "evade", "shoulderDuel", "postGraze" };

        readonly KinematicsParams _kin;
        readonly BallParams _ball;
        readonly ClipLibrary _lib;
        readonly PitchSpec _pitch;

        public MotionSceneBuilder(KinematicsParams kin, BallParams ball, ClipLibrary lib, PitchSpec pitch)
        {
            _kin = kin ?? throw new ArgumentNullException(nameof(kin));
            _ball = ball ?? throw new ArgumentNullException(nameof(ball));
            _lib = lib ?? throw new ArgumentNullException(nameof(lib));
            _pitch = pitch;
        }

        public MotionScene Build(string id)
        {
            switch (id)
            {
                case "straight50": return Straight50();
                case "stopCut": return StopCut();
                case "turnBackSide": return TurnBackSide();
                case "longPass": return LongPass();
                case "volley": return Volley();
                case "tackle": return Tackle();
                case "header": return Header();
                case "keeper": return Keeper();
                case "simFault": return SimFault();
                case "oneTouch": return OneTouch();
                case "chainTouch": return ChainTouch();
                case "sameFrame": return SameFrame();
                case "flyingStart": return FlyingStart();
                case "substitution": return Substitution();
                case "inNet": return InNet();
                case "fastSlow": return FastSlow();
                case "curveRun": return CurveRun();
                case "evade": return Evade();
                case "shoulderDuel": return ShoulderDuel();
                case "postGraze": return PostGraze();
                default: throw new ArgumentException("장면 없음: " + id);
            }
        }

        // ---------- 장면 스크립트 작성기 ----------

        sealed class Script
        {
            public readonly MotionSceneBuilder B;
            public readonly string Id;
            public readonly float Length;
            public readonly List<PlayerSetup> Setups = new List<PlayerSetup>();
            public readonly List<(float t, int e, Func<KinematicsSystem, SteeringCommand> cmd)> Cmds = new List<(float, int, Func<KinematicsSystem, SteeringCommand>)>();
            /// <summary>몸 방향 덮어쓰기 구간 (005는 이동 방향만 기록한다 — 백페달·사이드스텝·저킹 표현용 합성 입력).</summary>
            public readonly List<(float t0, float t1, int e, float yaw)> Faces = new List<(float, float, int, float)>();
            public readonly List<int> Actors = new List<int>();
            readonly bool[] _placed = new bool[HighlightData.Entities];

            public Script(MotionSceneBuilder b, string id, float length)
            {
                B = b;
                Id = id;
                Length = length;
            }

            public int Actor(int e, Vec2 pos, Vec2 facing, int display = 14, Vec2 vel = default)
            {
                Setups.Add(new PlayerSetup(e, KinematicAttributes.Uniform(display), pos, vel, facing));
                _placed[e] = true;
                Actors.Add(e);
                return e;
            }

            public void Cmd(float t, int e, SteeringCommand c) => Cmds.Add((t, e, _ => c));
            public void Cmd(float t, int e, Func<KinematicsSystem, SteeringCommand> c) => Cmds.Add((t, e, c));

            /// <summary>배치 안 한 엔티티: 경기장 먼 쪽에 정지 (측면선 밖 줄).</summary>
            public void PlaceBystanders()
            {
                for (int e = 0; e < HighlightData.Entities; e++)
                    if (!_placed[e])
                        Setups.Add(new PlayerSetup(e, KinematicAttributes.Uniform(10), new Vec2(5f + e * 3.5f, 66f), default, new Vec2(0f, -1f)));
            }
        }

        /// <summary>접촉 후 2프레임 (AC-14): 1.5x 60 fps 렌더 프레임 2개 = 시뮬 0.05 s.</summary>
        const float AfterContact = 2f * 1.5f / 60f;

        const float YawSlew = 700f * (float)Math.PI / 180f; // 몸 방향 덮어쓰기 회전 속도 (005 피벗 범위 안)

        MotionScene Simulate(Script sc)
        {
            sc.PlaceBystanders();
            var k = new KinematicsSystem(_kin, sc.Setups.ToArray());
            int steps = (int)Math.Round(sc.Length / MotionScene.RawDt);
            const int E = HighlightData.Entities;
            var rawX = new float[(steps + 1) * E];
            var rawZ = new float[(steps + 1) * E];
            var rawYaw = new float[(steps + 1) * E];
            var cmds = new List<(float t, int e, Func<KinematicsSystem, SteeringCommand> cmd)>(sc.Cmds);
            cmds.Sort((a, b) => a.t != b.t ? a.t.CompareTo(b.t) : a.e.CompareTo(b.e));
            int next = 0;
            var yaw = new float[E];
            void Store(int s)
            {
                for (int e = 0; e < E; e++)
                {
                    ref readonly var st = ref k.State(k.IndexOf(e));
                    rawX[s * E + e] = st.Position.X - _pitch.LengthM * 0.5f;
                    rawZ[s * E + e] = st.Position.Y - _pitch.WidthM * 0.5f;
                    float want = (float)Math.Atan2(st.Facing.X, st.Facing.Y);
                    float t = s * MotionScene.RawDt;
                    foreach (var f in sc.Faces)
                        if (f.e == e && t >= f.t0 && t < f.t1) want = f.yaw;
                    if (s == 0) yaw[e] = want;
                    else
                    {
                        float d = (float)Math.IEEERemainder(want - yaw[e], 2 * Math.PI);
                        float max = YawSlew * MotionScene.RawDt;
                        yaw[e] += d > max ? max : d < -max ? -max : d;
                    }
                    rawYaw[s * E + e] = yaw[e];
                }
            }
            Store(0);
            for (int s = 1; s <= steps; s++)
            {
                float t = (s - 1) * MotionScene.RawDt;
                while (next < cmds.Count && cmds[next].t <= t + 1e-5f)
                {
                    k.SetCommandImmediate(cmds[next].e, cmds[next].cmd(k));
                    next++;
                }
                k.Step();
                Store(s);
            }
            int frames = (int)Math.Floor(sc.Length * HighlightData.FrameRate) + 1;
            var d2 = new HighlightData(Start, frames);
            for (int f = 0; f < frames; f++)
            {
                float u = f / HighlightData.FrameRate / MotionScene.RawDt;
                int s0 = Math.Min((int)u, steps - 1);
                float a = u - s0;
                for (int e = 0; e < E; e++)
                {
                    int i0 = s0 * E + e, i1 = i0 + E;
                    d2.Set(f, e, rawX[i0] + (rawX[i1] - rawX[i0]) * a, rawZ[i0] + (rawZ[i1] - rawZ[i0]) * a, rawYaw[i0] + (rawYaw[i1] - rawYaw[i0]) * a);
                }
            }
            return new MotionScene { Id = sc.Id, Data = d2, Actors = sc.Actors.ToArray(), RawX = rawX, RawZ = rawZ };
        }

        // ---------- 공 ----------

        /// <summary>월드 위치·속도 → 시뮬.</summary>
        Vec3 ToSim(Vec3 w) => new Vec3(w.X + _pitch.LengthM * 0.5f, w.Z + _pitch.WidthM * 0.5f, w.Y);
        static Vec3 VelToSim(Vec3 w) => new Vec3(w.X, w.Z, w.Y);
        Vec3 ToWorld(Vec3 s) => new Vec3(s.X - _pitch.LengthM * 0.5f, s.Z, s.Y - _pitch.WidthM * 0.5f);

        /// <summary>시각 t의 공 = from. 프레임 구간 [t0, t1)을 고정 위치로 채운다.</summary>
        static void BallHold(HighlightData d, float t0, float t1, Vec3 pos)
        {
            for (int f = 0; f < d.Frames; f++)
            {
                float t = d.StartTime + f / HighlightData.FrameRate;
                if (t >= t0 && t < t1) d.Ball[f] = pos;
            }
        }

        /// <summary>from(t0) → to(t1) 직선 (들어오는 공: 시뮬 비행 대신 접촉 위치를 정확히 맞추는 입력).</summary>
        static void BallLine(HighlightData d, float t0, Vec3 from, float t1, Vec3 to)
        {
            for (int f = 0; f < d.Frames; f++)
            {
                float t = d.StartTime + f / HighlightData.FrameRate;
                if (t >= t0 && t <= t1) d.Ball[f] = from + (to - from) * ((t - t0) / (t1 - t0));
            }
        }

        /// <summary>t0에 pos에서 vel로 차인 공을 004로 t1까지 진행.</summary>
        void BallFly(HighlightData d, float t0, float t1, Vec3 pos, Vec3 vel)
        {
            var s = pos.Y <= _ball.Radius + 1e-3f && Math.Abs(vel.Y) < 1e-3f
                ? BallState.RollingAt(ToSim(pos).XY, VelToSim(vel).XY, _ball.Radius)
                : BallState.InAir(ToSim(pos), VelToSim(vel), Vec3.Zero);
            var ev = new BallEventBuffer();
            float dt = MotionScene.RawDt, now = t0;
            float prevT = t0;
            var prev = ToWorld(s.Position);
            int f = (int)Math.Ceiling((t0 - d.StartTime) * HighlightData.FrameRate - 1e-4f);
            while (f < d.Frames)
            {
                float ft = d.StartTime + f / HighlightData.FrameRate;
                if (ft > t1) break;
                while (now < ft - 1e-5f)
                {
                    prev = ToWorld(s.Position);
                    prevT = now;
                    ev.Clear();
                    BallStepper.Step(ref s, _ball, ev, out _);
                    now += dt;
                }
                float a = now > prevT ? (ft - prevT) / (now - prevT) : 0f;
                var cur = ToWorld(s.Position);
                d.Ball[f] = prev + (cur - prev) * a;
                f++;
            }
        }

        /// <summary>엔티티 e가 시각 t에 clip으로 공을 건드릴 때 공이 있어야 할 위치(클립 접촉점 + 어긋남).</summary>
        Vec3 BallAtContact(HighlightData d, int e, float t, string clip, Vec3 jitter = default)
        {
            if (!_lib.TryGet(clip, out var c) || !c.HasContact) throw new ArgumentException("접촉 클립 아님: " + clip);
            var p = d.Pos(e, t);
            float y = d.YawAt(e, t), sy = (float)Math.Sin(y), cy = (float)Math.Cos(y);
            var cp = c.ContactPoint + jitter;
            return new Vec3(p.X + cp.X * cy + cp.Z * sy, cp.Y, p.Z - cp.X * sy + cp.Z * cy);
        }

        static Vec3 Dir(float yaw, float speed, float up = 0f) => new Vec3((float)Math.Sin(yaw) * speed, up, (float)Math.Cos(yaw) * speed);

        static void Key(MotionScene s, string name, float t) => s.Keys.Add(new KeyValuePair<string, float>(name, t));

        static void Ev(HighlightData d, float t, SimEventKind kind, int e, int other, BodyPart part, ActionOutcome o, Vec3 ball, Vec3 vel) =>
            d.Events.Add(new AnimEvent(t, d.Events.Count + 1, kind, e, other, part, o, ball, vel));

        static void Owner(HighlightData d, float t0, float t1, int e)
        {
            for (int f = 0; f < d.Frames; f++)
            {
                float t = d.StartTime + f / HighlightData.FrameRate;
                if (t >= t0 && t < t1) d.BallOwner[f] = (short)e;
            }
        }

        static readonly Vec2 E0 = new Vec2(1f, 0f);

        /// <summary>엔티티를 구간 내내 고정 (경합 상대: 005 Separation이 밀어내는 값 대신 맞붙은 합성 위치).</summary>
        static void Hold(HighlightData d, int e, float x, float z, float yaw)
        {
            for (int f = 0; f < d.Frames; f++) d.Set(f, e, x, z, yaw);
        }

        const float TackleSlide = 3.5f, TackleBrake = 8f;

        /// <summary>시각 t까지 winT초에 걸쳐 tackler를 victim 뒤 gap으로 붙인다 (Separation이 벌린 거리를 접촉 거리로).</summary>
        static void CloseIn(HighlightData d, int tackler, int victim, float t, float win, float gap)
        {
            const int E = HighlightData.Entities;
            for (int f = 0; f < d.Frames; f++)
            {
                float ft = d.StartTime + f / HighlightData.FrameRate;
                if (ft < t - win) continue;
                float u = Math.Min(1f, (ft - (t - win)) / win);
                u = u * u * (3f - 2f * u);
                var v = d.Pos(victim, Math.Min(ft, t));
                var y = d.YawAt(victim, Math.Min(ft, t));
                var want = v - Dir(y, gap);
                int i = f * E + tackler;
                if (ft >= t)
                {
                    // 접촉 뒤 미끄러지며 멈춘다 (감속 8 m/s²): 순간 정지는 시뮬 오류로 잡힌다
                    float tau = Math.Min(ft - t, TackleSlide / TackleBrake);
                    float along = TackleSlide * tau - 0.5f * TackleBrake * tau * tau;
                    var slid = want + Dir(y, along);
                    d.X[i] = slid.X;
                    d.Z[i] = slid.Z;
                    d.Yaw[i] = y;
                    continue;
                }
                d.X[i] += (want.X - d.X[i]) * u;
                d.Z[i] += (want.Z - d.Z[i]) * u;
                d.Yaw[i] += (float)Math.IEEERemainder(y - d.Yaw[i], 2 * Math.PI) * u;
            }
        }

        // ---------- 증거 장면 8종 ----------

        /// <summary>① 직선 질주 50 m: 정지 → 출발 → 최고속도.</summary>
        MotionScene Straight50()
        {
            var sc = new Script(this, "straight50", 9f);
            sc.Actor(0, new Vec2(20f, 34f), E0, 16);
            sc.Cmd(0.5f, 0, SteeringCommand.Seek(new Vec2(95f, 34f)));
            var m = Simulate(sc);
            Key(m, "start", Start + 0.7f);
            Key(m, "accel", Start + 1.3f);
            Key(m, "top", Start + 6f);
            BallHold(m.Data, Start, Start + 10f, new Vec3(0f, 0.11f, -30f));
            return m;
        }

        /// <summary>② 급정지 후 컷.</summary>
        MotionScene StopCut()
        {
            var sc = new Script(this, "stopCut", 7f);
            sc.Actor(0, new Vec2(20f, 34f), E0, 15);
            sc.Cmd(0f, 0, SteeringCommand.Seek(new Vec2(95f, 34f)));
            sc.Cmd(3f, 0, k =>
            {
                var p = k.State(k.IndexOf(0)).Position;
                return SteeringCommand.Arrive(p + new Vec2(7f, 0f));
            });
            sc.Cmd(5f, 0, k =>
            {
                var p = k.State(k.IndexOf(0)).Position;
                return SteeringCommand.Seek(p + new Vec2(25f, 30f));
            });
            var m = Simulate(sc);
            Key(m, "decel", Start + 3.3f);
            Key(m, "plant", Start + 5.05f);
            Key(m, "exit", Start + 5.7f);
            BallHold(m.Data, Start, Start + 10f, new Vec3(0f, 0.11f, -30f));
            return m;
        }

        /// <summary>③ 180° 턴 → 백페달 → 사이드스텝 대치.</summary>
        MotionScene TurnBackSide()
        {
            var sc = new Script(this, "turnBackSide", 9f);
            sc.Actor(0, new Vec2(50f, 34f), E0, 14);
            sc.Cmd(0f, 0, SteeringCommand.Seek(new Vec2(80f, 34f), 3.4f));
            sc.Cmd(2f, 0, SteeringCommand.Seek(new Vec2(20f, 34f), 3.4f));
            sc.Cmd(4.5f, 0, SteeringCommand.Seek(new Vec2(10f, 34f), 2.2f));
            sc.Faces.Add((4.5f, 6.5f, 0, (float)(Math.PI * 0.5)));     // +X를 보며 −X로 물러남 (백페달)
            sc.Cmd(6.5f, 0, SteeringCommand.Seek(new Vec2(30f, 60f), 1.6f));
            sc.Faces.Add((6.5f, 9.1f, 0, (float)(Math.PI * 0.5)));     // +X를 보며 옆으로 (사이드스텝)
            var m = Simulate(sc);
            Key(m, "turn", Start + 2.2f);
            Key(m, "backpedal", Start + 5.5f);
            Key(m, "sidestep", Start + 7.8f);
            BallHold(m.Data, Start, Start + 10f, new Vec3(0f, 0.11f, -30f));
            return m;
        }

        /// <summary>④ 롱패스: 달려오다 공을 띄워 찬다.</summary>
        MotionScene LongPass()
        {
            var sc = new Script(this, "longPass", 5f);
            sc.Actor(0, new Vec2(30f, 30f), E0, 14);
            sc.Cmd(0f, 0, SteeringCommand.Seek(new Vec2(90f, 30f), 4.5f));
            sc.Cmd(2.5f, 0, k =>
            {
                var p = k.State(k.IndexOf(0)).Position;
                return SteeringCommand.Arrive(p + new Vec2(2f, 0f), 3f);
            });
            var m = Simulate(sc);
            float t = Start + 3f;
            var b = BallAtContact(m.Data, 0, t, "pass_lofted_r");
            var v = Dir((float)(Math.PI * 0.45), 20f, 9f);
            BallHold(m.Data, Start, t, b);
            BallFly(m.Data, t, Start + 5.1f, b, v);
            Ev(m.Data, t, SimEventKind.Pass, 0, -1, BodyPart.RightFoot, ActionOutcome.Success, b, v);
            Key(m, "plant", t - 0.1f);
            Key(m, "contact", t);
            Key(m, "after", t + AfterContact);
            return m;
        }

        /// <summary>⑤ 발리 슈팅: 날아오는 공을 무릎 아래 높이에서 찬다.</summary>
        MotionScene Volley()
        {
            var sc = new Script(this, "volley", 4f);
            sc.Actor(0, new Vec2(85f, 34f), E0, 15);
            sc.Cmd(0f, 0, SteeringCommand.Seek(new Vec2(92f, 34f), 1.2f));
            var m = Simulate(sc);
            float t = Start + 2f;
            var b = BallAtContact(m.Data, 0, t, "shot_volley_r");
            var v = Dir((float)(Math.PI * 0.5), 26f, 2f);
            BallLine(m.Data, Start, b + new Vec3(-2f, 2.2f, 14f), t, b);
            BallFly(m.Data, t, Start + 4.1f, b, v);
            Ev(m.Data, t, SimEventKind.Shot, 0, -1, BodyPart.RightFoot, ActionOutcome.Success, b, v);
            Key(m, "plant", t - 0.1f);
            Key(m, "contact", t);
            Key(m, "after", t + AfterContact);
            return m;
        }

        /// <summary>⑥ 슬라이딩 태클 성공(1→0) + 파울 태클(3→2).</summary>
        MotionScene Tackle()
        {
            var sc = new Script(this, "tackle", 8f);
            sc.Actor(0, new Vec2(30f, 20f), E0, 12);
            sc.Actor(1, new Vec2(26f, 18f), E0, 15);
            sc.Actor(2, new Vec2(60f, 48f), E0, 12);
            sc.Actor(3, new Vec2(56f, 50f), E0, 15);
            sc.Cmd(0f, 0, SteeringCommand.Seek(new Vec2(90f, 20f), 3.5f));
            sc.Cmd(0f, 1, SteeringCommand.Pursue(0, 5f));
            sc.Cmd(0f, 2, SteeringCommand.Seek(new Vec2(100f, 48f), 3.5f));
            sc.Cmd(0f, 3, SteeringCommand.Pursue(2, 5f));
            var m = Simulate(sc);
            float t1 = Start + 2.5f, t2 = Start + 5.5f;
            var d = m.Data;
            m.RawEdited = true;
            CloseIn(d, 1, 0, t1, 0.6f, 0.95f);
            CloseIn(d, 3, 2, t2, 0.6f, 0.86f);
            var b1 = BallAtContact(d, 1, t1, "tackle_win_r");
            var v1 = Dir((float)(Math.PI * 0.25), 6f);
            BallHold(d, Start, t1, b1);
            BallFly(d, t1, t2, b1, v1);
            Ev(d, t1, SimEventKind.Tackle, 1, 0, BodyPart.RightFoot, ActionOutcome.Win, b1, v1);
            var fp = d.Pos(2, t2);
            Ev(d, t2, SimEventKind.Tackle, 3, 2, BodyPart.RightFoot, ActionOutcome.Fail, BallAtContact(d, 3, t2, "tackle_slide_r"), default);
            Ev(d, t2, SimEventKind.Foul, 3, 2, BodyPart.RightFoot, ActionOutcome.Success, fp, default);
            Owner(d, Start, t1, 0);
            Key(m, "tackleWin", t1);
            Key(m, "tackleWinAfter", t1 + AfterContact);
            m.Focus["tackleWinAfter"] = 0;
            Key(m, "tackleFoul", t2);
            Key(m, "fall", t2 + 0.3f);
            m.Focus["tackleWin"] = 0;
            m.Focus["tackleFoul"] = 2;
            m.Focus["fall"] = 2;
            return m;
        }

        /// <summary>⑦ 헤딩 경합: 0 승, 1 패 (두 선수가 같이 점프).</summary>
        MotionScene Header()
        {
            var sc = new Script(this, "header", 4f);
            sc.Actor(0, new Vec2(70f, 34f), E0, 14);
            sc.Actor(1, new Vec2(70.9f, 34.1f), new Vec2(-1f, 0f), 14);
            var m = Simulate(sc);
            var d = m.Data;
            m.RawEdited = true;
            Hold(d, 0, 17.5f, 0f, (float)(Math.PI * 0.5));
            Hold(d, 1, 18.3f, 0.1f, -(float)(Math.PI * 0.5));
            float t = Start + 2f;
            var b = BallAtContact(d, 0, t, "header_jump");
            var v = Dir(-(float)(Math.PI * 0.4), 12f, 2f);
            BallLine(d, Start, b + new Vec3(10f, 3f, 12f), t, b);
            BallFly(d, t, Start + 4.1f, b, v);
            Ev(d, t, SimEventKind.Clearance, 0, 1, BodyPart.Head, ActionOutcome.Win, b, v);
            Key(m, "jump", t - 0.35f);
            Key(m, "contact", t);
            Key(m, "after", t + AfterContact);
            return m;
        }

        /// <summary>⑧ GK: 다이빙 선방(0), 이어서 실점(손 안 닿음).</summary>
        MotionScene Keeper()
        {
            var sc = new Script(this, "keeper", 8f);
            sc.Actor(0, new Vec2(1.5f, 34f), E0, 15);
            var m = Simulate(sc);
            var d = m.Data;
            float t1 = Start + 2f, t2 = Start + 5.5f;
            var b1 = BallAtContact(d, 0, t1, "gk_dive_l");
            var v1 = Dir((float)(Math.PI * 0.15), 9f, 3f);
            BallLine(d, Start, b1 + new Vec3(16f, 0.6f, -3f), t1, b1);
            BallFly(d, t1, t2 - 1f, b1, v1);
            Ev(d, t1, SimEventKind.Parry, 0, -1, BodyPart.Hand, ActionOutcome.Success, b1, v1); // 011: 접촉은 Parry·Catch·Punch, Save는 집계 이벤트
            // 실점: 반대편 구석으로 — 다이빙했지만 손이 닿지 않는다
            var goal = new Vec3(-_pitch.LengthM * 0.5f - 0.2f, 1.2f, 3f);
            BallLine(d, t2 - 1f, goal + new Vec3(18f, 0f, -2f), t2, goal);
            BallHold(d, t2, Start + 9f, goal);
            Ev(d, t2, SimEventKind.DiveStart, 0, -1, BodyPart.None, ActionOutcome.Fail, goal, default); // 011: 실점 다이빙 = 변환기가 예정 접촉 시각으로 옮긴 DiveStart
            Key(m, "save", t1);
            Key(m, "saveAfter", t1 + AfterContact);
            Key(m, "beaten", t2);
            return m;
        }

        // ---------- 경계 장면 (AC-18, FR-80~88) ----------

        /// <summary>FR-80: 시뮬이 005 한계를 넘는 궤적(0.1 s 순간 이동 1.5 m).</summary>
        MotionScene SimFault()
        {
            var sc = new Script(this, "simFault", 4f);
            sc.Actor(0, new Vec2(30f, 34f), E0, 14);
            sc.Cmd(0f, 0, SteeringCommand.Seek(new Vec2(90f, 34f), 4f));
            var m = Simulate(sc);
            m.ExpectSimFault = true;
            m.RawEdited = true;
            const int E = HighlightData.Entities;
            for (int f = 60; f < m.Data.Frames; f++) m.Data.Z[f * E] += 1.5f;
            BallHold(m.Data, Start, Start + 5f, new Vec3(0f, 0.11f, -30f));
            Key(m, "fault", Start + 2f);
            return m;
        }

        /// <summary>FR-81: 접촉까지 어떤 클립보다 짧은 급한 원터치 (공을 받은 직후 0.15 s).</summary>
        MotionScene OneTouch()
        {
            var sc = new Script(this, "oneTouch", 3f);
            sc.Actor(0, new Vec2(60f, 34f), E0, 14);
            sc.Cmd(0f, 0, SteeringCommand.Seek(new Vec2(90f, 34f), 3f));
            var m = Simulate(sc);
            var d = m.Data;
            float t1 = Start + 1.5f, t2 = t1 + 0.15f;
            var b1 = BallAtContact(d, 0, t1, "trap_foot_r");
            var b2 = BallAtContact(d, 0, t2, "onetouch_pass_r");
            var v2 = Dir((float)(Math.PI * 0.2), 12f);
            BallLine(d, Start, b1 + new Vec3(10f, 0f, 2f), t1, b1);
            BallLine(d, t1, b1, t2, b2);
            BallFly(d, t2, Start + 3.1f, b2, v2);
            Ev(d, t1, SimEventKind.PassReceived, 0, -1, BodyPart.RightFoot, ActionOutcome.Success, b1, default);
            Ev(d, t2, SimEventKind.Pass, 0, -1, BodyPart.RightFoot, ActionOutcome.Success, b2, v2);
            Key(m, "contact", t2);
            return m;
        }

        /// <summary>FR-82: 패스 직후 리바운드, 드리블 터치 연속.</summary>
        MotionScene ChainTouch()
        {
            var sc = new Script(this, "chainTouch", 5f);
            sc.Actor(0, new Vec2(40f, 34f), E0, 14);
            sc.Cmd(0f, 0, SteeringCommand.Seek(new Vec2(90f, 34f), 5f));
            var m = Simulate(sc);
            var d = m.Data;
            Vec3 prev = default;
            string[] clips = { "dribble_touch_r", "dribble_touch_l", "dribble_touch_r", "dribble_touch_l", "dribble_touch_r" };
            float t0 = Start + 1f;
            for (int i = 0; i < clips.Length; i++)
            {
                float t = t0 + i * 0.5f;
                var b = BallAtContact(d, 0, t, clips[i]);
                if (i == 0) BallHold(d, Start, t, b);
                else BallLine(d, t - 0.5f, prev, t, b);
                var part = clips[i].EndsWith("_r", StringComparison.Ordinal) ? BodyPart.RightFoot : BodyPart.LeftFoot;
                Ev(d, t, SimEventKind.Dribble, 0, -1, part, ActionOutcome.Success, b, Dir(d.YawAt(0, t), 6f));
                prev = b;
            }
            float last = t0 + (clips.Length - 1) * 0.5f;
            BallFly(d, last, Start + 5.1f, prev, Dir(d.YawAt(0, last), 6f));
            Owner(d, Start, last, 0);
            Key(m, "contact", t0 + 1f);
            return m;
        }

        /// <summary>FR-83: 같은 프레임에 두 선수가 공에 접촉, 0 승리.</summary>
        MotionScene SameFrame()
        {
            var sc = new Script(this, "sameFrame", 3f);
            sc.Actor(0, new Vec2(50f, 34f), E0, 14);
            sc.Actor(1, new Vec2(51.1f, 34.2f), new Vec2(-1f, 0f), 14);
            var m = Simulate(sc);
            var d = m.Data;
            m.RawEdited = true;
            Hold(d, 0, -2.5f, 0f, (float)(Math.PI * 0.5));
            Hold(d, 1, -1.6f, 0.2f, -(float)(Math.PI * 0.5));
            float t = Start + 1.5f;
            var b = BallAtContact(d, 0, t, "tackle_stand_r");
            var v = Dir(0f, 8f);
            BallHold(d, Start, t, b);
            BallFly(d, t, Start + 3.1f, b, v);
            Ev(d, t, SimEventKind.Tackle, 0, 1, BodyPart.RightFoot, ActionOutcome.Win, b, v);
            Ev(d, t, SimEventKind.Tackle, 1, 0, BodyPart.RightFoot, ActionOutcome.Fail, b, default);
            Key(m, "contact", t);
            return m;
        }

        /// <summary>FR-84: 장면 첫 프레임에 이미 고속.</summary>
        MotionScene FlyingStart()
        {
            var sc = new Script(this, "flyingStart", 3f);
            sc.Actor(0, new Vec2(20f, 34f), E0, 15, new Vec2(7f, 0f));
            sc.Cmd(0f, 0, SteeringCommand.Seek(new Vec2(95f, 34f)));
            var m = Simulate(sc);
            BallHold(m.Data, Start, Start + 5f, new Vec3(0f, 0.11f, -30f));
            Key(m, "first", Start);
            return m;
        }

        /// <summary>FR-85: 1.5 s에 엔티티 5 투입·엔티티 6 퇴장. 근처에 엔티티 7이 지나간다.</summary>
        MotionScene Substitution()
        {
            var sc = new Script(this, "substitution", 3f);
            sc.Actor(5, new Vec2(50f, 2f), new Vec2(0f, 1f), 12);
            sc.Actor(6, new Vec2(52f, 2.5f), new Vec2(0f, -1f), 12);
            sc.Actor(7, new Vec2(46f, 3.2f), E0, 13);     // 1.5 s에 투입 지점 옆을 지난다 (겹침 유도)
            sc.Cmd(0f, 5, SteeringCommand.Seek(new Vec2(50f, 20f), 1.5f));
            sc.Cmd(0f, 6, SteeringCommand.Seek(new Vec2(52f, 0.5f), 1.5f));
            sc.Cmd(0f, 7, SteeringCommand.Seek(new Vec2(60f, 3.2f), 4f));
            var m = Simulate(sc);
            var d = m.Data;
            const int E = HighlightData.Entities;
            m.RawEdited = true;
            for (int f = 0; f < d.Frames; f++)
            {
                if (f < 45) d.Present[f * E + 5] = false;
                if (f >= 45) d.Present[f * E + 6] = false;
            }
            BallHold(d, Start, Start + 5f, new Vec3(0f, 0.11f, -30f));
            Key(m, "swap", Start + 1.5f);
            return m;
        }

        /// <summary>FR-86: 골대 옆을 지나 골망 안으로 들어가 멈춘다.</summary>
        MotionScene InNet()
        {
            var sc = new Script(this, "inNet", 5f);
            sc.Actor(0, new Vec2(6f, 34f), new Vec2(-1f, 0f), 14);
            sc.Cmd(0f, 0, SteeringCommand.Arrive(new Vec2(-1f, 34f), 3f));
            var m = Simulate(sc);
            BallHold(m.Data, Start, Start + 6f, new Vec3(-_pitch.LengthM * 0.5f - 0.5f, 0.11f, 0f));
            Key(m, "inNet", Start + 4f);
            return m;
        }

        /// <summary>FR-22: 반경 약 10 m 곡선 질주 (AC-09). 목표점을 원 위로 옮겨 가며 쫓는다.</summary>
        MotionScene CurveRun()
        {
            var sc = new Script(this, "curveRun", 8f);
            sc.Actor(0, new Vec2(40f, 24f), E0, 16);
            var center = new Vec2(40f, 34f);
            const float radius = 10f;
            for (int i = 0; i < 80; i++)
            {
                float t = i * 0.1f, a = -(float)(Math.PI * 0.5) + 0.75f * t + 0.5f;
                sc.Cmd(t, 0, SteeringCommand.Seek(center + new Vec2((float)Math.Cos(a), (float)Math.Sin(a)) * radius));
            }
            var m = Simulate(sc);
            BallHold(m.Data, Start, Start + 9f, new Vec3(0f, 0.11f, -30f));
            Key(m, "curve", Start + 4f);
            return m;
        }

        /// <summary>FR-45: 맞달려 오는 두 선수가 005 Separation으로 비켜 간다.</summary>
        MotionScene Evade()
        {
            var sc = new Script(this, "evade", 5f);
            sc.Actor(0, new Vec2(40f, 34f), E0, 14);
            sc.Actor(1, new Vec2(60f, 34.3f), new Vec2(-1f, 0f), 14);
            sc.Cmd(0f, 0, SteeringCommand.Seek(new Vec2(70f, 34f), 5f));
            sc.Cmd(0f, 1, SteeringCommand.Seek(new Vec2(30f, 34f), 5f));
            var m = Simulate(sc);
            BallHold(m.Data, Start, Start + 6f, new Vec3(0f, 0.11f, -30f));
            Key(m, "pass", Start + 2f);
            return m;
        }

        /// <summary>AC-07: 나란히 달리며 어깨 몸싸움.</summary>
        MotionScene ShoulderDuel()
        {
            var sc = new Script(this, "shoulderDuel", 5f);
            sc.Actor(0, new Vec2(40f, 34f), E0, 14);
            sc.Actor(1, new Vec2(40f, 35f), E0, 14);
            sc.Cmd(0f, 0, SteeringCommand.Seek(new Vec2(90f, 34f), 5f));
            sc.Cmd(0f, 1, SteeringCommand.Seek(new Vec2(90f, 35f), 5f));
            var m = Simulate(sc);
            var d = m.Data;
            // Separation이 벌린 간격을 맞붙은 합성 간격으로
            m.RawEdited = true;
            const int E = HighlightData.Entities;
            for (int f = 0; f < d.Frames; f++)
            {
                d.X[f * E + 1] = d.X[f * E];
                d.Z[f * E + 1] = d.Z[f * E] + 0.42f;   // 005 접촉 반경 0.4 m 근처
                d.Yaw[f * E + 1] = d.Yaw[f * E];
            }
            float t = Start + 2.5f;
            Ev(d, t, SimEventKind.Duel, 0, 1, BodyPart.None, ActionOutcome.Win, d.Pos(0, t) + new Vec3(0f, 0.11f, 0f), default);
            BallHold(d, Start, Start + 6f, new Vec3(0f, 0.11f, -30f));
            Key(m, "duel", t);
            return m;
        }

        /// <summary>FR-86: 골대 기둥을 스치듯 지나 골망 안으로 (시뮬은 기둥을 모른다 — 화면 루트가 비켜 간다).</summary>
        MotionScene PostGraze()
        {
            var sc = new Script(this, "postGraze", 4f);
            sc.Actor(0, new Vec2(6f, 34f + 3.66f), new Vec2(-1f, 0f), 13);
            sc.Cmd(0f, 0, SteeringCommand.Seek(new Vec2(-1f, 34f + 3.66f), 3f));
            var m = Simulate(sc);
            BallHold(m.Data, Start, Start + 5f, new Vec3(-_pitch.LengthM * 0.5f - 0.5f, 0.11f, 0f));
            Key(m, "post", Start + 2.2f);
            return m;
        }

        /// <summary>FR-87: 가장 빠른 선수와 가장 느린 선수의 질주.</summary>
        MotionScene FastSlow()
        {
            var sc = new Script(this, "fastSlow", 8f);
            sc.Actor(0, new Vec2(10f, 20f), E0, 20);
            sc.Actor(1, new Vec2(10f, 48f), E0, 1);
            sc.Cmd(0f, 0, SteeringCommand.Seek(new Vec2(104f, 20f)));
            sc.Cmd(0f, 1, SteeringCommand.Seek(new Vec2(104f, 48f)));
            var m = Simulate(sc);
            BallHold(m.Data, Start, Start + 9f, new Vec3(0f, 0.11f, -30f));
            Key(m, "top", Start + 6f);
            return m;
        }
    }
}
