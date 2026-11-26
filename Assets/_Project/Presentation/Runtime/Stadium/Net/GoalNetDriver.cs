using System;
using RoadToPro.Core.Math;

namespace RoadToPro.Presentation.Stadium.Net
{
    /// <summary>
    /// 그물 뷰 구동 로직 (specs/059 FR-27~29·53·54, 순수 C#). 장면에 구운 그물 데이터(<see cref="NetBake"/>, 골대별)를 시각으로 보간해
    /// 두 골대 그물 렌더 정점(로컬)을 쓰고, 이번 프레임에 메시에 써야 하는 골대를 비트로 돌려준다:
    /// 굽기 구간(과 그 뒤 마지막 프레임 유지)이면 매 프레임, 정지 모양으로 돌아가면 한 번 쓴 뒤 쓰지 않는다.
    /// 같은 장면·같은 시각(키 모먼트 정지)이면 쓰지 않는다. 장면이 바뀌면 새 장면 데이터만 쓴다(이전 장면 출렁임이 이어지지 않음).
    /// 프레임 경로 할당 없음.
    /// </summary>
    public sealed class GoalNetDriver
    {
        public readonly NetCloth Cloth;
        /// <summary>[골대 번호][렌더 정점] 로컬 위치. 번호 0 = 왼쪽 골(X &lt; 0, side −1), 1 = 오른쪽(+1).</summary>
        public readonly Vec3[][] Local = new Vec3[2][];
        readonly Vec3[] _restRender;
        readonly Vec3[] _nodes;
        readonly bool[] _moving = new bool[2];
        int _lastScene = int.MinValue;
        double _lastTime = double.NaN;

        public GoalNetDriver(NetCloth cloth)
        {
            Cloth = cloth ?? throw new ArgumentNullException(nameof(cloth));
            int n = cloth.Shape.Count;
            _restRender = new Vec3[n];
            for (int i = 0; i < n; i++) _restRender[i] = cloth.Rest[cloth.VertexToNode[i]];
            _nodes = new Vec3[cloth.NodeCount];
            for (int s = 0; s < 2; s++)
            {
                Local[s] = new Vec3[n];
                Array.Copy(_restRender, Local[s], n);
            }
        }

        /// <summary>정지 모양(늘어뜨린 그물) 렌더 정점.</summary>
        public Vec3[] RestRender => _restRender;

        /// <summary>앞 프레임에 어느 골대든 움직이고 있었는가.</summary>
        public bool AnyAwake => _moving[0] || _moving[1];

        public static int IndexOf(int side) => side < 0 ? 0 : 1;
        public static int SideOf(int index) => index == 0 ? -1 : 1;
        public static int MaskOf(int side) => 1 << IndexOf(side);

        /// <param name="bakes">[골대 번호] 이 장면의 구운 데이터 (없으면 null).</param>
        /// <returns>이번 프레임에 정점을 써야 하는 골대 비트 (<see cref="MaskOf"/>).</returns>
        public int Update(int sceneId, double time, NetBake[] bakes)
        {
            bool newScene = sceneId != _lastScene;
            if (!newScene && time == _lastTime) return 0;   // 정지(키 모먼트·일시정지): 그물도 정확히 멈춤 (FR-53)
            _lastScene = sceneId;
            _lastTime = time;
            int dirty = 0;
            for (int s = 0; s < 2; s++)
            {
                var bake = bakes != null && bakes.Length > s ? bakes[s] : null;
                bool moving = bake != null && time > bake.StartTime;
                if (moving)
                {
                    bake.Sample(time, Cloth.Rest, _nodes);
                    var map = Cloth.VertexToNode;
                    var dst = Local[s];
                    for (int i = 0; i < dst.Length; i++) dst[i] = _nodes[map[i]];
                    dirty |= 1 << s;
                }
                else if (_moving[s] || newScene)
                {
                    Array.Copy(_restRender, Local[s], _restRender.Length);   // 정지 모양으로 한 번 되돌림
                    if (_moving[s]) dirty |= 1 << s;
                }
                _moving[s] = moving;
            }
            return dirty;
        }
    }
}
