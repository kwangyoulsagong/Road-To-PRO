using System;
using System.Collections.Generic;
using RoadToPro.Core.Serialization;

namespace RoadToPro.Presentation.Players.Logic
{
    /// <summary>062 FR-70: 표정 5종 (무표정 = 셰이프 키 0).</summary>
    public enum Expression { Neutral = 0, Cheer = 1, Smile = 2, Disappoint = 3, Focus = 4 }

    /// <summary>
    /// 표정을 고르는 반응·이벤트 (FR-70). 060 세리머니 반응(득점자·동료·실점 팀) + SimEvent(골·실점·찬스 무산·카드)의
    /// **임시 열거형** — 060이 아직 구현 전이라 이 스펙에 두고, 060 연결 때 재확인한다(plan '위험').
    /// </summary>
    public enum ReactionTrigger { Scorer, Teammate, ConcedingTeam, Goal, Conceded, ChanceMissed, Card }

    /// <summary>표정 셰이프 키 이름·LOD 규칙 (FR-71).</summary>
    public static class ExpressionLogic
    {
        /// <summary>무표정 제외 4개, Expression 1~4 순서 = 구운 메시 블렌드셰이프 1~4번 (0번 = HideHead).</summary>
        public static readonly string[] Keys = { "Expr_cheer", "Expr_smile", "Expr_disappoint", "Expr_focus" };

        public const float MinBlend = 0.15f, MaxBlend = 0.3f;

        /// <summary>FR-71: 표정은 LOD0·클로즈업만, LOD1 이하 무표정.</summary>
        public static float LodScale(int lod) => lod == 0 ? 1f : 0f;

        public static float ClampBlend(float s) => s < MinBlend ? MinBlend : s > MaxBlend ? MaxBlend : s;

        /// <summary>088 T30j: 표정별 입 벌림(치아가 빛을 받는 정도) — 생성기 face.py 표정: cheer만 mouth-open 0.85, smile은 입꼬리 [제안].</summary>
        public const float CheerMouthOpen = 1f, SmileMouthOpen = 0.4f;

        /// <summary>
        /// 088 T30j: 표정 가중치(Keys 순서) → 입 벌림 0~1. 셰이더가 치아 빛을 lerp(TeethOcc, 1, 이 값)으로 줄인다 —
        /// 무표정 입술 틈으로 보이는 윗니 끝이 그늘 없이 하늘빛을 받아 흰 줄로 보였다(클로즈업 얼굴은 그림자 맵을 안 받음, T30h).
        /// </summary>
        public static float MouthOpen(ReadOnlySpan<float> w)
        {
            float v = (w.Length > 0 ? w[0] * CheerMouthOpen : 0f) + (w.Length > 1 ? w[1] * SmileMouthOpen : 0f);
            return v < 0f ? 0f : v > 1f ? 1f : v;
        }
    }

    /// <summary>
    /// 반응·이벤트 → 표정 표 (데이터 Presentation/Players/Data/expressions.json):
    /// <c>{ "schemaVersion": 1, "defaultBlend": 0.2, "map": [ { "trigger": "Scorer", "expression": "Cheer", "blend": 0.2 } ] }</c>.
    /// 빠진 종류는 무표정 + 진단(종류마다 1회 — MissingCount, OnMissing).
    /// </summary>
    public sealed class ExpressionTable
    {
        readonly Expression[] _expr;
        readonly float[] _blend;
        readonly bool[] _has, _reported;
        readonly float _default;
        public int MissingCount { get; private set; }

        /// <summary>빠진 매핑 진단 (종류마다 1회). Unity 쪽에서 로그를 붙인다.</summary>
        public Action<ReactionTrigger> OnMissing;

        ExpressionTable(float defaultBlend)
        {
            int n = Enum.GetValues(typeof(ReactionTrigger)).Length;
            _expr = new Expression[n];
            _blend = new float[n];
            _has = new bool[n];
            _reported = new bool[n];
            _default = ExpressionLogic.ClampBlend(defaultBlend);
        }

        public static ExpressionTable Parse(string json)
        {
            var root = (JsonObject)JsonReader.Parse(json);
            if (!root.TryGet("schemaVersion", out var sv) || (double)sv != 1) throw new FormatException("expressions.json schemaVersion 1 아님");
            float def = root.TryGet("defaultBlend", out var db) ? (float)(double)db : 0.2f;
            var t = new ExpressionTable(def);
            if (root.TryGet("map", out var mp) && mp is List<object> list)
                foreach (var x in list)
                {
                    var o = (JsonObject)x;
                    o.TryGet("trigger", out var tr);
                    o.TryGet("expression", out var ex);
                    if (!Enum.TryParse((string)tr, out ReactionTrigger trig)) throw new FormatException("expressions.json 모르는 trigger: " + tr);
                    if (!Enum.TryParse((string)ex, out Expression e)) throw new FormatException("expressions.json 모르는 expression: " + ex);
                    int i = (int)trig;
                    t._expr[i] = e;
                    t._blend[i] = o.TryGet("blend", out var bl) ? ExpressionLogic.ClampBlend((float)(double)bl) : t._default;
                    t._has[i] = true;
                }
            return t;
        }

        public Expression Resolve(ReactionTrigger trigger, out float blendSec)
        {
            int i = (int)trigger;
            if (_has[i])
            {
                blendSec = _blend[i];
                return _expr[i];
            }
            if (!_reported[i])
            {
                _reported[i] = true;
                MissingCount++;
                OnMissing?.Invoke(trigger);
            }
            blendSec = _default;
            return Expression.Neutral;
        }
    }

    /// <summary>
    /// 표정 교차 섞기 (FR-71). 상태 = 마지막 Set 하나(이전 표정·새 표정·시작 시각·길이) → 가중치는 f(재생 시각)라
    /// 같은 시각이면 호출 순서·프레임률과 상관없이 같다 (AC-14). 할당 없음.
    /// </summary>
    public sealed class ExpressionBlend
    {
        Expression _from = Expression.Neutral, _to = Expression.Neutral;
        float _t0, _dur = ExpressionLogic.MinBlend;

        public Expression Current => _to;

        /// <summary>재생 시각 t부터 blendSec(0.15~0.3 s로 자름) 동안 지금 표정 → e.</summary>
        public void Set(Expression e, float t, float blendSec)
        {
            _from = _to;
            _to = e;
            _t0 = t;
            _dur = ExpressionLogic.ClampBlend(blendSec);
        }

        /// <summary>w(길이 ≥ Keys.Length)에 표정 키 가중치 0~1을 쓴다.</summary>
        public void Weights(float t, Span<float> w)
        {
            for (int i = 0; i < ExpressionLogic.Keys.Length; i++) w[i] = 0f;
            float a = (t - _t0) / _dur;
            a = a <= 0f ? 0f : a >= 1f ? 1f : a * a * (3f - 2f * a);
            if (_from != Expression.Neutral) w[(int)_from - 1] += 1f - a;
            if (_to != Expression.Neutral) w[(int)_to - 1] += a;
        }
    }
}
