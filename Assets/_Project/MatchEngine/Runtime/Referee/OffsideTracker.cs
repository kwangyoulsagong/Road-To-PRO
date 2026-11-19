using System;
using RoadToPro.Core.Hashing;
using RoadToPro.Core.Math;
using RoadToPro.Core.Serialization;

namespace RoadToPro.MatchEngine.Referee
{
    /// <summary>공 터치 종류 (specs/012 FR-05): 의도적 플레이 → 리셋, 굴절·세이브 → 유지.</summary>
    public enum OffsideTouch : byte
    {
        Deliberate = 0,
        Deflection = 1,
        Save = 2,
    }

    /// <summary>오프사이드 관여 유형 (FR-04).</summary>
    public static class OffsideInvolvement
    {
        public const byte Play = 1;
        public const byte Opponent = 2;
        public const byte Gain = 3;
    }

    /// <summary>관여가 생긴 오프사이드 위치 후보 (진짜 d 기준, 부심 지각 전).</summary>
    public struct OffsideOffence
    {
        public int Slot;
        public byte Type;
        /// <summary>판정 시점(패스·슛 순간) 실제 마진 d (m).</summary>
        public float D;
        public float LineX;
        /// <summary>판정 시점 공격수 속도 (flash-lag 입력).</summary>
        public float Speed;
    }

    /// <summary>
    /// 오프사이드 위치 후보 추적 (specs/012 FR-02·04~06, IFAB Law 11). 패스(또는 슛, OFF-07) 순간 스냅샷으로 후보와 실제 d를 정하고,
    /// 다음 터치로 관여·리셋을 판정한다. 부심 지각(FR-07)은 호출자가 <see cref="Perception.AssistantPerceive"/>로. RNG 없음.
    /// 후보 = 상대 진영에 있고 d &gt; −<c>ref.off.candBack</c>인 공격수(패서 제외) — 온사이드 근처도 넣어 부심 오판이 가능하게 한다.
    /// </summary>
    public sealed class OffsideTracker
    {
        const int N = RefereeWorld.N;
        readonly float[] _d = new float[N];
        readonly float[] _speed = new float[N];
        bool _open;
        int _team = -1;
        int _passer = -1;
        bool _exempt;
        /// <summary>스냅샷 뒤 상대 굴절·세이브·골대 (관여 ③ 이득 취득).</summary>
        bool _rebound;
        float _lineX;
        long _tick = -1;

        public bool Open => _open;
        public int Team => _team;
        public float LineX => _lineX;
        public bool Exempt => _exempt;
        public bool IsCandidate(int slot) => _open && slot >= 0 && slot < N && !float.IsNaN(_d[slot]);
        public float MarginOf(int slot) => _d[slot];

        public void Close()
        {
            _open = false;
            _team = -1;
            _passer = -1;
            _rebound = false;
            _exempt = false;
        }

        /// <summary>
        /// 패스·슛 순간 스냅샷 (FR-02·06). runnerX = 22 슬롯 x(비활성 NaN), speed = 22 슬롯 속력. exempt = 스로인·골킥·코너에서 직접(FR-03).
        /// </summary>
        public void Snapshot(RefereeParams p, long tick, int attTeam, int passer, float ballX, ReadOnlySpan<float> runnerX, ReadOnlySpan<float> speed,
            int attackDir, float pitchL, bool exempt)
        {
            _open = true;
            _team = attTeam;
            _passer = passer;
            _exempt = exempt;
            _rebound = false;
            _tick = tick;
            var line = OffsideJudge.SecondLastDefenderX(runnerX, 1 - attTeam, attackDir, pitchL, p.OffsideBodyReach);
            var any = false;
            for (int s = 0; s < N; s++)
            {
                _d[s] = float.NaN;
                _speed[s] = 0f;
                if (s / RefereeWorld.PerTeam != attTeam || s == passer) continue;
                var x = runnerX[s];
                if (x - x != 0f) continue;
                var pos = OffsideJudge.Position(x, ballX, line, attackDir, pitchL, p.OffsideBodyReach);
                if (!pos.InOpponentHalf || pos.D <= -p.CandidateBack) continue;
                _d[s] = pos.D;
                _speed[s] = speed[s];
                _lineX = pos.LineX;
                any = true;
            }
            if (!any) _lineX = OffsideJudge.Position(float.NaN, ballX, line, attackDir, pitchL, p.OffsideBodyReach).LineX;
        }

        OffsideOffence Make(int slot, byte type)
        {
            var o = new OffsideOffence { Slot = slot, Type = type, D = _d[slot], LineX = _lineX, Speed = _speed[slot] };
            Close();
            return o;
        }

        /// <summary>
        /// 공 터치 (FR-04 ①·③, FR-05). 같은 팀 후보가 터치 → 관여(굴절·세이브 뒤면 ③ 이득, 아니면 ① 플레이). 같은 팀 비후보 터치 → 단계 끝.
        /// 상대 의도적 플레이 → 리셋, 상대 굴절·세이브 → 유지(이득 표시). 면제 스냅샷은 관여를 내지 않고 다음 터치에서 닫는다.
        /// </summary>
        public bool TryTouch(int slot, OffsideTouch kind, out OffsideOffence o)
        {
            o = default;
            if (!_open || slot < 0 || slot >= N) return false;
            if (slot / RefereeWorld.PerTeam == _team)
            {
                if (!_exempt && !float.IsNaN(_d[slot]))
                {
                    o = Make(slot, _rebound ? OffsideInvolvement.Gain : OffsideInvolvement.Play);
                    return true;
                }
                Close();
                return false;
            }
            if (kind == OffsideTouch.Deliberate) { Close(); return false; }
            _rebound = true;
            return false;
        }

        /// <summary>골대·심판 맞고 나온 공 (FR-04 ③ 이득 취득 조건).</summary>
        public void OnRebound() { if (_open) _rebound = true; }

        /// <summary>
        /// FR-04 ② 상대 관여 — 공 경합·가까운 공을 플레이하려 함: 상대가 공을 플레이하는 순간 그 지점 radius 안의 후보(가까운 순, 같으면 슬롯 순).
        /// </summary>
        public bool TryInterfere(RefereeParams p, Vec2 at, ReadOnlySpan<Vec2> pos, ReadOnlySpan<bool> active, out OffsideOffence o)
        {
            o = default;
            if (!_open || _exempt) return false;
            var best = -1;
            var bestD = p.InterfereRadius * p.InterfereRadius;
            for (int s = 0; s < N; s++)
            {
                if (float.IsNaN(_d[s]) || !active[s]) continue;
                var d2 = (pos[s] - at).LengthSq();
                if (d2 <= bestD) { bestD = d2; best = s; }
            }
            if (best < 0) return false;
            o = Make(best, OffsideInvolvement.Opponent);
            return true;
        }

        /// <summary>
        /// FR-04 ② OFF-09 GK 시야 방해: 같은 팀 슛 순간 슛 선(공 → 골 중앙) 폭 corridor 안이고 GK에서 sightGk 안인 후보.
        /// </summary>
        public bool TrySightBlock(RefereeParams p, int shooter, Vec2 from, Vec2 goal, Vec2 keeper, ReadOnlySpan<Vec2> pos, ReadOnlySpan<bool> active, out OffsideOffence o)
        {
            o = default;
            if (!_open || _exempt || shooter / RefereeWorld.PerTeam != _team) return false;
            var v = goal - from;
            var len = v.Length();
            if (len < 1e-3f) return false;
            var dir = v / len;
            for (int s = 0; s < N; s++)
            {
                if (s == shooter || float.IsNaN(_d[s]) || !active[s]) continue;
                var r = pos[s] - from;
                var along = r.X * dir.X + r.Y * dir.Y;
                if (along <= 0f || along >= len) continue;
                var perp = DMath.Abs(r.X * dir.Y - r.Y * dir.X);
                if (perp > p.SightCorridor) continue;
                if ((pos[s] - keeper).Length() > p.SightGkDist) continue;
                o = Make(s, OffsideInvolvement.Opponent);
                return true;
            }
            return false;
        }

        public void Write(StateWriter w)
        {
            w.Write(_open); w.Write(_team); w.Write(_passer); w.Write(_exempt); w.Write(_rebound); w.Write(_lineX); w.Write(_tick);
            for (int s = 0; s < N; s++) { w.Write(_d[s]); w.Write(_speed[s]); }
        }

        public void Read(StateReader r)
        {
            _open = r.ReadBool(); _team = r.ReadInt(); _passer = r.ReadInt(); _exempt = r.ReadBool(); _rebound = r.ReadBool(); _lineX = r.ReadFloat(); _tick = r.ReadLong();
            for (int s = 0; s < N; s++) { _d[s] = r.ReadFloat(); _speed[s] = r.ReadFloat(); }
        }

        public void AddTo(ref StableHasher h)
        {
            h.Add(_open); h.Add(_team); h.Add(_passer); h.Add(_exempt); h.Add(_rebound); h.Add(_lineX); h.Add(_tick);
            for (int s = 0; s < N; s++) { h.Add(_d[s]); h.Add(_speed[s]); }
        }
    }
}
