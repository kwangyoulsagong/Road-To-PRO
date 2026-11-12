using System;
using System.Collections.Generic;
using RoadToPro.Core.Math;
using RoadToPro.Core.Pitch;
using RoadToPro.MatchEngine.Ball;
using RoadToPro.MatchEngine.Kinematics;

namespace RoadToPro.MatchEngine.Match
{
    /// <summary>경기 선수 입력. 능력치는 002 전까지 005 운동 능력치(내부 0~100)를 직접 받는다.</summary>
    public readonly struct MatchPlayer
    {
        public readonly int Id;
        public readonly KinematicAttributes Attributes;
        /// <summary>GK 능력치 (specs/011, 002 전 대체 입력). 지정 안 됨(<c>IsSet</c> false)이면 선발 GK는 리그 평균, 필드 선수는 대체값.</summary>
        public readonly Goalkeeper.KeeperAttributes Keeper;
        /// <summary>정신·성격 (specs/010, 002 전 대체 입력). 지정 안 됨이면 평균(<see cref="UtilityAI.DecisionAttributes.Average"/>).</summary>
        public readonly UtilityAI.DecisionAttributes Mind;
        /// <summary>플레이 스타일 (specs/010, 003 전 대체 입력). null이면 중립.</summary>
        public readonly UtilityAI.PlayStyle Style;

        public MatchPlayer(int id, in KinematicAttributes attributes)
            : this(id, attributes, default)
        {
        }

        public MatchPlayer(int id, in KinematicAttributes attributes, in Goalkeeper.KeeperAttributes keeper)
            : this(id, attributes, keeper, default, null)
        {
        }

        public MatchPlayer(int id, in KinematicAttributes attributes, in Goalkeeper.KeeperAttributes keeper, in UtilityAI.DecisionAttributes mind, UtilityAI.PlayStyle style)
        {
            if (id < 0) throw new ArgumentOutOfRangeException(nameof(id), "선수 id는 0 이상");
            Id = id;
            Attributes = attributes;
            Keeper = keeper;
            Mind = mind;
            Style = style;
        }

        /// <summary>정신·스타일만 바꾼 복사본.</summary>
        public MatchPlayer WithMind(in UtilityAI.DecisionAttributes mind, UtilityAI.PlayStyle style = null) => new MatchPlayer(Id, Attributes, Keeper, mind, style ?? Style);
    }

    /// <summary>팀 입력 (FR-10). Starters[0]이 골키퍼다.</summary>
    public sealed class TeamSetup
    {
        public int TeamId { get; set; }
        public MatchPlayer[] Players { get; set; } = Array.Empty<MatchPlayer>();
        public int[] Starters { get; set; } = Array.Empty<int>();
        public int[] Bench { get; set; } = Array.Empty<int>();
        public int CaptainId { get; set; } = -1;
        public int SetPieceTakerId { get; set; } = -1;
        /// <summary>대형 슬롯 11개 (팀 관점 정규화: x 0 = 자기 골라인 → 1 = 상대 골라인, y 0~1). 009가 생기면 지시로 바뀐다.</summary>
        public Vec2[] Formation { get; set; } = Array.Empty<Vec2>();
        /// <summary>승부차기 키커 순서 (선택, FR-15). 없으면 출전 선수 id 순.</summary>
        public int[] PenaltyOrder { get; set; }
        /// <summary>팀 전술 (specs/009). 두 팀 모두 있으면 009 단계를 쓰고, 없으면 006 대체 단계를 쓴다.</summary>
        public Tactics.TeamTactics Tactics { get; set; }

        public TeamSetup Clone() => new TeamSetup
        {
            TeamId = TeamId, Players = (MatchPlayer[])Players?.Clone(), Starters = (int[])Starters?.Clone(), Bench = (int[])Bench?.Clone(),
            CaptainId = CaptainId, SetPieceTakerId = SetPieceTakerId, Formation = (Vec2[])Formation?.Clone(), PenaltyOrder = (int[])PenaltyOrder?.Clone(),
            Tactics = Tactics?.Clone(),
        };

        public bool TryGetPlayer(int id, out MatchPlayer p)
        {
            foreach (var x in Players)
                if (x.Id == id) { p = x; return true; }
            p = default;
            return false;
        }
    }

    /// <summary>피치 상태 (FR-10). 공 물리 표면(004)으로 바뀐다.</summary>
    public enum PitchCondition : byte
    {
        Dry = 0,
        Wet = 1,
        Artificial = 2,
    }

    /// <summary>
    /// 경기 설정 입력 (specs/006 FR-10). 같은 설정 + 같은 입력 로그 → 같은 결과 (결정론).
    /// </summary>
    public sealed class MatchSetup
    {
        public ulong Seed { get; set; }
        public TeamSetup Home { get; set; }
        public TeamSetup Away { get; set; }
        public PitchSpec Pitch { get; set; } = PitchSpec.Default;
        public PitchCondition Condition { get; set; } = PitchCondition.Dry;
        public CompetitionRules Rules { get; set; } = CompetitionRules.Default();
        /// <summary>심판 성향 (012 자리, 대체 단계는 쓰지 않음).</summary>
        public int RefereeLeniency { get; set; }
        /// <summary>경기 중요도 (002 FR-61).</summary>
        public float Importance { get; set; } = 1f;
        public int MyPlayerId { get; set; } = -1;

        public TeamSetup Team(int team) => team == 0 ? Home : Away;

        /// <summary>깊은 복사 (경기가 설정을 따로 갖는다 — 호출자 변경이 진행 중 경기·재구성에 새지 않게).</summary>
        public MatchSetup Clone() => new MatchSetup
        {
            Seed = Seed, Home = Home?.Clone(), Away = Away?.Clone(), Pitch = Pitch, Condition = Condition,
            Rules = Rules == null ? null : (CompetitionRules)Rules.MemberwiseCopy(), RefereeLeniency = RefereeLeniency, Importance = Importance, MyPlayerId = MyPlayerId,
        };

        public SurfaceCondition Surface => Condition switch
        {
            PitchCondition.Wet => SurfaceCondition.Wet,
            PitchCondition.Artificial => SurfaceCondition.Artificial,
            _ => SurfaceCondition.Dry,
        };

        /// <summary>입력 검증 (경계: 선발 11명 미만·중복 선수·벤치 초과·대형 슬롯 수 → 거부).</summary>
        public void Validate()
        {
            if (Home == null || Away == null) throw new MatchSetupException("홈·원정 팀이 모두 필요하다");
            if (Rules == null) throw new MatchSetupException("대회 규칙이 없다");
            Rules.Validate();
            if (!(Pitch.LengthM > 0f && Pitch.WidthM > 0f)) throw new MatchSetupException("피치 크기 오류");
            var seen = new HashSet<int>();
            for (int t = 0; t < 2; t++)
            {
                var ts = Team(t);
                var ids = new HashSet<int>();
                foreach (var p in ts.Players)
                {
                    if (!ids.Add(p.Id)) throw new MatchSetupException($"팀 {t} 선수 목록에 id {p.Id} 중복");
                    if (!seen.Add(p.Id)) throw new MatchSetupException($"선수 id {p.Id}가 두 팀에 모두 있다");
                }
                if (ts.Starters == null || ts.Starters.Length != 11) throw new MatchSetupException($"팀 {t} 선발은 정확히 11명 (받은 수 {ts.Starters?.Length ?? 0})");
                var lineup = new HashSet<int>();
                foreach (var id in ts.Starters)
                {
                    if (!ids.Contains(id)) throw new MatchSetupException($"팀 {t} 선발 {id}가 선수 목록에 없다");
                    if (!lineup.Add(id)) throw new MatchSetupException($"팀 {t} 선발에 {id} 중복");
                }
                var bench = ts.Bench ?? Array.Empty<int>();
                if (bench.Length > Rules.BenchSize) throw new MatchSetupException($"팀 {t} 벤치 {bench.Length}명 > 허용 {Rules.BenchSize}");
                foreach (var id in bench)
                {
                    if (!ids.Contains(id)) throw new MatchSetupException($"팀 {t} 벤치 {id}가 선수 목록에 없다");
                    if (!lineup.Add(id)) throw new MatchSetupException($"팀 {t} 선발·벤치에 {id} 중복");
                }
                if (ts.Formation == null || ts.Formation.Length != 11) throw new MatchSetupException($"팀 {t} 대형 슬롯은 11개");
                foreach (var f in ts.Formation)
                    if (!(f.X >= 0f && f.X <= 1f && f.Y >= 0f && f.Y <= 1f)) throw new MatchSetupException($"팀 {t} 대형 좌표는 0~1");
                var marks = ts.Tactics?.ManMarkTargets;
                if (marks != null)
                {
                    if (marks.Length != 11) throw new MatchSetupException($"팀 {t} 맨투맨 목록은 슬롯 11개");
                    foreach (var m in marks) if (m < -1 || m > 10) throw new MatchSetupException($"팀 {t} 맨투맨 대상 {m}은 −1~10이어야 한다");
                }
                if ((ts.Tactics != null) != (Team(1 - t).Tactics != null)) throw new MatchSetupException("전술은 두 팀 모두 있거나 모두 없어야 한다");
                if (ts.PenaltyOrder != null)
                    foreach (var id in ts.PenaltyOrder)
                        if (!lineup.Contains(id)) throw new MatchSetupException($"팀 {t} 승부차기 키커 {id}가 명단에 없다");
            }
        }
    }
}
