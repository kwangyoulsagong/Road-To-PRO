using System;
using NUnit.Framework;
using RoadToPro.Presentation.Players.Logic;
using UnityEditor;
using UnityEngine;

namespace RoadToPro.Tests.EditMode.Presentation.Players
{
    /// <summary>
    /// 표정 선택·섞기 (specs/062 FR-70·71, AC-08·14). T29가 구현할 API (Logic, Unity 비참조):
    /// <c>Expression</c> { Neutral, Cheer, Smile, Disappoint, Focus } · <c>ExpressionLogic.Keys</c> (Neutral 제외 4개 셰이프 키 이름, Expr_*),
    /// <c>ReactionTrigger</c> — 060 반응(득점자·동료·실점 팀)·SimEvent(골·실점·찬스 무산·카드)의 **임시 열거형**(060 연결 때 재확인, plan),
    /// <c>ExpressionTable.Parse(json)</c> · <c>Resolve(trigger, out blendSec)</c> (빠진 항목 → Neutral, <c>MissingCount</c>는 종류마다 1회),
    /// <c>ExpressionBlend</c> (재생 시각 기준 0.15~0.3 s 섞기, 상태 = 마지막 Set 하나), <c>ExpressionLogic.LodScale(lod)</c> (LOD1 이하 0).
    /// 데이터: Assets/_Project/Presentation/Players/Data/expressions.json
    /// </summary>
    public class ExpressionLogicTests
    {
        const string DataPath = "Assets/_Project/Presentation/Players/Data/expressions.json";

        static ExpressionTable Table()
        {
            var t = AssetDatabase.LoadAssetAtPath<TextAsset>(DataPath);
            Assert.IsNotNull(t, DataPath);
            return ExpressionTable.Parse(t.text);
        }

        [Test]
        public void Mapping_Complete_MissingFallsBackNeutral()
        {
            // 데이터 표: 모든 반응·이벤트가 매핑돼 빠진 항목 0, 섞기 시간 0.15~0.3 s
            Assert.AreEqual(new[] { "Expr_cheer", "Expr_smile", "Expr_disappoint", "Expr_focus" }, ExpressionLogic.Keys);
            var table = Table();
            foreach (ReactionTrigger tr in Enum.GetValues(typeof(ReactionTrigger)))
            {
                var e = table.Resolve(tr, out float blend);
                Assert.That(blend, Is.InRange(0.15f, 0.3f), tr.ToString());
                Assert.That((int)e, Is.InRange(0, 4), tr.ToString());
            }
            Assert.AreEqual(0, table.MissingCount, "빠진 매핑");
            Assert.AreEqual(Expression.Cheer, table.Resolve(ReactionTrigger.Scorer, out _), "득점자 = 환호");
            Assert.AreEqual(Expression.Disappoint, table.Resolve(ReactionTrigger.ConcedingTeam, out _), "실점 팀 = 실망");

            // 일부러 빠진 표: 없는 종류는 무표정 + 진단 1회 (같은 종류를 여러 번 물어도 1)
            var partial = ExpressionTable.Parse("{\"schemaVersion\":1,\"defaultBlend\":0.2,\"map\":[{\"trigger\":\"Scorer\",\"expression\":\"Cheer\"}]}");
            Assert.AreEqual(Expression.Neutral, partial.Resolve(ReactionTrigger.Card, out float b2));
            Assert.AreEqual(Expression.Neutral, partial.Resolve(ReactionTrigger.Card, out _));
            Assert.AreEqual(Expression.Neutral, partial.Resolve(ReactionTrigger.Goal, out _));
            Assert.AreEqual(2, partial.MissingCount, "종류마다 1회");
            Assert.That(b2, Is.InRange(0.15f, 0.3f));
            Assert.AreEqual(Expression.Cheer, partial.Resolve(ReactionTrigger.Scorer, out _));
        }

        [Test]
        public void Blend_PlaybackTime()
        {
            // FR-71: 재생 시각 기준 — 같은 시각이면 호출 순서·프레임률과 상관없이 같은 가중치, 시작엔 이전 표정 그대로, 섞기 끝엔 새 표정 1
            var w = new float[ExpressionLogic.Keys.Length];
            var w2 = new float[ExpressionLogic.Keys.Length];
            var b = new ExpressionBlend();
            b.Set(Expression.Smile, 10f, 0.2f);
            b.Weights(10.5f, w);
            Assert.AreEqual(1f, w[1], 1e-6f, "미소 완전히");
            b.Set(Expression.Cheer, 11f, 0.2f);
            b.Weights(11f, w);
            Assert.AreEqual(1f, w[1], 1e-6f, "전환 시작 = 이전(미소) 그대로");
            Assert.AreEqual(0f, w[0], 1e-6f);
            b.Weights(11.1f, w);
            Assert.That(w[0], Is.InRange(0.01f, 0.99f), "중간엔 섞인다");
            Assert.AreEqual(1f, w[0] + w[1], 1e-5f, "합 1 (교차 섞기)");
            b.Weights(11.2f, w);
            Assert.AreEqual(1f, w[0], 1e-6f, "끝 = 환호 완전히");
            Assert.AreEqual(0f, w[1], 1e-6f);
            // 같은 시각 = 같은 값 (역순·반복 평가)
            b.Weights(11.07f, w);
            b.Weights(11.15f, w2);
            b.Weights(11.07f, w2);
            CollectionAssert.AreEqual(w, w2);
            // 섞기 시간은 0.15~0.3 s로 자른다
            var c = new ExpressionBlend();
            c.Set(Expression.Focus, 0f, 5f);
            c.Weights(0.3f, w);
            Assert.AreEqual(1f, w[3], 1e-6f, "0.3 s 넘는 섞기는 0.3 s");
        }

        [Test]
        public void Lod1_Neutral()
        {
            // FR-71: 표정은 LOD0·클로즈업만, LOD1 이하는 무표정 고정
            Assert.AreEqual(1f, ExpressionLogic.LodScale(0));
            Assert.AreEqual(0f, ExpressionLogic.LodScale(1));
            Assert.AreEqual(0f, ExpressionLogic.LodScale(2));
        }
    }
}
