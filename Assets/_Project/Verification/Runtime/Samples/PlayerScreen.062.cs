using System;
using System.Collections.Generic;
using RoadToPro.Presentation.Highlight;
using RoadToPro.Presentation.Players;
using RoadToPro.Presentation.Players.Logic;
using UnityEngine;

namespace RoadToPro.Verification.Samples
{
    /// <summary>
    /// specs/062 T35 E2E 장면 (판정은 T37·육안 승인):
    /// 체형·손 클로즈업(AC-09) · 키트 움직임 0.5x 고정 시각 6개 + 배속별 같은 시각 포즈 해시(AC-10·14) · 헤어 색 일람(AC-11, 스타일 35는 057 hair1~7) ·
    /// 바탕 머리 8 × 3방향·지역별 3명·팀 11명 정렬(AC-11) · 표정 5 × 4명 + 반응(AC-12) · 품질 등급 전환 연속 캡처(AC-09 FR-85~89) · 정장(AC-15).
    /// </summary>
    public sealed partial class PlayerScreen
    {
        /// <summary>표정·바탕 머리·정장 장면 선수 (FacePlayers 앞 4명).</summary>
        static readonly int[] ExprPlayers = { 1, 2, 3, 4 };
        static readonly float[] KitTimes = { 0.6f, 0.9f, 1.2f, 1.5f, 1.8f, 2.1f };
        readonly HashSet<int> _over062 = new HashSet<int>();
        FaceData _faceData062;
        double _poseHash;
        bool _poseHashOn;

        FaceData Face062 => _faceData062 ??= FaceData.Parse(assets.body.faceJson.text);

        void Register062()
        {
            // 체형·손 클로즈업 (062-body-closeup)
            FarmContext.Screens["upperFront"] = () => ShowUpper(0f);
            FarmContext.Screens["upperThree"] = () => ShowUpper(35f);
            FarmContext.Screens["upperBack"] = () => ShowUpper(180f);
            FarmContext.Screens["handCloseup"] = ShowHand;
            for (int i = 0; i < 4; i++)
            {
                int k = i;
                FarmContext.Screens[$"tier{i + 1}"] = () => ShowTier(k);
            }
            // 키트 움직임 고정 시각·배속 (062-kit-motion)
            for (int i = 0; i < KitTimes.Length; i++)
            {
                int k = i;
                FarmContext.Screens[$"kitT{i + 1}"] = () => ShowKitAt(KitTimes[k], 0.5f);
            }
            foreach (var r in new[] { 0.5f, 1f, 1.5f, 3f })
            {
                float rate = r;
                FarmContext.Screens[$"kitRate{(int)(rate * 10)}"] = () => ShowKitAt(KitTimes[3], rate);
            }
            FarmContext.PerfCounters["poseHash"] = () => _poseHashOn ? _poseHash : (double?)null;
            // 헤어 색 (062-hair-catalog): 8색 × 대표 스타일, 컬러 포인트 3색
            FarmContext.Screens["hairColorsA"] = () => ShowHairColors(0);
            FarmContext.Screens["hairColorsB"] = () => ShowHairColors(1);
            FarmContext.Screens["hairColorPoint"] = () => ShowHairColors(2);
            // 얼굴 (062-face-catalog)
            for (int b = 0; b < AppearanceGenerator.BaseHeads; b++)
            {
                int k = b;
                FarmContext.Screens[$"base{b + 1}Front"] = () => ShowBase(k, 0f);
                FarmContext.Screens[$"base{b + 1}Three"] = () => ShowBase(k, 35f);
                FarmContext.Screens[$"base{b + 1}Side"] = () => ShowBase(k, 90f);
            }
            for (int r = 0; r < AppearanceGenerator.Regions; r++)
            {
                int k = r;
                FarmContext.Screens[$"region{r + 1}"] = () => ShowRegion(k);
            }
            FarmContext.Screens["team11"] = ShowTeam11;
            // 표정 (062-face-expressions)
            for (int e = 0; e < 5; e++)
            {
                var ex = (Expression)e;
                FarmContext.Screens[$"expr{ex}"] = () => ShowExpression(ex);
            }
            FarmContext.Screens["exprReaction"] = ShowReaction;
            // 정장 (062-suit)
            FarmContext.Screens["suitFront"] = () => ShowSuit(0f, false);
            FarmContext.Screens["suitThree"] = () => ShowSuit(35f, false);
            FarmContext.Screens["suitFace"] = () => ShowSuit(0f, true);
            // 성능 (062-model-perf, T36): 25명 재생 + 최고 품질 등급 3명(클로즈업 머리 2 + LOD0 고정 1, 표정·땀) + 손가락·보조 뼈·표정 갱신
            FarmContext.Screens["perfTier"] = ShowPerfTier;
            FarmContext.PerfCounters["playerTextureMB"] = () => _texMB > 0 ? _texMB : (double?)null;
        }

        double _texMB;

        /// <summary>FR-83: 선수가 쓰는 텍스처(공유 맵 + 팀 키트 아틀라스 + 마스크, 중복 제외) 런타임 메모리 합(MB).</summary>
        double PlayerTextureMB()
        {
            var seen = new HashSet<Texture>();
            long bytes = 0;
            foreach (var rig in _rigs)
            {
                if (rig == null) continue;
                foreach (var r in rig.GetComponentsInChildren<Renderer>(true))
                    foreach (var m in r.sharedMaterials)
                    {
                        if (m == null) continue;
                        foreach (int id in m.GetTexturePropertyNameIDs())
                        {
                            var t = m.GetTexture(id);
                            if (t != null && seen.Add(t)) bytes += UnityEngine.Profiling.Profiler.GetRuntimeMemorySizeLong(t);
                        }
                    }
            }
            return bytes / 1048576.0;
        }

        void ShowPerfTier()
        {
            Restore062();
            ShowPerf(1);
            _view = "perfTier";
            _sys.EnableMotionExtras(_kitMotion ??= assets.body.layoutJson != null ? KitSecondaryMotion.FromLayout(assets.body.layoutJson.text) : null);
            for (int i = 0; i < 3; i++)
            {
                int e = ExprPlayers[i];
                _rigs[e].SetCloseup(i < 2);
                _rigs[e].SetTopTier(i == 2);
                _rigs[e].SetSweat(1f);
                _rigs[e].ExpressionState.Set(i == 0 ? Expression.Cheer : Expression.Smile, -100f, 0.2f);
                _over062.Add(e);
            }
            _texMB = PlayerTextureMB();
            _closeupPerf = true;   // 090프레임마다 9·10번 클로즈업 머리 전환 비용(Players.CloseupSwitch)도 같이 잰다
        }

        void Prepare062(string view)
        {
            PrepareStill(view);
            if (_bodiesApplied) RestoreLooks();
            Restore062();
            _poseHashOn = false;
        }

        void Restore062()
        {
            if (_over062.Count == 0) return;
            foreach (int e in _over062)
            {
                if (_rigs[e].Outfit != PlayerOutfit.Uniform) _rigs[e].ApplyOutfit(PlayerOutfit.Uniform, 0);
                _rigs[e].ApplyAppearance(_looks[e], _faces[e], Face062);
                _rigs[e].SetExpression(new float[ExpressionLogic.Keys.Length]);
                _rigs[e].ExpressionState.Set(Expression.Neutral, -100f, 0.15f);
                _rigs[e].SetTopTier(false);
            }
            _over062.Clear();
        }

        void Apply062(int e, in PlayerAppearance a)
        {
            _rigs[e].ApplyAppearance(a, _faces[e], Face062);
            _over062.Add(e);
        }

        void Frame(Vector3 c, float height, float frac, float yawDeg = 0f, float lift = 0.03f)
        {
            float vHalf = cam.fieldOfView * 0.5f * Mathf.Deg2Rad;
            float dist = height / (frac * 2f * Mathf.Tan(vHalf));
            var dir = Quaternion.Euler(0f, yawDeg, 0f) * Vector3.forward;
            cam.transform.position = c + dir * dist + Vector3.up * lift;
            cam.transform.rotation = Quaternion.LookRotation(c - cam.transform.position, Vector3.up);
        }

        // ---------------------------------------------------------------- 체형·손 (AC-09)

        /// <summary>상반신 클로즈업 (근육·어깨·소매 경계·구운 디테일): 근육 최대 체형 선수 1명, 머리~허리가 화면 75 %.</summary>
        void ShowUpper(float yawDeg)
        {
            int ent = ExprPlayers[0];
            Prepare062($"upper{(yawDeg == 0f ? "Front" : yawDeg < 90f ? "Three" : "Back")}");
            var a = _looks[ent];
            Apply062(ent, new PlayerAppearance(a.HeightCm, a.WeightKg, a.Thin, 1f, a.Heavy, a.SkinTone, a.Face, a.HairStyle, a.HairColor,
                0.9f, 0.9f, a.ColorPoint, a.Beard, a.BaseA, a.BaseB, a.BaseC, a.WeightA, a.WeightB, a.WeightC));
            PoseStill(e => (e == ent, 0f, 0f, yawDeg * Mathf.Deg2Rad), false, 0);
            var rig = _rigs[ent];
            rig.SetTopTier(true);
            float s = rig.transform.localScale.y;
            Frame(rig.Bone("Chest").position + Vector3.up * 0.1f * s, 0.75f * s, 0.75f);
        }

        /// <summary>손 클로즈업 (5손가락·손등 힘줄·주름): 오른손이 화면 높이 40 %.</summary>
        void ShowHand()
        {
            int ent = ExprPlayers[0];
            Prepare062("handCloseup");
            PoseStill(e => (e == ent, 0f, 0f, 0.4f), false, 0);
            var rig = _rigs[ent];
            rig.SetTopTier(true);
            float s = rig.transform.localScale.y;
            // 손 중심(손목 뼈 + 손바닥 쪽 8 cm)을 선수 오른쪽 앞 비스듬히에서 — 손이 화면 높이 40 % (T37 캡처에서 손이 가장자리에 잘림)
            var hand = rig.Bone("RightHand");
            var c = hand.position + (hand.childCount > 0 ? (hand.GetChild(0).position - hand.position).normalized : Vector3.down) * 0.08f * s;
            float vHalf = cam.fieldOfView * 0.5f * Mathf.Deg2Rad;
            float dist = 0.2f * s / (0.4f * 2f * Mathf.Tan(vHalf));
            var dir = (rig.transform.right * 0.8f + rig.transform.forward * 0.6f).normalized;
            cam.transform.position = c + dir * dist + Vector3.up * 0.03f;
            cam.transform.rotation = Quaternion.LookRotation(c - cam.transform.position, Vector3.up);
        }

        /// <summary>
        /// 품질 등급 전환 (FR-85~89): 3명이 카메라로 다가오는 4단계 — 거리별로 QualityTierPolicy(061 TopTier 대신 가까운 3명)를 적용한다.
        /// 1 원거리(일반 LOD) · 2 중간(3명 LOD0 고정) · 3 근접(앞 2명 클로즈업 머리 + 3번째 LOD0 고정) · 4 더 근접.
        /// </summary>
        void ShowTier(int step)
        {
            Prepare062($"tier{step + 1}");
            int[] who = { ExprPlayers[0], ExprPlayers[1], ExprPlayers[2] };
            PoseStill(e =>
            {
                int i = Array.IndexOf(who, e);
                return (i >= 0, (i - 1f) * 0.75f, i == 1 ? 0.4f : 0f, 0f);
            }, false, -1);
            float[] dist = { 14f, 4.5f, 1.8f, 1.2f };
            var c = new Vector3(0f, 1.6f, 0f);
            cam.transform.position = c + new Vector3(0f, 0.05f, dist[step]);
            cam.transform.rotation = Quaternion.LookRotation(c - cam.transform.position, Vector3.up);
            var ratio = new float[_rigs.Length];
            var curC = new bool[_rigs.Length];
            var curP = new bool[_rigs.Length];
            var nc = new bool[_rigs.Length];
            var np = new bool[_rigs.Length];
            foreach (int e in who)
            {
                var rig = _rigs[e];
                ratio[e] = CloseupHeads.Ratio(cam, rig.Bone("Head").position, rig.transform.localScale.y);
                _over062.Add(e);
            }
            QualityTierPolicy.Select(ratio, step == 0 ? Array.Empty<int>() : who, curC, curP, nc, np);
            foreach (int e in who)
            {
                _rigs[e].SetCloseup(nc[e]);
                _rigs[e].SetTopTier(np[e]);
                if (!nc[e] && !np[e]) _rigs[e].UpdateLod(cam);
            }
        }

        // ---------------------------------------------------------------- 키트 움직임 (AC-10·14)

        /// <summary>0.5x 질주를 재생 시각 t에서 멈춰 상체 옆에서 본다. rate만 바꾼 캡처는 같은 시각 → 같은 포즈 해시(AC-14).</summary>
        void ShowKitAt(float sec, float rate)
        {
            ShowKitMotion(rate, (long)Math.Round(sec * 60.0 / rate));
            _view = rate == 0.5f && Array.IndexOf(KitTimes, sec) >= 0 && sec != KitTimes[3] ? $"kitT{Array.IndexOf(KitTimes, sec) + 1}" : $"kitRate{(int)(rate * 10)}";
            _poseHash = PoseHash(_rigs[0]);
            _poseHashOn = true;
        }

        static double PoseHash(PlayerRig rig)
        {
            ulong h = 1469598103934665603UL;
            foreach (var t in rig.GetComponentsInChildren<Transform>())
            {
                var q = t.localRotation;
                foreach (float f in new[] { q.x, q.y, q.z, q.w })
                {
                    int x = Mathf.RoundToInt(f * 1e5f);
                    unchecked
                    {
                        h ^= (uint)x;
                        h *= 1099511628211UL;
                    }
                }
            }
            return h & ((1UL << 52) - 1);
        }

        // ---------------------------------------------------------------- 헤어 색 (AC-11)

        /// <summary>0: 색 0~3 · 1: 색 4~7 (스타일 = 짧은 기본 컷 1, 중간 커튼 15) · 2: 컬러 포인트 3색 + 없음(페이드 5).</summary>
        void ShowHairColors(int set)
        {
            Prepare062(set == 0 ? "hairColorsA" : set == 1 ? "hairColorsB" : "hairColorPoint");
            for (int i = 0; i < 4; i++)
            {
                int e = ExprPlayers[i];
                var a = _looks[e];
                if (set < 2) Apply062(e, a.WithHair(i % 2 == 0 ? 1 : 15, set * 4 + i, 0));
                else Apply062(e, a.WithHair(5, 1, i));
            }
            Lineup4(0f, 0.55f);
        }

        void Lineup4(float yawDeg, float gap)
        {
            PoseStill(e =>
            {
                int i = Array.IndexOf(ExprPlayers, e);
                return (i >= 0, (i - 1.5f) * gap, 0f, yawDeg * Mathf.Deg2Rad);
            }, true, 0);
            foreach (int e in ExprPlayers) _rigs[e].SetCloseup(false);
            Frame(new Vector3(0f, 1.62f, 0f), 0.75f, 0.6f);
            float vHalf = cam.fieldOfView * 0.5f * Mathf.Deg2Rad;
            float hHalf = Mathf.Atan(Mathf.Tan(vHalf) * cam.aspect);
            float need = (1.5f * gap + 0.35f) / Mathf.Tan(hHalf);
            if (cam.transform.position.z < need) cam.transform.position = new Vector3(0f, 1.65f, need);
            cam.transform.rotation = Quaternion.LookRotation(new Vector3(0f, 1.62f, 0f) - cam.transform.position, Vector3.up);
        }

        // ---------------------------------------------------------------- 얼굴 (AC-11)

        /// <summary>바탕 머리 b만(가중치 1000) 입힌 얼굴 클로즈업 — 헤어 삭발, 수염 없음, 피부톤 = 지역 목록 가운데.</summary>
        void ShowBase(int b, float yawDeg)
        {
            int ent = ExprPlayers[0];
            Prepare062($"base{b + 1}{(yawDeg == 0f ? "Front" : yawDeg < 60f ? "Three" : "Side")}");
            var a = _looks[ent];
            var tones = AppearanceGenerator.SkinTonesOf(AppearanceGenerator.RegionOf(b));
            int o1 = (b + 1) % AppearanceGenerator.BaseHeads, o2 = (b + 2) % AppearanceGenerator.BaseHeads;
            var pure = new PlayerAppearance(a.HeightCm, a.WeightKg, a.Thin, a.Muscular, a.Heavy, tones[tones.Length / 2], a.Face, 9, a.HairColor,
                a.Broad, a.Limb, 0, 0, b, o1, o2, 1000, 0, 0);
            _rigs[ent].ApplyAppearance(pure, new FaceShape(new float[_faces[ent].Axis.Length]), Face062);
            _over062.Add(ent);
            PoseStill(e => (e == ent, 0f, 0f, yawDeg * Mathf.Deg2Rad), false, 0);
            var rig = _rigs[ent];
            rig.SetCloseup(true);
            float s = rig.transform.localScale.y;
            var head = rig.Bone("Head").position + new Vector3(0f, 0.09f * s, 0f);
            Frame(head, 0.24f * s, 0.45f, 0f, 0.02f);
        }

        /// <summary>지역 r 선수 3명 (주 바탕이 그 지역인 id를 찾아 생성값 그대로 — 혼합·피부톤·헤어·수염 포함).</summary>
        void ShowRegion(int r)
        {
            Prepare062($"region{r + 1}");
            int found = 0;
            for (int id = 10_000; found < 3 && id < 20_000; id++)
            {
                var a = AppearanceGenerator.Generate(new BodyInput(id));
                if (AppearanceGenerator.RegionOf(a.BaseA) != r) continue;
                int e = ExprPlayers[found++];
                _rigs[e].ApplyAppearance(a, FaceGenerator.Generate(Face062, id), Face062);
                _over062.Add(e);
            }
            PoseStill(e =>
            {
                int i = Array.IndexOf(ExprPlayers, e);
                return (i >= 0 && i < 3, (i - 1f) * 0.55f, 0f, 0f);
            }, true, 0);
            Frame(new Vector3(0f, 1.6f, 0f), 0.6f, 0.6f);
            float vHalf = cam.fieldOfView * 0.5f * Mathf.Deg2Rad;
            float hHalf = Mathf.Atan(Mathf.Tan(vHalf) * cam.aspect);
            float need = (0.55f + 0.35f) / Mathf.Tan(hHalf);
            if (cam.transform.position.z < need) cam.transform.position = new Vector3(0f, 1.63f, need);
        }

        /// <summary>팀 11명 정렬 (홈 0~10, AppearanceDistance.GenerateTeam 결과): 정면 반신 — 겉보기 중복 육안 확인(FR-65).</summary>
        void ShowTeam11()
        {
            Prepare062("team11");
            var inputs = new BodyInput[11];
            for (int i = 0; i < 11; i++) inputs[i] = _inputs[i];
            var looks = new PlayerAppearance[11];
            var faces = new FaceShape[11];
            AppearanceDistance.GenerateTeam(Face062, new FaceLandmarks(Face062), inputs, looks, faces);
            for (int e = 0; e < 11; e++)
            {
                _rigs[e].ApplyAppearance(looks[e], faces[e], Face062);
                _over062.Add(e);
            }
            const float gap = 0.62f;
            PoseStill(e => (e < 11, (e - 5f) * gap, 0f, 0f), true, 0);
            float vHalf = cam.fieldOfView * 0.5f * Mathf.Deg2Rad;
            float hHalf = Mathf.Atan(Mathf.Tan(vHalf) * cam.aspect);
            float dist = (5f * gap + 0.4f) / Mathf.Tan(hHalf);
            cam.transform.position = new Vector3(0f, 1.45f, dist);
            cam.transform.rotation = Quaternion.LookRotation(new Vector3(0f, 1.35f, 0f) - cam.transform.position, Vector3.up);
        }

        // ---------------------------------------------------------------- 표정 (AC-12)

        void SetExpr(int e, Expression ex)
        {
            var w = new float[ExpressionLogic.Keys.Length];
            if (ex != Expression.Neutral) w[(int)ex - 1] = 1f;
            _rigs[e].ExpressionState.Set(ex, -100f, 0.15f);   // 재생 시각과 상관없이 섞기 끝 상태
            _rigs[e].SetExpression(w);
            _over062.Add(e);
        }

        /// <summary>표정 하나를 4명(서로 다른 바탕·피부·수염)에게 — 얼굴 4개가 한 화면, 클로즈업 머리 2명 + LOD0 고정 2명.</summary>
        void ShowExpression(Expression ex)
        {
            Prepare062("expr" + ex);
            foreach (int e in ExprPlayers) SetExpr(e, ex);
            Lineup4(0f, 0.42f);
            for (int i = 0; i < ExprPlayers.Length; i++)
            {
                _rigs[ExprPlayers[i]].SetCloseup(i < 2);
                _rigs[ExprPlayers[i]].SetTopTier(i >= 2);
            }
        }

        /// <summary>반응 장면: expressions.json으로 득점자(환호)·동료(미소)·실점 팀(실망)·카드(집중) — 데이터 매핑 경로 확인.</summary>
        void ShowReaction()
        {
            Prepare062("exprReaction");
            var table = assets.expressionsJson != null ? ExpressionTable.Parse(assets.expressionsJson.text) : null;
            var trig = new[] { ReactionTrigger.Scorer, ReactionTrigger.Teammate, ReactionTrigger.ConcedingTeam, ReactionTrigger.Card };
            for (int i = 0; i < ExprPlayers.Length; i++)
                SetExpr(ExprPlayers[i], table != null ? table.Resolve(trig[i], out _) : Expression.Neutral);
            Lineup4(0f, 0.42f);
            foreach (int e in ExprPlayers) _rigs[e].SetTopTier(true);
            if (table == null || table.MissingCount > 0) Debug.LogWarning("[062] 표정 매핑이 없거나 빠졌다");
        }

        // ---------------------------------------------------------------- 정장 (AC-15)

        /// <summary>같은 선수(같은 외형·얼굴) 둘: 왼쪽 유니폼 · 오른쪽 정장. face = 얼굴 클로즈업(정장 쪽, 클로즈업 머리).</summary>
        void ShowSuit(float yawDeg, bool face)
        {
            Prepare062(face ? "suitFace" : yawDeg == 0f ? "suitFront" : "suitThree");
            int a = ExprPlayers[0], b = ExprPlayers[1];
            Apply062(b, _looks[a]);
            _rigs[b].ApplyAppearance(_looks[a], _faces[a], Face062);
            _rigs[b].ApplyOutfit(PlayerOutfit.Suit, (_inputs[a].Id & 0x7fffffff) % PlayerLook.SuitColors.Length);
            PoseStill(e => (e == a || e == b, e == a ? -0.45f : 0.45f, 0f, yawDeg * Mathf.Deg2Rad), !face, 0);
            if (face)
            {
                var rig = _rigs[b];
                rig.SetCloseup(true);
                float s = rig.transform.localScale.y;
                Frame(rig.Bone("Head").position + new Vector3(0f, -0.05f * s, 0f), 0.5f * s, 0.6f);
                return;
            }
            float vHalf = cam.fieldOfView * 0.5f * Mathf.Deg2Rad;
            float dist = 1.15f / Mathf.Tan(vHalf);
            cam.transform.position = new Vector3(0f, 1.05f, dist);
            cam.transform.rotation = Quaternion.LookRotation(new Vector3(0f, 0.95f, 0f) - cam.transform.position, Vector3.up);
        }
    }
}
