using System.Collections;
using RoadToPro.Core.Logging;
using RoadToPro.Presentation.Highlight;
using RoadToPro.Presentation.Logging;
using RoadToPro.Presentation.Players;
using RoadToPro.Presentation.Players.Logic;
using RoadToPro.Presentation.Stadium;
using UnityEngine;
using ILogger = RoadToPro.Core.Logging.ILogger;

namespace RoadToPro.Verification.Samples.Highlight
{
    /// <summary>
    /// specs/041 검증용 무대: 043 가상 선수 25명을 만든다. 002·020이 연결되면 실제 선수 외형으로 바뀐다.
    /// </summary>
    public sealed class HighlightCastStage : HighlightStage
    {
        const int RigsPerFrame = 5;

        [SerializeField] PlayerScreen.PlayerAssets assets;
        [SerializeField] Light sun;
        [SerializeField] Material emissive;
        [SerializeField] StadiumLighting lighting;
        [SerializeField] StadiumLightingPreset day;
        [SerializeField] PitchMaterialBinder binder;

        readonly PlayerRig[] _rigs = new PlayerRig[HighlightData.Entities];
        readonly float[] _scales = new float[HighlightData.Entities];
        Transform _ball;
        Material _ballMaterial;
        Texture2D _homeAtlas, _awayAtlas;
        ClipLibrary _lib;
        AnimationMap _map;

        public override PlayerRig[] Rigs => _rigs;
        public override AnimationClip[] Clips => assets.clips;
        public override Transform Ball => _ball;
        public ClipLibrary Library => _lib;
        public AnimationMap Map => _map;
        public float[] Scales => _scales;

        public void Configure(in PlayerScreen.PlayerAssets a, Light s, Material e, StadiumLighting l, StadiumLightingPreset d, PitchMaterialBinder b)
        {
            assets = a;
            sun = s;
            emissive = e;
            lighting = l;
            day = d;
            binder = b;
        }

        public override IEnumerator Prepare(Camera broadcastCamera)
        {
            ILogger log = new UnityLogger(RoadToPro.Core.Logging.LogLevel.Warning);
            lighting.Configure(sun, emissive, new[] { broadcastCamera }, new BlobShadow4[0]);
            lighting.Apply(day);
            binder.Bind();

            _lib = ClipLibrary.Parse(assets.clipsJson.text);
            _map = AnimationMap.Parse(assets.mapJson.text, _lib, log);
            // 가상 팀 색 (실존 클럽 키트 아님, 01-legal)
            var kits = KitColorResolver.Resolve(PlayerScreen.Team(0xC8102E, 0xFFFFFF, 0x222222, 0x00A651, 0xFFFFFF, 0xFFFFFF),
                PlayerScreen.Team(0x1C3F94, 0xFFD100, 0xFFFFFF, 0xFF6F00, 0xFFFFFF, 0xFFFFFF));
            KitCompositor.PlaybackActive = false;
            var comp = new KitCompositor(GlyphAtlasData.Parse(assets.glyphJson.text), assets.names, assets.numbers);
            _homeAtlas = comp.ComposeTeam(PlayerScreen.Squad(PlayerScreen.HomeNames), log);
            _awayAtlas = comp.ComposeTeam(PlayerScreen.Squad(PlayerScreen.AwayNames), log);
            yield return null;

            var inputs = new BodyInput[HighlightData.Entities];
            var looks = new PlayerAppearance[HighlightData.Entities];
            for (int e = 0; e < inputs.Length; e++) inputs[e] = new BodyInput(4300 + e * 13, null, null, PlayerScreen.RoleOf(e));
            AppearanceGenerator.GenerateGroup(inputs, looks);
            var faceData = FaceData.Parse(assets.body.faceJson.text);
            var ids = new int[inputs.Length];
            for (int e = 0; e < ids.Length; e++) ids[e] = inputs[e].Id;
            var faces = new FaceShape[ids.Length];
            FaceGenerator.GenerateGroup(faceData, new FaceLandmarks(faceData), ids, faces);
            yield return null;

            for (int e = 0; e < HighlightData.Entities; e++)
            {
                var rig = PlayerRig.Create(assets.prefab, transform, assets.bodyShader, assets.kitShader, assets.body, assets.fabric);
                rig.name = $"Entity{e:00}";
                rig.ApplyAppearance(looks[e], faces[e], faceData);
                KitColors k;
                Texture2D atlas = null;
                int cell = -1;
                if (e == 0) { k = kits.HomeKeeper; atlas = _homeAtlas; cell = 0; }
                else if (e <= 10) { k = kits.Home; atlas = _homeAtlas; cell = e; }
                else if (e == 11) { k = kits.AwayKeeper; atlas = _awayAtlas; cell = 0; }
                else if (e <= 21) { k = kits.Away; atlas = _awayAtlas; cell = e - 11; }
                else k = kits.Referee;
                rig.ApplyKit(k, assets.masks[0], atlas, cell);
                _rigs[e] = rig;
                _scales[e] = rig.transform.localScale.y;
                if ((e + 1) % RigsPerFrame == 0) yield return null;
            }

            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = "Ball";
            go.transform.SetParent(transform, false);
            Destroy(go.GetComponent<Collider>());
            go.transform.localScale = Vector3.one * 0.22f;
            var r = go.GetComponent<Renderer>();
            _ballMaterial = new Material(assets.kitShader) { name = "BallStandIn" };
            _ballMaterial.SetColor("_Primary", Color.white);
            _ballMaterial.SetColor("_BootColor", Color.white);
            _ballMaterial.SetVector("_TextCell", Vector4.zero);
            r.sharedMaterial = _ballMaterial;
            _ball = go.transform;
            KitCompositor.PlaybackActive = true;
        }

        void OnDestroy()
        {
            KitCompositor.PlaybackActive = false;
            if (_homeAtlas != null) Destroy(_homeAtlas);
            if (_awayAtlas != null) Destroy(_awayAtlas);
            if (_ballMaterial != null) Destroy(_ballMaterial);
        }
    }
}
