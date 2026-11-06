using System;
using System.Collections.Generic;
using RoadToPro.Core.Math;
using RoadToPro.MatchEngine.Match.Events;
using RoadToPro.Presentation.Players.Logic;
using RoadToPro.Presentation.Players.Logic.Motion;
using UnityEngine;

namespace RoadToPro.Presentation.Players.Motion
{
    /// <summary>지표 표본 한 개: 시뮬 시각·엔티티·값.</summary>
    public readonly struct MotionSample
    {
        public readonly float Time, Value;
        public readonly int Entity;

        public MotionSample(float time, int entity, float value)
        {
            Time = time;
            Entity = entity;
            Value = value;
        }
    }

    /// <summary>공 접촉 측정 한 건 (FR-30~33).</summary>
    public struct ContactRecord
    {
        public long Seq;
        public SimEventKind Kind;
        public int Entity;
        public string Clip;
        public BodyPart SimPart, ClipPart;
        public float Time, TimeError, Distance, DistanceLimit;
        public float RootError;
        /// <summary>킥 계열 디딤발: 공 옆·뒤 거리(m). IsKick이 false면 의미 없음.</summary>
        public bool IsKick;
        public float PlantSide, PlantBack;
        /// <summary>급한 원터치 (FR-81): 시간이 모자라 빠르게 재생. 디딤발 기준이 완화된다.</summary>
        public bool OneTouch;
        /// <summary>시뮬 판정이 빗나감(패자·실점): 닿으면 안 된다 (FR-44·83).</summary>
        public bool Missed;
    }

    public readonly struct MotionViolation
    {
        public readonly string Metric;
        public readonly float Time, Value, Limit;
        public readonly int Entity;

        public MotionViolation(string metric, float time, int entity, float value, float limit)
        {
            Metric = metric;
            Time = time;
            Entity = entity;
            Value = value;
            Limit = limit;
        }
    }

    /// <summary>한 장면 한 배속의 측정 결과 (FR-70·71).</summary>
    public sealed class MotionReport
    {
        public string Scene;
        public float Rate;
        public readonly Dictionary<string, List<MotionSample>> Metrics = new Dictionary<string, List<MotionSample>>(StringComparer.Ordinal);
        public readonly List<ContactRecord> Contacts = new List<ContactRecord>();
        public readonly List<string> ClipSequence = new List<string>();
        /// <summary>시뮬 한계 초과 엔티티 (FR-80).</summary>
        public readonly HashSet<int> SimFaultEntities = new HashSet<int>();

        public void Add(string metric, float t, int e, float v)
        {
            if (!Metrics.TryGetValue(metric, out var l)) Metrics[metric] = l = new List<MotionSample>();
            l.Add(new MotionSample(t, e, v));
        }

        public float Stat(string metric, float p)
        {
            if (!Metrics.TryGetValue(metric, out var l) || l.Count == 0) return 0f;
            var v = new List<float>(l.Count);
            foreach (var s in l) v.Add(s.Value);
            return MotionStats.Percentile(v, p);
        }

        public float Max(string metric) => Stat(metric, 100f);

        /// <summary>측정 값 해시 (AC-13). 프로필·해상도와 무관해야 한다.</summary>
        public ulong Hash()
        {
            var h = Core.Hashing.StableHasher.Create();
            var keys = new List<string>(Metrics.Keys);
            keys.Sort(StringComparer.Ordinal);
            foreach (var k in keys)
                foreach (var s in Metrics[k]) { h.Add(s.Time); h.Add(s.Entity); h.Add(s.Value); }
            foreach (var c in Contacts) { h.Add(c.Seq); h.Add(c.Time); h.Add(c.Distance); }
            return h.Value;
        }

        /// <summary>기준 위반 목록 (FR-71). 표본별 max 기준 + 통계 기준.</summary>
        public List<MotionViolation> Violations(MotionStandards std)
        {
            var o = new List<MotionViolation>();
            void Each(string metric, string id)
            {
                if (!Metrics.TryGetValue(metric, out var l)) return;
                var th = std[id];
                foreach (var s in l)
                    if (!th.Passes(s.Value)) o.Add(new MotionViolation(metric, s.Time, s.Entity, s.Value, s.Value > th.Max ? th.Max : th.Min));
            }
            void Pct(string metric, string id, float p)
            {
                float v = Stat(metric, p);
                if (v > std.Max(id)) o.Add(new MotionViolation(metric + "_p" + p, 0f, -1, v, std.Max(id)));
            }
            Each("rootError", "rootErrorMax");
            Pct("rootError", "rootErrorP95", 95f);
            Each("rootSpeedStep", "rootSpeedStep");
            Pct("yawError", "yawErrorP95", 95f);
            Pct("leanError", "leanErrorP95", 95f);
            Each("yawDelta", "yawFrameDelta");
            Each("slide", "slideMax");
            Pct("slide", "slideP95", 95f);
            Each("footHeight", "footHeight");
            Each("torsoOverlap", "torsoOverlap");
            Each("limbPenetration", "limbPenetration");
            Each("ballAfterContact", "ballAfterContact");
            Each("ballPull", "ballPull");
            Each("postPenetration", "postPenetration");
            foreach (var c in Contacts)
            {
                if (c.Missed)
                {
                    if (c.Distance < std.Min("loserBallGap")) o.Add(new MotionViolation("loserBallGap", c.Time, c.Entity, c.Distance, std.Min("loserBallGap")));
                    continue;
                }
                if (c.TimeError > std.Max("contactTime")) o.Add(new MotionViolation("contactTime", c.Time, c.Entity, c.TimeError, std.Max("contactTime")));
                if (c.Distance > c.DistanceLimit) o.Add(new MotionViolation("contactDistance", c.Time, c.Entity, c.Distance, c.DistanceLimit));
                if (c.RootError > std.Max("rootErrorContact")) o.Add(new MotionViolation("rootErrorContact", c.Time, c.Entity, c.RootError, std.Max("rootErrorContact")));
                if (c.IsKick)
                {
                    // 디딤발 (FR-33): 범위 밖은 급한 원터치로 표시된 것만 2배까지 (FR-81)
                    float relax = c.OneTouch ? std.Max("oneTouchRelax") : 1f;
                    var side = std["plantSide"];
                    var back = std["plantBack"];
                    if (c.PlantSide < side.Min / relax || c.PlantSide > side.Max * relax) o.Add(new MotionViolation("plantSide", c.Time, c.Entity, c.PlantSide, side.Max * relax));
                    if (c.PlantBack < back.Min / relax || c.PlantBack > back.Max * relax) o.Add(new MotionViolation("plantBack", c.Time, c.Entity, c.PlantBack, back.Max * relax));
                }
                if (c.SimPart != BodyPart.None && c.ClipPart != c.SimPart) o.Add(new MotionViolation("contactPart", c.Time, c.Entity, (float)c.ClipPart, (float)c.SimPart));
            }
            return o;
        }
    }

    /// <summary>
    /// 재생 측정기 (specs/054 FR-70·71). 실제 본 위치로 지표를 잰다. 값은 재생 시계(시뮬 초) 기준이라 렌더 해상도·프로필과 무관(AC-13).
    /// EditMode(수정 중)와 E2E(최종 판정)가 같은 코드를 쓴다. 검증 모드 전용 — 재생 경로에는 Diagnostics null 분기만 남는다.
    /// </summary>
    public sealed class MotionMeter : IMotionDiagnostics
    {
        readonly AnimPlan _plan;
        readonly PlayerRig[] _rigs;
        readonly int[] _actors;
        readonly float _rate, _fps;
        readonly MotionReport _r;
        readonly MotionStandards _std;
        readonly SlideTracker[] _slide;
        readonly Transform[] _toes;
        readonly Vec3[] _prevOff, _prevOffVel;
        readonly float[] _prevYaw;
        readonly bool[] _has;
        readonly BodyPose[] _pose;
        readonly string[] _lastClip;
        readonly Queue<float>[] _yawWindow;
        const float YawWindowSeconds = 0.5f;
        PoseSample _s;
        float _pt;
        bool _hasT;

        /// <summary>골대 기둥 끝점 쌍 (월드). 비어 있으면 측정 안 함 (FR-86).</summary>
        public Vec3[] Posts = Array.Empty<Vec3>();

        public MotionReport Report => _r;

        public MotionMeter(AnimPlan plan, PlayerRig[] rigs, int[] actors, MotionStandards std, string scene, float rate, float fps = 60f)
        {
            _fps = fps;
            _plan = plan;
            _rigs = rigs;
            _actors = actors;
            _std = std;
            _rate = rate;
            _r = new MotionReport { Scene = scene, Rate = rate };
            int n = actors.Length;
            _slide = new SlideTracker[n * 2];
            _toes = new Transform[n * 2];
            _prevOff = new Vec3[n];
            _prevOffVel = new Vec3[n];
            _prevYaw = new float[n];
            _has = new bool[n];
            _pose = new BodyPose[n];
            _lastClip = new string[n];
            _yawWindow = new Queue<float>[n];
            for (int i = 0; i < n; i++)
            {
                var rig = rigs[actors[i]];
                _toes[i * 2] = rig.Bone("LeftToes");
                _toes[i * 2 + 1] = rig.Bone("RightToes");
                _slide[i * 2] = new SlideTracker(std.Max("stanceHeight"), std.Max("stanceFootSpeed"));
                _slide[i * 2 + 1] = new SlideTracker(std.Max("stanceHeight"), std.Max("stanceFootSpeed"));
            }
        }

        static Vec3 V(Vector3 v) => new Vec3(v.x, v.y, v.z);

        static void ReadPose(PlayerRig r, ref BodyPose p)
        {
            Vec3 B(string n) => V(r.Bone(n).position);
            p.A0 = B("Hips"); p.B0 = B("Neck");
            p.A1 = B("Head"); p.B1 = p.A1 + new Vec3(0, 0.12f, 0);
            p.A2 = B("LeftUpperArm"); p.B2 = B("LeftHand");
            p.A3 = B("RightUpperArm"); p.B3 = B("RightHand");
            p.A4 = B("LeftUpperLeg"); p.B4 = B("LeftLowerLeg");
            p.A5 = B("RightUpperLeg"); p.B5 = B("RightLowerLeg");
            p.A6 = p.B4; p.B6 = B("LeftFoot");
            p.A7 = p.B5; p.B7 = B("RightFoot");
        }

        /// <summary>렌더 골반 정면 각 (라디안, 0 = +Z).</summary>
        static float PelvisYaw(PlayerRig r)
        {
            var d = r.Bone("RightUpperLeg").position - r.Bone("LeftUpperLeg").position;
            return (float)Math.Atan2(-d.z, d.x);
        }

        /// <summary>이 액션이 땅에 미끄러지거나 쓰러지는 동작이면 발 지지 기준 밖 (FR-10은 지지 발 기준).</summary>
        internal static bool GroundContactAction(ClipInfo c) =>
            c.Group == "fall" || c.Id == "tackle_slide_r" || c.Id == "tackle_win_r" || c.Id == "gk_dive_l" || c.Id == "gk_dive_r";

        bool InGroundAction(int e, float t)
        {
            var tl = _plan.Timeline(e);
            int k = tl.ActiveAt(t);
            return k >= 0 && GroundContactAction(_plan.Library.Get(tl.Get(k).Clip));
        }

        public void OnApplied(float t)
        {
            float dt = _hasT ? t - _pt : 0f;
            for (int i = 0; i < _actors.Length; i++)
            {
                int e = _actors[i];
                var rig = _rigs[e];
                _plan.Evaluate(e, t, ref _s);
                if (!_s.Present || !rig.gameObject.activeSelf)
                {
                    _has[i] = false;
                    continue;
                }
                // FR-80: 시뮬 한계 초과 엔티티는 시뮬 쪽 실패로 분류하고 054 지표에서 뺀다. 화면 추종 값만 따로 남긴다
                if (_plan.HasSimFault(e))
                {
                    _r.SimFaultEntities.Add(e);
                    _r.Add("simFaultError", t, e, _s.RootOffset.Length());
                    continue;
                }
                // 루트 오차 = 화면 루트와 시뮬 위치 차
                float err = _s.RootOffset.Length();
                _r.Add("rootError", t, e, err);
                // 보정 속도 변화 (FR-03)
                if (_has[i] && dt > 0)
                {
                    var vel = (_s.RootOffset - _prevOff[i]) / dt;
                    // 재생 시계 기준 60 fps 프레임 하나의 보정 속도 변화 (FR-03)
                    _r.Add("rootSpeedStep", t, e, (vel - _prevOffVel[i]).Length() * (1f / 60f) / dt);
                    _prevOffVel[i] = vel;
                }
                _prevOff[i] = _s.RootOffset;
                // 방향
                bool ground = InGroundAction(e, t);
                float yaw = _s.Yaw;
                if (!ground)
                {
                    // 렌더 골반 방향 vs 시뮬 (FR-05). 쓰러지는 동작은 골반이 몸 방향을 뜻하지 않아 뺀다 (spec "측정 제외")
                    // 걸음 때 골반 흔들림(055 FR 골반 회전)은 빼고 평균 방향 오차를 본다: 0.5 s 이동 평균
                    float signed = (float)Math.IEEERemainder(PelvisYaw(rig) - _plan.Data.YawAt(e, t), 2 * Math.PI) * Mathf.Rad2Deg;
                    var w = _yawWindow[i] ??= new Queue<float>();
                    w.Enqueue(signed);
                    int size = Mathf.Max(1, Mathf.RoundToInt(YawWindowSeconds * _fps / _rate));
                    while (w.Count > size) w.Dequeue();
                    float sum = 0f;
                    foreach (var q in w) sum += q;
                    if (w.Count == size) _r.Add("yawError", t, e, Math.Abs(sum / size));
                }
                if (_has[i] && dt > 0)
                {
                    // 재생 시계 기준: 60 fps 1배속 프레임 하나의 각도로 환산 (FR-25). 예외 없음(AC-03)
                    float d = (float)Math.Abs(Math.IEEERemainder(yaw - _prevYaw[i], 2 * Math.PI)) * Mathf.Rad2Deg;
                    _r.Add("yawDelta", t, e, d * (1f / 60f) / dt);
                }
                _prevYaw[i] = yaw;
                MeasureLean(e, t, rig);
                _has[i] = true;
                // 발
                for (int k = 0; k < 2; k++)
                {
                    var p = _toes[i * 2 + k].position;
                    if (!ground)
                    {
                        if (_slide[i * 2 + k].Add(t, p.x, p.y, p.z, out var sl)) _r.Add("slide", t, e, sl);
                        if (_slide[i * 2 + k].InStance) _r.Add("footHeight", t, e, p.y);
                    }
                    else if (_slide[i * 2 + k].Finish(out var sl2)) _r.Add("slide", t, e, sl2);
                }
                ReadPose(rig, ref _pose[i]);
                // 클립 시퀀스 (AC-13)
                // 가중치가 가장 큰 클립 (전환·로코모션 포함, FR-70)
                int top = 0;
                for (int c = 1; c < _s.Count; c++)
                    if (_s.Weight(c) > _s.Weight(top)) top = c;
                var id = _s.Count > 0 ? _plan.Library.Get(_s.Clip(top)).Id : "none";
                if (id != _lastClip[i]) { _lastClip[i] = id; _r.ClipSequence.Add(e + ":" + id); }
            }
            // 선수 간 (FR-40)
            for (int i = 0; i < _actors.Length; i++)
            {
                if (!_has[i]) continue;
                for (int j = i + 1; j < _actors.Length; j++)
                {
                    if (!_has[j]) continue;
                    var dh = _pose[i].A0 - _pose[j].A0;
                    if (dh.X * dh.X + dh.Z * dh.Z > 9f) continue;
                    _r.Add("torsoOverlap", t, _actors[i], Math.Max(0f, BodyCapsules.TorsoOverlap(_pose[i], _pose[j])));
                    _r.Add("limbPenetration", t, _actors[i], Math.Max(0f, BodyCapsules.LimbPenetration(_pose[i], _pose[j])));
                }
                for (int k = 0; k + 1 < Posts.Length; k += 2)
                    _r.Add("postPenetration", t, _actors[i], Math.Max(0f, BodyCapsules.Radius(0) + 0.06f
                        - BodyCapsules.SegmentDistance(_pose[i].A0, _pose[i].B0, Posts[k], Posts[k + 1])));
            }
            // 공 (FR-35)
            var sim = _plan.BallKeyed(t);
            var vis = _plan.BallVisual(t);
            float gap = (vis - sim).Length();
            bool pulled = false;
            foreach (var ev in _plan.Data.Events)
                if (t < ev.Time && t > ev.Time - BallVisualCorrection.BallPullTime) pulled = true;
            _r.Add(pulled ? "ballPull" : "ballAfterContact", t, -1, gap);
            _pt = t;
            _hasT = true;
        }

        /// <summary>곡선 질주 몸 기울기 오차 (FR-22, AC-09): 액션 없이 횡가속이 큰 구간만.</summary>
        void MeasureLean(int e, float t, PlayerRig rig)
        {
            var tr = _plan.Track;
            _plan.Data.FrameAt(t, out var f, out _);
            float lat = tr.AccelLat(f, e);
            // 곡선 주행 횡가속 범위만 (005 kin.lat 상한 7 m/s²). Separation의 순간 밀침은 곡선이 아니다
            if (Math.Abs(lat) < LeanMinAccel || Math.Abs(lat) > LeanMaxAccel || tr.Speed(f, e) < LeanMinSpeed || _plan.Timeline(e).ActiveAt(t) >= 0) return;
            // 0.4 s 이상 이어지는 곡선만: 잠깐 비켜 가는 움직임은 자세 평활 창보다 짧다
            for (int g = Math.Max(0, f - LeanSustain); g <= Math.Min(tr.Frames - 1, f + LeanSustain); g++)
            {
                float v = tr.AccelLat(g, e);
                if (Math.Abs(v) < LeanMinAccel || v * lat < 0f) return;
            }
            var up = rig.Bone("Neck").position - rig.Bone("Hips").position;
            float roll = (float)Math.Atan2(Vector3.Dot(up, rig.transform.right), up.y);
            _r.Add("leanError", t, e, Math.Abs(roll - MotionTrack.LeanFor(lat)) * Mathf.Rad2Deg);
        }

        const float LeanMinAccel = 2f, LeanMaxAccel = 7f, LeanMinSpeed = 3f;
        const int LeanSustain = 6;

        /// <summary>장면 끝 처리: 진행 중 접지를 닫는다.</summary>
        public void Finish()
        {
            for (int i = 0; i < _actors.Length; i++)
                for (int k = 0; k < 2; k++)
                    if (_slide[i * 2 + k].Finish(out var sl))
                        _r.Add("slide", _pt, _actors[i], sl);
        }

        float DistanceLimit(BodyPart p) =>
            p switch
            {
                BodyPart.Head => _std.Max("contactDistHead"),
                BodyPart.Chest => _std.Max("contactDistBody"),
                BodyPart.Thigh => _std.Max("contactDistBody"),
                BodyPart.Hand => _std.Max("contactDistHand"),
                _ => _std.Max("contactDistFoot"),
            };

        static Vec3 RulePoint(PlayerRig rig, in ContactRule r)
        {
            var bone = V(rig.Bone(r.Bone).position);
            var toward = r.Toward != null ? V(rig.Bone(r.Toward).position) : bone;
            return r.Evaluate(bone, toward, V(rig.Bone("LeftUpperLeg").position), V(rig.Bone("RightUpperLeg").position), rig.transform.lossyScale.y);
        }

        /// <summary>
        /// 공 접촉 측정: 이벤트마다 접촉 시각으로 포즈를 적용해 접촉 부위·거리·디딤발을 잰다.
        /// apply = 시스템 ApplyAt. 재생 측정이 끝난 뒤 부른다 (측정 순서가 포즈를 바꾸지 않는다: 포즈 = f(t)).
        /// </summary>
        public void MeasureContacts(Action<float> apply)
        {
            var d = _plan.Data;
            foreach (var ev in d.Events)
            {
                foreach (var e in _actors)
                {
                    var tl = _plan.Timeline(e);
                    for (int k = 0; k < tl.Count; k++)
                    {
                        if (tl.SeqOf(k) != ev.Seq) continue;
                        var a = tl.Get(k);
                        var c = _plan.Library.Get(a.Clip);
                        if (!c.HasContact) continue;
                        apply(ev.Time);
                        var rig = _rigs[e];
                        var cp = RulePoint(rig, c.Rule);
                        _plan.Evaluate(e, ev.Time, ref _s);
                        var rec = new ContactRecord
                        {
                            Seq = ev.Seq, Kind = ev.Kind, Entity = e, Clip = c.Id, SimPart = ev.Part, ClipPart = c.ContactPart,
                            // 시각 차 = 클립 접촉 시각 차 + 렌더 프레임 격자에서 가장 가까운 프레임까지(재생 시계 기준, FR-30)
                            Time = ev.Time, TimeError = Math.Abs(a.ContactAt - ev.Time) + 0.5f * _rate / _fps,
                            Distance = (cp - _plan.BallVisual(ev.Time - 1e-3f)).Length(), DistanceLimit = DistanceLimit(c.ContactPart),
                            Missed = AnimPlan.MissesBall(ev) || e != ev.Entity,
                            RootError = _s.RootOffset.Length(), OneTouch = a.Rate > 1.001f,
                        };
                        // 킥 계열 땅볼만 디딤발 기준 (055 FR-60a). 측정식은 055 gait_metrics와 같다: 발 중심 → 공, 옆은 공 반지름·발 반폭을 뺀 거리
                        bool kick = ev.Kind == SimEventKind.Pass || ev.Kind == SimEventKind.Shot || ev.Kind == SimEventKind.Cross;
                        if (kick && c.BallHeightMin < 0.3f && (c.ContactPart == BodyPart.RightFoot || c.ContactPart == BodyPart.LeftFoot))
                        {
                            rec.IsKick = true;
                            string pn = c.ContactPart == BodyPart.RightFoot ? "Left" : "Right";
                            var ankle = V(rig.Bone(pn + "Foot").position);
                            var toes = V(rig.Bone(pn + "Toes").position);
                            var pc = (ankle + toes + (toes - ankle) * 0.4f) * 0.5f;
                            var hipL = V(rig.Bone("LeftUpperLeg").position);
                            var hipR = V(rig.Bone("RightUpperLeg").position);
                            var r = hipR - hipL;
                            var f = new Vec3(-r.Z, 0, r.X).Normalized();
                            var side = new Vec3(f.Z, 0, -f.X);
                            var rel = cp - pc;
                            // 기준 키(1.80 m)로 환산: 055 FR-60 범위는 기준 키 측정값이다
                            float s = rig.transform.lossyScale.y;
                            rec.PlantBack = Vec3.Dot(rel, f) / s;
                            rec.PlantSide = Math.Abs(Vec3.Dot(rel, side)) / s - 0.11f - 0.045f;
                        }
                        _r.Contacts.Add(rec);
                    }
                }
            }
        }
    }
}
