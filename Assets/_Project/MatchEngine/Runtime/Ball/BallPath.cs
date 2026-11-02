using System;
using RoadToPro.Core.Math;

namespace RoadToPro.MatchEngine.Ball
{
    /// <summary>
    /// 한 서브스텝 동안 공 중심이 지나간 꺾인 선 (골대 반발로 최대 2번 꺾여 최대 4점).
    /// 각 점은 서브스텝 시간축의 비율 F(0 = 시작, 1 = 끝)를 가진다. 골대·지면·경계 판정이 같은 시간축을 쓰도록 한다
    /// (결정론 리뷰 R-3). 값 타입, 할당 없음.
    /// </summary>
    public struct BallPath
    {
        public const int MaxPoints = GoalFrame.MaxContactsPerSubstep + 2;

        Vec3 _p0, _p1, _p2, _p3;
        float _f0, _f1, _f2, _f3;

        public int Count { get; private set; }

        public static BallPath Line(Vec3 start, Vec3 end)
        {
            var path = new BallPath();
            path.Add(start, 0f);
            path.Add(end, 1f);
            return path;
        }

        public Vec3 Point(int i)
        {
            switch (i)
            {
                case 0: return _p0;
                case 1: return _p1;
                case 2: return _p2;
                case 3: return _p3;
                default: throw new ArgumentOutOfRangeException(nameof(i));
            }
        }

        public float Fraction(int i)
        {
            switch (i)
            {
                case 0: return _f0;
                case 1: return _f1;
                case 2: return _f2;
                case 3: return _f3;
                default: throw new ArgumentOutOfRangeException(nameof(i));
            }
        }

        public Vec3 Last => Point(Count - 1);

        public void Add(Vec3 point, float fraction)
        {
            if (Count >= MaxPoints) throw new InvalidOperationException("BallPath 점 개수 초과");
            Set(Count, point, fraction);
            Count++;
        }

        /// <summary>점 i를 바꾸고 그 뒤 점을 버린다 (점 i가 새 끝점).</summary>
        public void TruncateAt(int i, Vec3 point, float fraction)
        {
            if (i < 1 || i > Count - 1) throw new ArgumentOutOfRangeException(nameof(i));
            Set(i, point, fraction);
            Count = i + 1;
        }

        void Set(int i, Vec3 p, float f)
        {
            switch (i)
            {
                case 0: _p0 = p; _f0 = f; break;
                case 1: _p1 = p; _f1 = f; break;
                case 2: _p2 = p; _f2 = f; break;
                case 3: _p3 = p; _f3 = f; break;
                default: throw new ArgumentOutOfRangeException(nameof(i));
            }
        }
    }
}
