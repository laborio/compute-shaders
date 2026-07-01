using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Serialization;

[DisallowMultipleComponent]
public class CrowdController : MonoBehaviour
{
    private const int MaxInstancesPerBatch = 1023;
    private const int WebGLMaxInstancesPerBatch = 256;
    private const float DefaultSampleRate = 30f;

    private static readonly int BaseMapId = Shader.PropertyToID("_BaseMap");
    private static readonly int BillboardMapId = Shader.PropertyToID("_BillboardMap");
    private static readonly int OutfitDataMapId = Shader.PropertyToID("_OutfitDataMap");
    private static readonly int PoseTextureId = Shader.PropertyToID("_PoseTexture");
    private static readonly int BoneCountId = Shader.PropertyToID("_BoneCount");
    private static readonly int ClipMeta0Id = Shader.PropertyToID("_ClipMeta0");
    private static readonly int ClipMeta1Id = Shader.PropertyToID("_ClipMeta1");
    private static readonly int ClipMeta2Id = Shader.PropertyToID("_ClipMeta2");
    private static readonly int ColorRId = Shader.PropertyToID("_ColorR");
    private static readonly int ColorGId = Shader.PropertyToID("_ColorG");
    private static readonly int ColorBId = Shader.PropertyToID("_ColorB");
    private static readonly int ColorAId = Shader.PropertyToID("_ColorA");
    private static readonly int AnimDataId = Shader.PropertyToID("_AnimData");
    private static readonly int TransitionFadeId = Shader.PropertyToID("_TransitionFade");
    private static readonly int FallbackTransitionFadeId = Shader.PropertyToID("_FallbackTransitionFade");
    private static readonly int FallbackColorRId = Shader.PropertyToID("_FallbackColorR");
    private static readonly int FallbackColorGId = Shader.PropertyToID("_FallbackColorG");
    private static readonly int FallbackColorBId = Shader.PropertyToID("_FallbackColorB");
    private static readonly int FallbackColorAId = Shader.PropertyToID("_FallbackColorA");
    private static readonly int FallbackAnimDataId = Shader.PropertyToID("_FallbackAnimData");

    [Serializable]
    private struct OutfitPreset
    {
        public Color colorR;
        public Color colorG;
        public Color colorB;
        public Color colorA;
        public int billboardVariant;
    }

    private enum PlaybackState
    {
        StandingHold = 0,
        SittingDown = 1,
        SeatedHold = 2,
        StandingUp = 3,
    }

    private enum RuntimeClip
    {
        Idle = 0,
        Sit = 1,
        Stand = 2,
    }

    private enum DebugRenderMode
    {
        Normal = 0,
        BillboardsOnly = 1,
        UnskinnedLit = 2,
        UnskinnedSolid = 3,
        SkinnedSolid = 4,
        MeshesOnly = 5,
    }

    private enum LayoutMode
    {
        Grid = 0,
        SeatLayoutAsset = 1,
    }

    private struct ClipBakeInfo
    {
        public RuntimeClip clip;
        public int startRow;
        public int frameCount;
        public float duration;
    }

    private struct InstanceState
    {
        public Matrix4x4 matrix;
        public PlaybackState playbackState;
        public float clipTime;
        public float holdTimer;
        public int outfitIndex;
    }

    private sealed class Chunk
    {
        public readonly List<int> instanceIndices = new();
        public Bounds bounds;
        public bool hasBounds;
    }

    private sealed class BillboardBatchBucket
    {
        public Material material;
        public bool useDedicatedBillboardMaterial;
        public bool isActiveInFrame;
        public readonly List<Matrix4x4> matrices = new();
        public readonly List<float> transitionFades = new();
        public readonly List<Vector4> colorRs = new();
        public readonly List<Vector4> colorGs = new();
        public readonly List<Vector4> colorBs = new();
        public readonly List<Vector4> colorAs = new();
        public readonly List<Vector4> animDatas = new();

        public void Clear()
        {
            isActiveInFrame = false;
            matrices.Clear();
            transitionFades.Clear();
            colorRs.Clear();
            colorGs.Clear();
            colorBs.Clear();
            colorAs.Clear();
            animDatas.Clear();
        }
    }

    [Header("Source")]
    [SerializeField] private GameObject crowdSource;
    [SerializeField] private AnimationClip idleClip;
    [SerializeField] private AnimationClip sitClip;
    [SerializeField] private AnimationClip standClip;
    [SerializeField] private Texture2D atlasMap;
    [SerializeField] private Texture2D billboardColor01;
    [SerializeField] private Texture2D billboardColor02;
    [SerializeField] private Texture2D billboardSideColor01;
    [SerializeField] private Texture2D billboardSideColor02;
    [SerializeField] private Texture2D billboardSeatedColor01;
    [SerializeField] private Texture2D billboardSeatedColor02;
    [SerializeField] private Texture2D billboardSeatedSideColor01;
    [SerializeField] private Texture2D billboardSeatedSideColor02;
    [SerializeField] private Texture2D outfitDataMap;
    [SerializeField] private Material materialTemplate;
    [SerializeField] private Material billboardMaterialTemplate;
    [SerializeField] private bool hideSourceCharacter = true;
    [SerializeField] private Vector3 modelRotationEuler = new(-90f, 0f, 0f);

    [Header("Crowd Layout")]
    [SerializeField] private LayoutMode layoutMode = LayoutMode.Grid;
    [SerializeField] private TextAsset seatLayoutAsset;
    [SerializeField] private bool useSeatLayoutForward = true;
    [SerializeField] private float seatLayoutForwardYawOffset;
    [FormerlySerializedAs("seatLayoutLocalOffset")]
    [SerializeField] private Vector3 seatLayoutWorldOffset;
    [SerializeField] private float seatLayoutLateralJitter = 0.05f;
    [SerializeField] private int instanceCount = 400;
    [SerializeField] private Vector2 areaSize = new(24f, 16f);
    [SerializeField] private Vector2 chunkSize = new(6f, 6f);
    [SerializeField] private float characterScale = 1f;
    [SerializeField] private float characterHeight = 1.9f;
    [SerializeField] private float facingYaw = 180f;
    [SerializeField] private int columnsPerRow;
    [SerializeField] private float rowSpacingX = 1.1f;
    [SerializeField] private float rowSpacingY = 0.5f;
    [SerializeField] private float rowSpacingZ = 1f;
    [SerializeField] private float rowJitterX = 0.18f;
    [SerializeField] private float rowJitterY = 0.06f;
    [SerializeField] private float lod1Distance = 18f;
    [SerializeField] private float lod2Distance = 32f;
    [SerializeField] private float lod3Distance = 40f;
    [SerializeField] private float billboardDistance = 48f;
    [SerializeField] private bool useWebGLLodDistanceOverrides = true;
    [SerializeField] private float webGLLod1Distance = 4f;
    [SerializeField] private float webGLLod2Distance = 7f;
    [SerializeField] private float webGLLod3Distance = 8.5f;
    [SerializeField] private float webGLBillboardDistance = 10f;
    [SerializeField] private float billboardTransitionBand = 8f;
    [SerializeField] private float billboardScale = 1f;
    [SerializeField] private float billboardDepthOffset = 0f;
    [SerializeField] private float billboardStandingHeightOffset = 0f;
    [SerializeField] private float billboardSeatedHeightOffset = -0.57f;
    [SerializeField, Range(0f, 1f)] private float billboardSideViewDotThreshold = 0.72f;
    [SerializeField] private bool enableLod0 = true;
    [SerializeField] private bool enableLod1 = true;
    [SerializeField] private bool enableLod2 = true;
    [SerializeField] private bool enableLod3 = true;
    [SerializeField] private bool enableBillboards = true;
    [SerializeField] private int randomSeed = 7;

    [Header("Animation")]
    [SerializeField] private Vector2 standingHoldRange = new(2f, 6f);
    [SerializeField] private Vector2 seatedHoldRange = new(2f, 5f);

    [Header("Outfits")]
    [SerializeField] private OutfitPreset[] outfits =
    {
        new OutfitPreset
        {
            colorR = new Color(0.76f, 0.19f, 0.17f, 1f),
            colorG = new Color(0.18f, 0.21f, 0.24f, 1f),
            colorB = new Color(0.09f, 0.10f, 0.12f, 1f),
            colorA = new Color(0.43f, 0.31f, 0.18f, 1f),
            billboardVariant = 0,
        },
        new OutfitPreset
        {
            colorR = new Color(0.18f, 0.39f, 0.74f, 1f),
            colorG = new Color(0.61f, 0.66f, 0.18f, 1f),
            colorB = new Color(0.10f, 0.11f, 0.14f, 1f),
            colorA = new Color(0.58f, 0.44f, 0.34f, 1f),
            billboardVariant = 1,
        },
    };

    [Header("Debug")]
    [SerializeField] private bool drawChunkGizmos = true;
    [SerializeField] private bool drawSeatLayoutDebugGizmos = true;
    [SerializeField] private int seatLayoutDebugStride = 24;
    [SerializeField] private int seatLayoutDebugMaxMarkers = 2500;
    [SerializeField] private float seatLayoutDebugMarkerSize = 0.18f;
    [SerializeField] private bool drawSeatLayoutForwardGizmos = true;
    [SerializeField] private float seatLayoutDebugForwardLength = 0.4f;
    [SerializeField] private Color seatLayoutDebugMarkerColor = new(0.15f, 0.95f, 0.35f, 0.9f);
    [SerializeField] private Color seatLayoutDebugForwardColor = new(1f, 0.45f, 0.1f, 0.9f);
    [SerializeField] private bool drawSeatLayoutAlignmentGizmos = true;
    [SerializeField] private GameObject seatLayoutReferenceObject;
    [SerializeField] private float seatLayoutCenterMarkerSize = 0.6f;
    [SerializeField] private Color seatLayoutReferenceBoundsColor = new(0.15f, 0.75f, 1f, 0.9f);
    [SerializeField] private Color seatLayoutSourceBoundsColor = new(1f, 0.2f, 0.85f, 0.9f);
    [SerializeField] private Color seatLayoutCenterDeltaColor = new(1f, 0.95f, 0.1f, 0.95f);
    [SerializeField] private bool autoLogSeatLayoutDiagnostics;
    [SerializeField] private DebugRenderMode debugRenderMode = DebugRenderMode.Normal;
    [SerializeField] private bool forceWebGLBillboardsOnly;
    [SerializeField] private bool useWebGLBillboardFallback = true;
    [SerializeField] private bool useWebGLNonInstancedMeshFallback = true;
    [SerializeField] private bool useWebGLSkipLod0 = true;

    private readonly Plane[] frustumPlanes = new Plane[6];
    private readonly List<Chunk> chunks = new();
    private readonly Dictionary<Material, BillboardBatchBucket> billboardBatchBuckets = new();
    private readonly List<BillboardBatchBucket> activeBillboardBatchBuckets = new();
    private MaterialPropertyBlock materialPropertyBlock;
    private Material runtimeMaterial;
    private Material[] billboardStandingFrontMaterials;
    private Material[] billboardStandingSideMaterials;
    private Material[] billboardSeatedFrontMaterials;
    private Material[] billboardSeatedSideMaterials;
    private Texture2D poseTexture;
    private Mesh crowdMesh;
    private Mesh billboardMesh;
    private Mesh[] lodMeshes;
    private InstanceState[] instances;
    private ClipBakeInfo[] bakedClips;
    private System.Random randomGenerator;
    private GameObject resolvedSourceRoot;
    private SkinnedMeshRenderer resolvedSourceRenderer;
    private float computedCrowdDepth;
    private Bounds crowdBounds;
    private bool hasCrowdBounds;
    private int frameDrawCallCount;
    private int frameSetPassCount;
    private int frameVisibleInstanceCount;
    private int frameVisibleMeshInstanceCount;
    private int frameVisibleBillboardInstanceCount;
    private int frameVisibleChunkCount;
    private long frameTriangleCount;
    private int lastDrawCallCount;
    private int lastSetPassCount;
    private int lastVisibleInstanceCount;
    private int lastVisibleMeshInstanceCount;
    private int lastVisibleBillboardInstanceCount;
    private int lastVisibleChunkCount;
    private long lastTriangleCount;
    private int maxInstancesPerBatch;
    private Matrix4x4[] matrixBatch;
    private Vector4[] colorRBatch;
    private Vector4[] colorGBatch;
    private Vector4[] colorBBatch;
    private Vector4[] colorABatch;
    private Vector4[] animDataBatch;
    private float[] transitionFadeBatch;

    public int LastDrawCallCount => lastDrawCallCount;
    public int LastSetPassCount => lastSetPassCount;
    public int LastVisibleInstanceCount => lastVisibleInstanceCount;
    public int LastVisibleMeshInstanceCount => lastVisibleMeshInstanceCount;
    public int LastVisibleBillboardInstanceCount => lastVisibleBillboardInstanceCount;
    public int LastVisibleChunkCount => lastVisibleChunkCount;
    public long LastTriangleCount => lastTriangleCount;
    public int LastShadowCasterCount => 0;
    public string ActiveDebugRenderModeName => ResolveActiveDebugRenderMode().ToString();
    public bool IsWebGLBillboardFallbackActive =>
        debugRenderMode == DebugRenderMode.Normal &&
        ResolveActiveDebugRenderMode() == DebugRenderMode.BillboardsOnly;
    public bool IsWebGLBillboardOnlyShowcaseEnabled =>
        Application.platform == RuntimePlatform.WebGLPlayer && forceWebGLBillboardsOnly;
    public bool IsWebGLNonInstancedMeshFallbackActive =>
        Application.platform == RuntimePlatform.WebGLPlayer && useWebGLNonInstancedMeshFallback;
    public bool IsWebGLSkipLod0Active =>
        Application.platform == RuntimePlatform.WebGLPlayer && useWebGLSkipLod0 && lodMeshes != null && lodMeshes.Length >= 2;
    public bool HasBillboardMesh => billboardMesh != null;
    public int BillboardMaterialCount => billboardStandingFrontMaterials?.Length ?? 0;
    public bool UsesDedicatedBillboardShader => UsesDedicatedBillboardMaterial();
    public string BillboardShaderName =>
        billboardStandingFrontMaterials != null &&
        billboardStandingFrontMaterials.Length > 0 &&
        billboardStandingFrontMaterials[0] != null &&
        billboardStandingFrontMaterials[0].shader != null
            ? billboardStandingFrontMaterials[0].shader.name
            : "<none>";
    public float ActiveLod1Distance => ResolveLod1Distance();
    public float ActiveLod2Distance => ResolveLod2Distance();
    public float ActiveLod3Distance => ResolveLod3Distance();
    public float ActiveBillboardDistance => ResolveBillboardDistance();
    public bool HasPoseTexture => poseTexture != null;
    public int RuntimeBoneCount => crowdMesh != null ? crowdMesh.bindposes.Length : 0;
    public bool RuntimeMaterialInstancingEnabled => runtimeMaterial != null && runtimeMaterial.enableInstancing;

    private void OnEnable()
    {
        InitializeCrowd();
    }

    private void LateUpdate()
    {
        if (runtimeMaterial == null || crowdMesh == null || instances == null || bakedClips == null)
        {
            return;
        }

        UpdateInstanceAnimation(Time.deltaTime);
        RenderCrowd();
    }

    private void OnDisable()
    {
        ReleaseRuntimeResources();
    }

    private void OnValidate()
    {
        instanceCount = Mathf.Max(1, instanceCount);
        seatLayoutForwardYawOffset = Mathf.Repeat(seatLayoutForwardYawOffset, 360f);
        areaSize.x = Mathf.Max(1f, areaSize.x);
        areaSize.y = Mathf.Max(1f, areaSize.y);
        chunkSize.x = Mathf.Max(1f, chunkSize.x);
        chunkSize.y = Mathf.Max(1f, chunkSize.y);
        characterScale = Mathf.Max(0.01f, characterScale);
        characterHeight = Mathf.Max(0.25f, characterHeight);
        facingYaw = Mathf.Repeat(facingYaw, 360f);
        columnsPerRow = Mathf.Max(0, columnsPerRow);
        rowSpacingX = Mathf.Max(0.1f, rowSpacingX);
        rowSpacingY = Mathf.Max(0f, rowSpacingY);
        rowSpacingZ = Mathf.Max(0.1f, rowSpacingZ);
        rowJitterX = Mathf.Max(0f, rowJitterX);
        rowJitterY = Mathf.Max(0f, rowJitterY);
        lod1Distance = Mathf.Max(0.1f, lod1Distance);
        lod2Distance = Mathf.Max(lod1Distance, lod2Distance);
        lod3Distance = Mathf.Max(lod2Distance, lod3Distance);
        billboardDistance = Mathf.Max(lod2Distance, billboardDistance);
        webGLLod1Distance = Mathf.Max(0.1f, webGLLod1Distance);
        webGLLod2Distance = Mathf.Max(webGLLod1Distance, webGLLod2Distance);
        webGLLod3Distance = Mathf.Max(webGLLod2Distance, webGLLod3Distance);
        webGLBillboardDistance = Mathf.Max(webGLLod2Distance, webGLBillboardDistance);
        billboardTransitionBand = Mathf.Max(0f, billboardTransitionBand);
        billboardScale = Mathf.Max(0.01f, billboardScale);
        seatLayoutDebugStride = Mathf.Max(1, seatLayoutDebugStride);
        seatLayoutDebugMaxMarkers = Mathf.Max(1, seatLayoutDebugMaxMarkers);
        seatLayoutDebugMarkerSize = Mathf.Max(0.01f, seatLayoutDebugMarkerSize);
        seatLayoutDebugForwardLength = Mathf.Max(0.01f, seatLayoutDebugForwardLength);
        seatLayoutCenterMarkerSize = Mathf.Max(0.01f, seatLayoutCenterMarkerSize);
        standingHoldRange.x = Mathf.Max(0f, standingHoldRange.x);
        standingHoldRange.y = Mathf.Max(standingHoldRange.x, standingHoldRange.y);
        seatedHoldRange.x = Mathf.Max(0f, seatedHoldRange.x);
        seatedHoldRange.y = Mathf.Max(seatedHoldRange.x, seatedHoldRange.y);
    }

    private void InitializeCrowd()
    {
        ReleaseRuntimeResources();

        if (!ResolveSourceCharacter())
        {
            Debug.LogError("CrowdController could not find a crowd source with a SkinnedMeshRenderer.");
            return;
        }

        if (!ResolveAnimationClips())
        {
            Debug.LogError("CrowdController could not resolve idle, sit, and stand clips.");
            return;
        }

        if (atlasMap == null || outfitDataMap == null)
        {
            Debug.LogError("CrowdController requires both atlasMap and outfitDataMap to render the crowd.");
            return;
        }

        if (outfits == null || outfits.Length == 0)
        {
            Debug.LogError("CrowdController requires at least one outfit preset.");
            return;
        }

        lodMeshes = ResolveLodMeshes();
        crowdMesh = lodMeshes != null && lodMeshes.Length > 0 ? lodMeshes[0] : null;
        if (crowdMesh == null)
        {
            Debug.LogError("CrowdController source renderer has no shared mesh.");
            return;
        }

        if (billboardColor01 != null || billboardColor02 != null)
        {
            billboardMesh = CreateBillboardMesh();
        }

        BuildAnimationTexture();

        runtimeMaterial = materialTemplate != null
            ? new Material(materialTemplate)
            : new Material(Shader.Find("ComputeCrowd/CrowdRender"));

        if (runtimeMaterial.shader == null)
        {
            Debug.LogError("CrowdController could not create the crowd material.");
            return;
        }

        runtimeMaterial.enableInstancing = true;
        runtimeMaterial.SetTexture(BaseMapId, atlasMap);
        runtimeMaterial.SetTexture(OutfitDataMapId, outfitDataMap);
        runtimeMaterial.SetTexture(PoseTextureId, poseTexture);
        runtimeMaterial.SetInt(BoneCountId, crowdMesh.bindposes.Length);
        runtimeMaterial.SetVector(ClipMeta0Id, PackClipMeta(RuntimeClip.Idle));
        runtimeMaterial.SetVector(ClipMeta1Id, PackClipMeta(RuntimeClip.Sit));
        runtimeMaterial.SetVector(ClipMeta2Id, PackClipMeta(RuntimeClip.Stand));

        billboardStandingFrontMaterials = BuildBillboardMaterials(billboardColor01, billboardColor02);
        billboardStandingSideMaterials = BuildBillboardMaterials(billboardSideColor01, billboardSideColor02);
        billboardSeatedFrontMaterials = BuildBillboardMaterials(billboardSeatedColor01, billboardSeatedColor02);
        billboardSeatedSideMaterials = BuildBillboardMaterials(billboardSeatedSideColor01, billboardSeatedSideColor02);

        AllocateBatchBuffers();
        materialPropertyBlock = new MaterialPropertyBlock();
        BuildInstances();

        if (Application.platform == RuntimePlatform.WebGLPlayer)
        {
            Debug.Log(
                $"CrowdController WebGL mode: {ResolveActiveDebugRenderMode()}, " +
                $"instancing={SystemInfo.supportsInstancing}, " +
                $"billboardsReady={billboardMesh != null && billboardStandingFrontMaterials != null && billboardStandingFrontMaterials.Length > 0}, " +
                $"dedicatedBillboardShader={UsesDedicatedBillboardMaterial()}, " +
                $"billboardShader={BillboardShaderName}");
        }

        if (hideSourceCharacter && resolvedSourceRoot != null && resolvedSourceRoot.scene.IsValid())
        {
            resolvedSourceRoot.SetActive(false);
        }

        if (autoLogSeatLayoutDiagnostics && layoutMode == LayoutMode.SeatLayoutAsset)
        {
            LogSeatLayoutDiagnostics();
        }
    }

    private bool ResolveSourceCharacter()
    {
        resolvedSourceRoot = crowdSource;
        if (resolvedSourceRoot == null)
        {
            SkinnedMeshRenderer fallbackRenderer = FindFirstObjectByType<SkinnedMeshRenderer>();
            if (fallbackRenderer == null)
            {
                return false;
            }

            resolvedSourceRenderer = fallbackRenderer;
            Animator sourceAnimator = fallbackRenderer.GetComponentInParent<Animator>();
            resolvedSourceRoot = sourceAnimator != null ? sourceAnimator.gameObject : fallbackRenderer.transform.root.gameObject;
        }
        else
        {
            resolvedSourceRenderer = resolvedSourceRoot.GetComponentInChildren<SkinnedMeshRenderer>(true);
        }

        if (resolvedSourceRenderer == null && resolvedSourceRoot != null)
        {
            resolvedSourceRenderer = resolvedSourceRoot.GetComponentInChildren<SkinnedMeshRenderer>(true);
        }

        return resolvedSourceRoot != null && resolvedSourceRenderer != null;
    }

    private Mesh[] ResolveLodMeshes()
    {
        List<Mesh> meshes = new();
        LODGroup lodGroup = resolvedSourceRoot != null ? resolvedSourceRoot.GetComponentInChildren<LODGroup>(true) : null;
        if (lodGroup != null)
        {
            LOD[] lodLevels = lodGroup.GetLODs();
            for (int lodIndex = 0; lodIndex < lodLevels.Length; lodIndex++)
            {
                Renderer[] renderers = lodLevels[lodIndex].renderers;
                for (int rendererIndex = 0; rendererIndex < renderers.Length; rendererIndex++)
                {
                    if (renderers[rendererIndex] is SkinnedMeshRenderer skinnedRenderer && skinnedRenderer.sharedMesh != null)
                    {
                        Mesh lodMesh = skinnedRenderer.sharedMesh;
                        if (meshes.Count == 0 || IsCompatibleLodMesh(meshes[0], lodMesh))
                        {
                            meshes.Add(lodMesh);
                        }
                        else
                        {
                            Debug.LogWarning($"CrowdController ignored LOD mesh '{lodMesh.name}' because it does not match the LOD0 rig layout.");
                        }
                        break;
                    }
                }
            }
        }

        if (meshes.Count == 0 && resolvedSourceRenderer != null && resolvedSourceRenderer.sharedMesh != null)
        {
            meshes.Add(resolvedSourceRenderer.sharedMesh);
        }

        return meshes.ToArray();
    }

    private static bool IsCompatibleLodMesh(Mesh referenceMesh, Mesh candidateMesh)
    {
        if (referenceMesh == null || candidateMesh == null)
        {
            return false;
        }

        return referenceMesh.bindposes.Length == candidateMesh.bindposes.Length;
    }

    private bool ResolveAnimationClips()
    {
        if (idleClip != null && sitClip != null && standClip != null)
        {
            return true;
        }

        Animator animator = resolvedSourceRoot != null ? resolvedSourceRoot.GetComponentInChildren<Animator>(true) : null;
        RuntimeAnimatorController controller = animator != null ? animator.runtimeAnimatorController : null;
        if (controller == null)
        {
            return idleClip != null && sitClip != null && standClip != null;
        }

        foreach (AnimationClip clip in controller.animationClips)
        {
            string clipName = clip.name.ToLowerInvariant();
            if (idleClip == null && (clipName.Contains("idle") || clipName.Contains("ilde")))
            {
                idleClip = clip;
            }
            else if (sitClip == null && clipName.Contains("sit"))
            {
                sitClip = clip;
            }
            else if (standClip == null && clipName.Contains("stand"))
            {
                standClip = clip;
            }
        }

        return idleClip != null && sitClip != null && standClip != null;
    }

    private void BuildAnimationTexture()
    {
        bakedClips = new ClipBakeInfo[3];
        AnimationClip[] clips = { idleClip, sitClip, standClip };

        List<Matrix4x4[]> sampledFrames = new();
        int totalRows = 0;

        for (int clipIndex = 0; clipIndex < clips.Length; clipIndex++)
        {
            AnimationClip clip = clips[clipIndex];
            int frameCount = CalculateSampleCount(clip);
            bakedClips[clipIndex] = new ClipBakeInfo
            {
                clip = (RuntimeClip)clipIndex,
                startRow = totalRows,
                frameCount = frameCount,
                duration = Mathf.Max(clip.length, 0f),
            };

            for (int frameIndex = 0; frameIndex < frameCount; frameIndex++)
            {
                float sampleTime = frameCount == 1 ? 0f : (clip.length * frameIndex) / (frameCount - 1);
                sampledFrames.Add(CapturePaletteAtTime(clip, sampleTime));
            }

            totalRows += frameCount;
        }

        int width = crowdMesh.bindposes.Length * 3;
        poseTexture = new Texture2D(width, totalRows, TextureFormat.RGBAHalf, false, true)
        {
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Clamp,
            name = "CrowdPoseTexture",
        };

        Color[] pixels = new Color[width * totalRows];
        for (int row = 0; row < sampledFrames.Count; row++)
        {
            WritePaletteRow(pixels, width, row, sampledFrames[row]);
        }

        poseTexture.SetPixels(pixels);
        poseTexture.Apply(false, false);
    }

    private int CalculateSampleCount(AnimationClip clip)
    {
        if (clip == null || clip.length <= 0f)
        {
            return 1;
        }

        float sampleRate = clip.frameRate > 0f ? Mathf.Min(clip.frameRate, DefaultSampleRate) : DefaultSampleRate;
        return Mathf.Max(2, Mathf.CeilToInt(clip.length * sampleRate) + 1);
    }

    private Matrix4x4[] CapturePaletteAtTime(AnimationClip clip, float sampleTime)
    {
        GameObject clone = Instantiate(resolvedSourceRoot);
        clone.hideFlags = HideFlags.HideAndDontSave;
        clone.SetActive(true);

        Animator cloneAnimator = clone.GetComponentInChildren<Animator>(true);
        if (cloneAnimator != null)
        {
            cloneAnimator.enabled = false;
        }

        clip.SampleAnimation(clone, sampleTime);

        SkinnedMeshRenderer cloneRenderer = clone.GetComponentInChildren<SkinnedMeshRenderer>(true);
        Transform[] bones = cloneRenderer.bones;
        Matrix4x4[] bindposes = cloneRenderer.sharedMesh.bindposes;
        Matrix4x4 rendererInverse = cloneRenderer.transform.worldToLocalMatrix;

        Matrix4x4[] palette = new Matrix4x4[bindposes.Length];
        for (int i = 0; i < palette.Length; i++)
        {
            palette[i] = rendererInverse * bones[i].localToWorldMatrix * bindposes[i];
        }

        if (Application.isPlaying)
        {
            Destroy(clone);
        }
        else
        {
            DestroyImmediate(clone);
        }

        return palette;
    }

    private static void WritePaletteRow(Color[] pixels, int rowWidth, int rowIndex, Matrix4x4[] palette)
    {
        int rowOffset = rowIndex * rowWidth;
        for (int boneIndex = 0; boneIndex < palette.Length; boneIndex++)
        {
            Matrix4x4 matrix = palette[boneIndex];
            int pixelIndex = rowOffset + boneIndex * 3;
            pixels[pixelIndex + 0] = new Color(matrix.m00, matrix.m01, matrix.m02, matrix.m03);
            pixels[pixelIndex + 1] = new Color(matrix.m10, matrix.m11, matrix.m12, matrix.m13);
            pixels[pixelIndex + 2] = new Color(matrix.m20, matrix.m21, matrix.m22, matrix.m23);
        }
    }

    private Vector4 PackClipMeta(RuntimeClip clip)
    {
        ClipBakeInfo info = bakedClips[(int)clip];
        return new Vector4(info.startRow, info.frameCount, info.duration, 0f);
    }

    private void BuildInstances()
    {
        randomGenerator = new System.Random(randomSeed);
        chunks.Clear();
        hasCrowdBounds = false;

        if (layoutMode == LayoutMode.SeatLayoutAsset && TryBuildSeatLayoutInstances())
        {
            return;
        }

        BuildGridInstances();
    }

    private bool TryBuildSeatLayoutInstances()
    {
        if (!CrowdSeatLayoutUtility.TryParse(seatLayoutAsset, out CrowdSeatLayoutData layout, out string error))
        {
            Debug.LogWarning($"CrowdController could not load seat layout asset. Falling back to grid layout. {error}");
            return false;
        }

        int seatCount = layout.SeatCount;
        instances = new InstanceState[seatCount];

        Vector3[] worldPositions = new Vector3[seatCount];
        Quaternion[] worldRotations = new Quaternion[seatCount];
        Vector3 min = new(float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity);
        Vector3 max = new(float.NegativeInfinity, float.NegativeInfinity, float.NegativeInfinity);

        for (int i = 0; i < seatCount; i++)
        {
            CrowdSeatLayoutSeat seat = layout.seats[i];
            Vector3 worldPosition = ResolveSeatLayoutWorldPosition(layout, seat) + ResolveSeatLayoutLateralJitter(seat);
            Quaternion worldRotation = ResolveSeatLayoutRotation(seat);

            worldPositions[i] = worldPosition;
            worldRotations[i] = worldRotation;
            min = Vector3.Min(min, worldPosition);
            max = Vector3.Max(max, worldPosition);
        }

        BuildChunksForInstances(worldPositions, ResolveSeatLayoutInstanceBoundsSize(), out float leftEdge, out float frontEdge, out int chunkCountX, out int chunkCountZ);

        for (int i = 0; i < seatCount; i++)
        {
            instances[i] = CreateInstanceState(Matrix4x4.TRS(worldPositions[i], worldRotations[i], Vector3.one * characterScale));
            AddInstanceToChunk(i, worldPositions[i], leftEdge, frontEdge, chunkCountX, chunkCountZ, ResolveSeatLayoutInstanceBoundsSize());
        }

        crowdBounds = new Bounds((min + max) * 0.5f, Vector3.Max(max - min, new Vector3(0.1f, characterHeight, 0.1f)));
        hasCrowdBounds = true;
        computedCrowdDepth = crowdBounds.size.z;
        return true;
    }

    private void BuildGridInstances()
    {
        instances = new InstanceState[instanceCount];

        int instancesPerRow = ResolveInstancesPerRow();
        int rowCount = ResolveRowCount(instancesPerRow);
        float layoutWidth = ResolveLayoutWidth(instancesPerRow);
        computedCrowdDepth = ResolveCrowdDepth(rowCount);

        Vector3[] positions = new Vector3[instanceCount];
        Quaternion facingRotation = Quaternion.Euler(0f, facingYaw, 0f) * Quaternion.Euler(modelRotationEuler);
        for (int i = 0; i < instanceCount; i++)
        {
            int row = i / instancesPerRow;
            int column = i % instancesPerRow;
            int rowPopulation = Mathf.Min(instancesPerRow, instanceCount - row * instancesPerRow);
            float rowWidth = Mathf.Max(0f, (rowPopulation - 1) * rowSpacingX);
            float xOffset = -rowWidth * 0.5f + column * rowSpacingX;
            float jitterX = RandomRange(-rowJitterX, rowJitterX);
            float jitterY = RandomRange(-rowJitterY, rowJitterY);

            positions[i] = transform.position + new Vector3(
                xOffset + jitterX,
                row * rowSpacingY + jitterY,
                row * rowSpacingZ);

            instances[i] = CreateInstanceState(Matrix4x4.TRS(positions[i], facingRotation, Vector3.one * characterScale));
        }

        BuildChunksForInstances(positions, new Vector3(rowSpacingX, characterHeight, 1f), out float leftEdge, out float frontEdge, out int chunkCountX, out int chunkCountZ);

        for (int i = 0; i < positions.Length; i++)
        {
            AddInstanceToChunk(i, positions[i], leftEdge, frontEdge, chunkCountX, chunkCountZ, new Vector3(rowSpacingX, characterHeight, 1f));
        }

        crowdBounds = new Bounds(
            transform.position + new Vector3(0f, ((rowCount - 1) * rowSpacingY + characterHeight) * 0.5f, computedCrowdDepth * 0.5f),
            new Vector3(layoutWidth, (rowCount - 1) * rowSpacingY + characterHeight, computedCrowdDepth));
        hasCrowdBounds = true;
    }

    private InstanceState CreateInstanceState(Matrix4x4 matrix)
    {
        PlaybackState initialState = RandomValue() > 0.5f ? PlaybackState.StandingHold : PlaybackState.SeatedHold;
        float holdDuration = initialState == PlaybackState.StandingHold
            ? RandomRange(standingHoldRange.x, standingHoldRange.y)
            : RandomRange(seatedHoldRange.x, seatedHoldRange.y);

        return new InstanceState
        {
            matrix = matrix,
            playbackState = initialState,
            clipTime = initialState == PlaybackState.SeatedHold ? 1f : 0f,
            holdTimer = holdDuration,
            outfitIndex = outfits.Length > 0 ? randomGenerator.Next(0, outfits.Length) : 0,
        };
    }

    private void BuildChunksForInstances(
        Vector3[] positions,
        Vector3 instanceBoundsSize,
        out float leftEdge,
        out float frontEdge,
        out int chunkCountX,
        out int chunkCountZ)
    {
        Vector3 min = new(float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity);
        Vector3 max = new(float.NegativeInfinity, float.NegativeInfinity, float.NegativeInfinity);

        for (int i = 0; i < positions.Length; i++)
        {
            min = Vector3.Min(min, positions[i]);
            max = Vector3.Max(max, positions[i]);
        }

        if (positions.Length == 0)
        {
            min = transform.position;
            max = transform.position;
        }

        leftEdge = min.x - instanceBoundsSize.x * 0.5f;
        frontEdge = min.z - instanceBoundsSize.z * 0.5f;

        float width = Mathf.Max(chunkSize.x, (max.x - min.x) + instanceBoundsSize.x);
        float depth = Mathf.Max(chunkSize.y, (max.z - min.z) + instanceBoundsSize.z);
        chunkCountX = Mathf.Max(1, Mathf.CeilToInt(width / chunkSize.x));
        chunkCountZ = Mathf.Max(1, Mathf.CeilToInt(depth / chunkSize.y));

        for (int z = 0; z < chunkCountZ; z++)
        {
            for (int x = 0; x < chunkCountX; x++)
            {
                chunks.Add(new Chunk());
            }
        }
    }

    private void AddInstanceToChunk(
        int instanceIndex,
        Vector3 position,
        float leftEdge,
        float frontEdge,
        int chunkCountX,
        int chunkCountZ,
        Vector3 instanceBoundsSize)
    {
        int chunkX = Mathf.Clamp(Mathf.FloorToInt((position.x - leftEdge) / chunkSize.x), 0, chunkCountX - 1);
        int chunkZ = Mathf.Clamp(Mathf.FloorToInt((position.z - frontEdge) / chunkSize.y), 0, chunkCountZ - 1);
        Chunk chunk = chunks[(chunkZ * chunkCountX) + chunkX];
        chunk.instanceIndices.Add(instanceIndex);

        Bounds instanceBounds = new Bounds(
            position + new Vector3(0f, characterHeight * 0.5f, 0f),
            instanceBoundsSize);

        if (!chunk.hasBounds)
        {
            chunk.bounds = instanceBounds;
            chunk.hasBounds = true;
        }
        else
        {
            chunk.bounds.Encapsulate(instanceBounds.min);
            chunk.bounds.Encapsulate(instanceBounds.max);
        }
    }

    private Quaternion ResolveSeatLayoutRotation(CrowdSeatLayoutSeat seat)
    {
        if (!useSeatLayoutForward)
        {
            return Quaternion.Euler(0f, facingYaw, 0f) * Quaternion.Euler(modelRotationEuler);
        }

        Vector3 worldForward = seat.forward;
        worldForward.y = 0f;
        if (worldForward.sqrMagnitude < 0.0001f)
        {
            worldForward = Vector3.forward;
        }

        return Quaternion.LookRotation(worldForward.normalized, Vector3.up)
            * Quaternion.Euler(0f, seatLayoutForwardYawOffset, 0f)
            * Quaternion.Euler(modelRotationEuler);
    }

    private Vector3 ResolveSeatLayoutWorldPosition(CrowdSeatLayoutData layout, CrowdSeatLayoutSeat seat)
    {
        return ResolveSeatLayoutWorldPoint(layout, seat.position);
    }

    private Vector3 ResolveSeatLayoutWorldPoint(CrowdSeatLayoutData layout, Vector3 point)
    {
        Vector3 position = point + seatLayoutWorldOffset;
        if (layout != null && layout.positionsRelativeToSourceCenter)
        {
            position += transform.position;
        }

        return position;
    }

    private Vector3 ResolveSeatLayoutInstanceBoundsSize()
    {
        float width = Mathf.Max(0.5f, characterScale);
        float depth = Mathf.Max(0.5f, characterScale);
        return new Vector3(width, characterHeight, depth);
    }

    private Vector3 ResolveSeatLayoutLateralJitter(CrowdSeatLayoutSeat seat)
    {
        if (seatLayoutLateralJitter <= 0f)
        {
            return Vector3.zero;
        }

        Vector3 forward = seat.forward;
        forward.y = 0f;
        if (forward.sqrMagnitude < 0.0001f)
        {
            forward = Vector3.forward;
        }

        Vector3 lateral = Vector3.Cross(Vector3.up, forward.normalized);
        if (lateral.sqrMagnitude < 0.0001f)
        {
            return Vector3.zero;
        }

        float offset = RandomRange(-seatLayoutLateralJitter, seatLayoutLateralJitter);
        return lateral.normalized * offset;
    }

    private int ResolveInstancesPerRow()
    {
        if (columnsPerRow > 0)
        {
            return Mathf.Min(columnsPerRow, instanceCount);
        }

        return Mathf.Max(1, Mathf.FloorToInt(areaSize.x / rowSpacingX));
    }

    private int ResolveRowCount(int instancesPerRow)
    {
        return Mathf.CeilToInt(instanceCount / (float)Mathf.Max(1, instancesPerRow));
    }

    private float ResolveLayoutWidth(int instancesPerRow)
    {
        float seatingWidth = Mathf.Max(1f, Mathf.Max(1, instancesPerRow) * rowSpacingX);
        return Mathf.Max(areaSize.x, seatingWidth);
    }

    private float ResolveCrowdDepth(int rowCount)
    {
        return Mathf.Max(areaSize.y, Mathf.Max(1f, rowCount * rowSpacingZ));
    }

    private void UpdateInstanceAnimation(float deltaTime)
    {
        float sitDuration = Mathf.Max(0.0001f, bakedClips[(int)RuntimeClip.Sit].duration);
        float standDuration = Mathf.Max(0.0001f, bakedClips[(int)RuntimeClip.Stand].duration);

        for (int i = 0; i < instances.Length; i++)
        {
            InstanceState state = instances[i];
            switch (state.playbackState)
            {
                case PlaybackState.StandingHold:
                    state.holdTimer -= deltaTime;
                    state.clipTime = 0f;
                    if (state.holdTimer <= 0f)
                    {
                        state.playbackState = PlaybackState.SittingDown;
                        state.clipTime = 0f;
                    }
                    break;

                case PlaybackState.SittingDown:
                    state.clipTime += deltaTime / sitDuration;
                    if (state.clipTime >= 1f)
                    {
                        state.playbackState = PlaybackState.SeatedHold;
                        state.clipTime = 1f;
                        state.holdTimer = RandomRange(seatedHoldRange.x, seatedHoldRange.y);
                    }
                    break;

                case PlaybackState.SeatedHold:
                    state.holdTimer -= deltaTime;
                    state.clipTime = 1f;
                    if (state.holdTimer <= 0f)
                    {
                        state.playbackState = PlaybackState.StandingUp;
                        state.clipTime = 0f;
                    }
                    break;

                case PlaybackState.StandingUp:
                    state.clipTime += deltaTime / standDuration;
                    if (state.clipTime >= 1f)
                    {
                        state.playbackState = PlaybackState.StandingHold;
                        state.clipTime = 0f;
                        state.holdTimer = RandomRange(standingHoldRange.x, standingHoldRange.y);
                    }
                    break;
            }

            instances[i] = state;
        }
    }

    private float RandomValue()
    {
        randomGenerator ??= new System.Random(randomSeed);
        return (float)randomGenerator.NextDouble();
    }

    private float RandomRange(float min, float max)
    {
        return Mathf.Lerp(min, max, RandomValue());
    }

    private void RenderCrowd()
    {
        Camera targetCamera = Camera.main;
        if (targetCamera == null)
        {
            return;
        }

        frameDrawCallCount = 0;
        frameSetPassCount = 0;
        frameVisibleInstanceCount = 0;
        frameVisibleMeshInstanceCount = 0;
        frameVisibleBillboardInstanceCount = 0;
        frameVisibleChunkCount = 0;
        frameTriangleCount = 0;
        ResetBillboardFrameBatches();

        GeometryUtility.CalculateFrustumPlanes(targetCamera, frustumPlanes);

        foreach (Chunk chunk in chunks)
        {
            if (!chunk.hasBounds || !GeometryUtility.TestPlanesAABB(frustumPlanes, chunk.bounds))
            {
                continue;
            }

            frameVisibleChunkCount++;
            DrawChunk(chunk);
        }

        FlushQueuedBillboards(billboardMesh);

        lastDrawCallCount = frameDrawCallCount;
        lastSetPassCount = frameSetPassCount;
        lastVisibleInstanceCount = frameVisibleInstanceCount;
        lastVisibleMeshInstanceCount = frameVisibleMeshInstanceCount;
        lastVisibleBillboardInstanceCount = frameVisibleBillboardInstanceCount;
        lastVisibleChunkCount = frameVisibleChunkCount;
        lastTriangleCount = frameTriangleCount;
    }

    private void DrawChunk(Chunk chunk)
    {
        Camera targetCamera = Camera.main;
        Mesh meshLod = SelectMeshLod(chunk);
        bool canUseBillboards = HasEnabledBillboards();
        if (ShouldForceBillboards() && canUseBillboards)
        {
            DrawBillboardChunk(chunk);
            return;
        }

        if (meshLod == null)
        {
            return;
        }

        if (!canUseBillboards)
        {
            if (UseNonInstancedWebGLMeshFallback())
            {
                DrawMeshChunkNonInstanced(chunk, meshLod);
                return;
            }

            DrawMeshChunk(chunk, meshLod);
            return;
        }

        float billboardDistanceThreshold = ResolveBillboardDistance();
        float halfTransitionBand = ResolveBillboardTransitionBand() * 0.5f;
        if (halfTransitionBand <= 0f || targetCamera == null)
        {
            bool useBillboard = Vector3.Distance(targetCamera != null ? targetCamera.transform.position : Vector3.zero, chunk.bounds.center) >= billboardDistanceThreshold;
            if (useBillboard)
            {
                DrawBillboardChunk(chunk);
                return;
            }

            if (UseNonInstancedWebGLMeshFallback())
            {
                DrawMeshChunkNonInstanced(chunk, meshLod);
                return;
            }

            DrawMeshChunk(chunk, meshLod);
            return;
        }

        Vector3 cameraPosition = targetCamera.transform.position;
        float bandStart = billboardDistanceThreshold - halfTransitionBand;
        float bandEnd = billboardDistanceThreshold + halfTransitionBand;
        float chunkMinDistance = Vector3.Distance(cameraPosition, chunk.bounds.ClosestPoint(cameraPosition));
        float chunkMaxDistance = Vector3.Distance(cameraPosition, chunk.bounds.center) + chunk.bounds.extents.magnitude;

        if (chunkMaxDistance <= bandStart)
        {
            if (UseNonInstancedWebGLMeshFallback())
            {
                DrawMeshChunkNonInstanced(chunk, meshLod);
                return;
            }

            DrawMeshChunk(chunk, meshLod);
            return;
        }

        if (chunkMinDistance >= bandEnd)
        {
            DrawBillboardChunk(chunk);
            return;
        }

        DrawChunkWithBillboardTransition(chunk, meshLod, billboardDistanceThreshold, halfTransitionBand, cameraPosition);
    }

    private void DrawChunkWithBillboardTransition(
        Chunk chunk,
        Mesh meshLod,
        float billboardDistanceThreshold,
        float halfTransitionBand,
        Vector3 cameraPosition)
    {
        if (UseNonInstancedWebGLMeshFallback())
        {
            DrawMeshChunkNonInstancedTransition(chunk, meshLod, billboardDistanceThreshold, halfTransitionBand, cameraPosition);
        }
        else
        {
            DrawMeshChunkTransition(chunk, meshLod, billboardDistanceThreshold, halfTransitionBand, cameraPosition);
        }

        DrawBillboardChunkTransition(chunk, billboardDistanceThreshold, halfTransitionBand, cameraPosition);
    }

    private void DrawMeshChunkTransition(
        Chunk chunk,
        Mesh drawMesh,
        float billboardDistanceThreshold,
        float halfTransitionBand,
        Vector3 cameraPosition)
    {
        int countInBatch = 0;
        for (int i = 0; i < chunk.instanceIndices.Count; i++)
        {
            int instanceIndex = chunk.instanceIndices[i];
            InstanceState state = instances[instanceIndex];
            float meshFade = 1f - ComputeBillboardBlend(state.matrix.GetColumn(3), cameraPosition, billboardDistanceThreshold, halfTransitionBand);
            if (meshFade <= 0f)
            {
                continue;
            }

            OutfitPreset outfit = outfits[Mathf.Clamp(state.outfitIndex, 0, outfits.Length - 1)];
            RuntimeClip runtimeClip = GetRuntimeClip(state.playbackState);
            matrixBatch[countInBatch] = state.matrix;
            colorRBatch[countInBatch] = outfit.colorR;
            colorGBatch[countInBatch] = outfit.colorG;
            colorBBatch[countInBatch] = outfit.colorB;
            colorABatch[countInBatch] = outfit.colorA;
            animDataBatch[countInBatch] = new Vector4(
                (float)runtimeClip,
                state.clipTime,
                ResolveRenderModeFlag(false),
                ResolveDebugModeFlag());
            transitionFadeBatch[countInBatch] = meshFade;
            frameVisibleInstanceCount++;
            frameVisibleMeshInstanceCount++;
            countInBatch++;

            if (countInBatch == maxInstancesPerBatch)
            {
                FlushBatch(drawMesh, runtimeMaterial, countInBatch);
                countInBatch = 0;
            }
        }

        if (countInBatch > 0)
        {
            FlushBatch(drawMesh, runtimeMaterial, countInBatch);
        }
    }

    private void DrawMeshChunkNonInstancedTransition(
        Chunk chunk,
        Mesh drawMesh,
        float billboardDistanceThreshold,
        float halfTransitionBand,
        Vector3 cameraPosition)
    {
        for (int i = 0; i < chunk.instanceIndices.Count; i++)
        {
            int instanceIndex = chunk.instanceIndices[i];
            InstanceState state = instances[instanceIndex];
            float meshFade = 1f - ComputeBillboardBlend(state.matrix.GetColumn(3), cameraPosition, billboardDistanceThreshold, halfTransitionBand);
            if (meshFade <= 0f)
            {
                continue;
            }

            OutfitPreset outfit = outfits[Mathf.Clamp(state.outfitIndex, 0, outfits.Length - 1)];
            RuntimeClip runtimeClip = GetRuntimeClip(state.playbackState);
            Vector4 animData = new(
                (float)runtimeClip,
                state.clipTime,
                ResolveRenderModeFlag(false),
                ResolveDebugModeFlag());

            DrawSingleMeshInstance(drawMesh, runtimeMaterial, state.matrix, outfit, animData, meshFade);
        }
    }

    private void DrawBillboardChunkTransition(
        Chunk chunk,
        float billboardDistanceThreshold,
        float halfTransitionBand,
        Vector3 cameraPosition)
    {
        bool useDedicatedBillboardMaterial = UsesDedicatedBillboardMaterial();
        for (int i = 0; i < chunk.instanceIndices.Count; i++)
        {
            int instanceIndex = chunk.instanceIndices[i];
            InstanceState state = instances[instanceIndex];
            Material passMaterial = ResolveBillboardMaterialForInstance(state, cameraPosition);
            if (passMaterial == null)
            {
                continue;
            }

            float billboardFade = ComputeBillboardBlend(state.matrix.GetColumn(3), cameraPosition, billboardDistanceThreshold, halfTransitionBand);
            if (billboardFade <= 0f)
            {
                continue;
            }

            QueueBillboardInstance(passMaterial, useDedicatedBillboardMaterial, state, cameraPosition, billboardFade);
        }
    }

    private float ComputeBillboardBlend(Vector3 position, Vector3 cameraPosition, float billboardDistanceThreshold, float halfTransitionBand)
    {
        if (halfTransitionBand <= 0f)
        {
            return Vector3.Distance(cameraPosition, position) >= billboardDistanceThreshold ? 1f : 0f;
        }

        float startDistance = billboardDistanceThreshold - halfTransitionBand;
        float endDistance = billboardDistanceThreshold + halfTransitionBand;
        float distance = Vector3.Distance(cameraPosition, position);
        return Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(startDistance, endDistance, distance));
    }

    private float ResolveBillboardTransitionBand()
    {
        return billboardTransitionBand;
    }

    private Mesh SelectMeshLod(Chunk chunk)
    {
        if (lodMeshes == null || lodMeshes.Length == 0)
        {
            return crowdMesh;
        }

        if (lodMeshes.Length == 1 || Camera.main == null)
        {
            int nearestEnabledLod = FindNearestEnabledMeshLodIndex();
            return nearestEnabledLod >= 0 ? lodMeshes[nearestEnabledLod] : null;
        }

        float distance = Vector3.Distance(Camera.main.transform.position, chunk.bounds.center);
        return SelectMeshLodForDistance(distance);
    }

    private Mesh SelectMeshLodForDistance(float distance)
    {
        float activeLod1Distance = ResolveLod1Distance();
        float activeLod2Distance = ResolveLod2Distance();
        float activeLod3Distance = ResolveLod3Distance();
        bool skipLod0OnWebGL = ShouldSkipLod0OnWebGL();

        if (lodMeshes == null || lodMeshes.Length == 0)
        {
            return crowdMesh;
        }

        if (distance >= activeLod3Distance && IsMeshLodEnabled(3))
        {
            return lodMeshes[3];
        }

        if (distance >= activeLod2Distance && IsMeshLodEnabled(2))
        {
            return lodMeshes[2];
        }

        if (skipLod0OnWebGL && IsMeshLodEnabled(1))
        {
            return lodMeshes[1];
        }

        if (distance >= activeLod1Distance && IsMeshLodEnabled(1))
        {
            return lodMeshes[1];
        }

        int nearestEnabledLod = FindNearestEnabledMeshLodIndex();
        return nearestEnabledLod >= 0 ? lodMeshes[nearestEnabledLod] : null;
    }

    private void DrawMeshChunk(Chunk chunk, Mesh drawMesh)
    {
        int countInBatch = 0;
        for (int i = 0; i < chunk.instanceIndices.Count; i++)
        {
            int instanceIndex = chunk.instanceIndices[i];
            InstanceState state = instances[instanceIndex];
            OutfitPreset outfit = outfits[Mathf.Clamp(state.outfitIndex, 0, outfits.Length - 1)];

            RuntimeClip runtimeClip = GetRuntimeClip(state.playbackState);
            matrixBatch[countInBatch] = state.matrix;
            colorRBatch[countInBatch] = outfit.colorR;
            colorGBatch[countInBatch] = outfit.colorG;
            colorBBatch[countInBatch] = outfit.colorB;
            colorABatch[countInBatch] = outfit.colorA;
            animDataBatch[countInBatch] = new Vector4(
                (float)runtimeClip,
                state.clipTime,
                ResolveRenderModeFlag(false),
                ResolveDebugModeFlag());
            transitionFadeBatch[countInBatch] = 1f;
            frameVisibleInstanceCount++;
            frameVisibleMeshInstanceCount++;
            countInBatch++;

            if (countInBatch == maxInstancesPerBatch)
            {
                FlushBatch(drawMesh, runtimeMaterial, countInBatch);
                countInBatch = 0;
            }
        }

        if (countInBatch > 0)
        {
            FlushBatch(drawMesh, runtimeMaterial, countInBatch);
        }
    }

    private void DrawMeshChunkNonInstanced(Chunk chunk, Mesh drawMesh)
    {
        for (int i = 0; i < chunk.instanceIndices.Count; i++)
        {
            int instanceIndex = chunk.instanceIndices[i];
            InstanceState state = instances[instanceIndex];
            OutfitPreset outfit = outfits[Mathf.Clamp(state.outfitIndex, 0, outfits.Length - 1)];
            RuntimeClip runtimeClip = GetRuntimeClip(state.playbackState);
            Vector4 animData = new(
                (float)runtimeClip,
                state.clipTime,
                ResolveRenderModeFlag(false),
                ResolveDebugModeFlag());

            DrawSingleMeshInstance(drawMesh, runtimeMaterial, state.matrix, outfit, animData, 1f);
        }
    }

    private void DrawBillboardChunk(Chunk chunk)
    {
        Vector3 cameraPosition = Camera.main != null ? Camera.main.transform.position : Vector3.zero;
        bool useDedicatedBillboardMaterial = UsesDedicatedBillboardMaterial();
        for (int i = 0; i < chunk.instanceIndices.Count; i++)
        {
            int instanceIndex = chunk.instanceIndices[i];
            InstanceState state = instances[instanceIndex];
            Material passMaterial = ResolveBillboardMaterialForInstance(state, cameraPosition);
            if (passMaterial == null)
            {
                continue;
            }

            QueueBillboardInstance(passMaterial, useDedicatedBillboardMaterial, state, cameraPosition, 1f);
        }
    }

    private Mesh SelectLodMesh(Chunk chunk, out bool useBillboard)
    {
        useBillboard = false;
        float activeLod1Distance = ResolveLod1Distance();
        float activeLod2Distance = ResolveLod2Distance();
        float activeBillboardDistance = ResolveBillboardDistance();
        bool skipLod0OnWebGL = ShouldSkipLod0OnWebGL();

        if (ShouldForceBillboards())
        {
            if (HasEnabledBillboards())
            {
                useBillboard = true;
                return billboardMesh;
            }

            return crowdMesh;
        }

        if (lodMeshes == null || lodMeshes.Length == 0)
        {
            return crowdMesh;
        }

        if (lodMeshes.Length == 1 || Camera.main == null)
        {
            int fallbackLodIndex = FindNearestEnabledMeshLodIndex();
            return fallbackLodIndex >= 0 ? lodMeshes[fallbackLodIndex] : crowdMesh;
        }

        float distance = Vector3.Distance(Camera.main.transform.position, chunk.bounds.center);
        if (HasEnabledBillboards() && distance >= activeBillboardDistance)
        {
            useBillboard = true;
            return billboardMesh;
        }

        if (distance >= ResolveLod3Distance() && IsMeshLodEnabled(3))
        {
            return lodMeshes[3];
        }

        if (distance >= activeLod2Distance && IsMeshLodEnabled(2))
        {
            return lodMeshes[2];
        }

        if (skipLod0OnWebGL && IsMeshLodEnabled(1))
        {
            return lodMeshes[1];
        }

        if (distance >= activeLod1Distance && IsMeshLodEnabled(1))
        {
            return lodMeshes[1];
        }

        int nearestEnabledLod = FindNearestEnabledMeshLodIndex();
        return nearestEnabledLod >= 0 ? lodMeshes[nearestEnabledLod] : crowdMesh;
    }

    private Matrix4x4 CreateBillboardMatrix(InstanceState state, Vector3 cameraPosition)
    {
        Matrix4x4 sourceMatrix = state.matrix;
        Vector3 position = sourceMatrix.GetColumn(3);
        Vector3 scale = new(
            sourceMatrix.GetColumn(0).magnitude,
            sourceMatrix.GetColumn(1).magnitude,
            sourceMatrix.GetColumn(2).magnitude);
        scale *= billboardScale;
        Vector3 sourceForward = sourceMatrix.GetColumn(2);
        sourceForward.y = 0f;
        if (sourceForward.sqrMagnitude < 0.0001f)
        {
            sourceForward = Vector3.forward;
        }

        position += sourceForward.normalized * billboardDepthOffset;

        Vector3 toCamera = cameraPosition - position;
        toCamera.y = 0f;
        if (toCamera.sqrMagnitude < 0.0001f)
        {
            toCamera = Vector3.forward;
        }

        Quaternion rotation = Quaternion.LookRotation(toCamera.normalized, Vector3.up);
        position.y += ComputeBillboardHeightOffset(state);
        return Matrix4x4.TRS(position, rotation, scale);
    }

    private float ComputeBillboardHeightOffset(InstanceState state)
    {
        return state.playbackState switch
        {
            PlaybackState.StandingHold => billboardStandingHeightOffset,
            PlaybackState.SittingDown => Mathf.Lerp(
                billboardStandingHeightOffset,
                billboardSeatedHeightOffset,
                Mathf.SmoothStep(0f, 1f, state.clipTime)),
            PlaybackState.SeatedHold => billboardSeatedHeightOffset,
            PlaybackState.StandingUp => Mathf.Lerp(
                billboardSeatedHeightOffset,
                billboardStandingHeightOffset,
                Mathf.SmoothStep(0f, 1f, state.clipTime)),
            _ => billboardStandingHeightOffset,
        };
    }

    private bool UsesDedicatedBillboardMaterial()
    {
        return billboardMaterialTemplate != null && billboardMaterialTemplate.shader != null;
    }

    private Material[] BuildBillboardMaterials(Texture2D firstVariant, Texture2D secondVariant)
    {
        List<Material> materials = new();
        Texture2D[] variants = { firstVariant, secondVariant };
        bool useDedicatedBillboardMaterial = UsesDedicatedBillboardMaterial();
        for (int variantIndex = 0; variantIndex < variants.Length; variantIndex++)
        {
            Texture2D variant = variants[variantIndex];
            if (variant == null)
            {
                continue;
            }

            Material material;
            if (useDedicatedBillboardMaterial)
            {
                material = new Material(billboardMaterialTemplate);
            }
            else
            {
                material = materialTemplate != null
                    ? new Material(materialTemplate)
                    : new Material(Shader.Find("ComputeCrowd/CrowdRender"));
            }

            if (material.shader == null)
            {
                continue;
            }

            material.enableInstancing = true;
            if (useDedicatedBillboardMaterial)
            {
                material.SetTexture(BaseMapId, variant);
            }
            else
            {
                material.SetTexture(BillboardMapId, variant);
                material.SetTexture(PoseTextureId, poseTexture);
                material.SetInt(BoneCountId, 1);
                material.SetVector(ClipMeta0Id, Vector4.zero);
                material.SetVector(ClipMeta1Id, Vector4.zero);
                material.SetVector(ClipMeta2Id, Vector4.zero);
            }

            materials.Add(material);
        }

        return materials.ToArray();
    }

    private int GetBillboardVariantCount()
    {
        return billboardStandingFrontMaterials?.Length ?? 0;
    }

    private int GetBillboardVariantIndex(int outfitIndex)
    {
        if (billboardStandingFrontMaterials == null || billboardStandingFrontMaterials.Length == 0)
        {
            return -1;
        }

        int presetIndex = Mathf.Clamp(outfitIndex, 0, outfits.Length - 1);
        int variantIndex = outfits[presetIndex].billboardVariant;
        return Mathf.Clamp(variantIndex, 0, billboardStandingFrontMaterials.Length - 1);
    }

    private Material ResolveBillboardMaterial(int variantIndex, PlaybackState playbackState, bool useSideView)
    {
        if (variantIndex < 0)
        {
            return null;
        }

        bool prefersSeated = UsesSeatedBillboard(playbackState);
        Material[] primaryMaterials = prefersSeated
            ? (useSideView ? billboardSeatedSideMaterials : billboardSeatedFrontMaterials)
            : (useSideView ? billboardStandingSideMaterials : billboardStandingFrontMaterials);
        Material[] fallbackMaterials = prefersSeated ? billboardSeatedFrontMaterials : billboardStandingFrontMaterials;

        if (primaryMaterials != null &&
            variantIndex < primaryMaterials.Length &&
            primaryMaterials[variantIndex] != null)
        {
            return primaryMaterials[variantIndex];
        }

        if (fallbackMaterials == null || variantIndex >= fallbackMaterials.Length)
        {
            return null;
        }

        return fallbackMaterials[variantIndex];
    }

    private Material ResolveBillboardMaterialForInstance(InstanceState state, Vector3 cameraPosition)
    {
        int variantIndex = GetBillboardVariantIndex(state.outfitIndex);
        if (variantIndex < 0)
        {
            return null;
        }

        bool useSideView = UsesSideBillboard(state, cameraPosition);
        PlaybackState billboardPlaybackState = UsesSeatedBillboard(state.playbackState)
            ? PlaybackState.SeatedHold
            : PlaybackState.StandingHold;

        return ResolveBillboardMaterial(variantIndex, billboardPlaybackState, useSideView);
    }

    private static bool UsesSeatedBillboard(PlaybackState playbackState)
    {
        return playbackState != PlaybackState.StandingHold;
    }

    private bool UsesSideBillboard(InstanceState state, Vector3 cameraPosition)
    {
        Vector3 position = state.matrix.GetColumn(3);
        Vector3 toCamera = cameraPosition - position;
        toCamera.y = 0f;
        if (toCamera.sqrMagnitude < 0.0001f)
        {
            return false;
        }

        Vector3 forward = state.matrix.GetColumn(2);
        forward.y = 0f;
        if (forward.sqrMagnitude < 0.0001f)
        {
            forward = Vector3.forward;
        }

        float facingDot = Mathf.Abs(Vector3.Dot(forward.normalized, toCamera.normalized));
        return facingDot < billboardSideViewDotThreshold;
    }

    private static RuntimeClip GetRuntimeClip(PlaybackState playbackState)
    {
        return playbackState switch
        {
            PlaybackState.StandingHold => RuntimeClip.Idle,
            PlaybackState.SittingDown => RuntimeClip.Sit,
            PlaybackState.SeatedHold => RuntimeClip.Sit,
            PlaybackState.StandingUp => RuntimeClip.Stand,
            _ => RuntimeClip.Idle,
        };
    }

    private float ResolveRenderModeFlag(bool isBillboardBatch)
    {
        if (ResolveActiveDebugRenderMode() == DebugRenderMode.BillboardsOnly)
        {
            return 1f;
        }

        return isBillboardBatch ? 1f : 0f;
    }

    private float ResolveDebugModeFlag()
    {
        return (float)ResolveActiveDebugRenderMode();
    }

    private DebugRenderMode ResolveActiveDebugRenderMode()
    {
        if (debugRenderMode != DebugRenderMode.Normal)
        {
            return debugRenderMode;
        }

        if (Application.platform == RuntimePlatform.WebGLPlayer && forceWebGLBillboardsOnly)
        {
            return DebugRenderMode.BillboardsOnly;
        }

        bool webGlFallbackAvailable =
            useWebGLBillboardFallback &&
            Application.platform == RuntimePlatform.WebGLPlayer &&
            billboardMesh != null &&
            billboardStandingFrontMaterials != null &&
            billboardStandingFrontMaterials.Length > 0;

        return webGlFallbackAvailable ? DebugRenderMode.BillboardsOnly : DebugRenderMode.Normal;
    }

    private bool ShouldForceBillboards()
    {
        return ResolveActiveDebugRenderMode() == DebugRenderMode.BillboardsOnly;
    }

    private bool ShouldSkipLod0OnWebGL()
    {
        return Application.platform == RuntimePlatform.WebGLPlayer && useWebGLSkipLod0;
    }

    private bool UseNonInstancedWebGLMeshFallback()
    {
        return Application.platform == RuntimePlatform.WebGLPlayer && useWebGLNonInstancedMeshFallback;
    }

    private float ResolveLod1Distance()
    {
        return Application.platform == RuntimePlatform.WebGLPlayer && useWebGLLodDistanceOverrides
            ? webGLLod1Distance
            : lod1Distance;
    }

    private float ResolveLod2Distance()
    {
        return Application.platform == RuntimePlatform.WebGLPlayer && useWebGLLodDistanceOverrides
            ? webGLLod2Distance
            : lod2Distance;
    }

    private float ResolveLod3Distance()
    {
        return Application.platform == RuntimePlatform.WebGLPlayer && useWebGLLodDistanceOverrides
            ? webGLLod3Distance
            : lod3Distance;
    }

    private float ResolveBillboardDistance()
    {
        return Application.platform == RuntimePlatform.WebGLPlayer && useWebGLLodDistanceOverrides
            ? webGLBillboardDistance
            : billboardDistance;
    }

    private Mesh CreateBillboardMesh()
    {
        Texture2D sourceTexture = billboardColor01 != null ? billboardColor01 : billboardColor02;
        float aspect = sourceTexture != null && sourceTexture.height > 0
            ? sourceTexture.width / (float)sourceTexture.height
            : 0.5f;
        float width = Mathf.Max(0.1f, characterHeight * aspect);
        float halfWidth = width * 0.5f;

        Mesh mesh = new Mesh
        {
            name = "CrowdBillboardQuad",
            hideFlags = HideFlags.HideAndDontSave,
        };

        mesh.vertices = new[]
        {
            new Vector3(-halfWidth, 0f, 0f),
            new Vector3(halfWidth, 0f, 0f),
            new Vector3(-halfWidth, characterHeight, 0f),
            new Vector3(halfWidth, characterHeight, 0f),
        };

        mesh.normals = new[]
        {
            Vector3.forward,
            Vector3.forward,
            Vector3.forward,
            Vector3.forward,
        };

        mesh.uv = new[]
        {
            new Vector2(0f, 0f),
            new Vector2(1f, 0f),
            new Vector2(0f, 1f),
            new Vector2(1f, 1f),
        };

        mesh.triangles = new[]
        {
            0, 1, 2,
            2, 1, 3,
        };

        mesh.bindposes = new[] { Matrix4x4.identity };
        mesh.boneWeights = new[]
        {
            CreateFullWeight(),
            CreateFullWeight(),
            CreateFullWeight(),
            CreateFullWeight(),
        };
        mesh.RecalculateBounds();
        return mesh;
    }

    private static BoneWeight CreateFullWeight()
    {
        return new BoneWeight
        {
            boneIndex0 = 0,
            weight0 = 1f,
        };
    }

    private void ResetBillboardFrameBatches()
    {
        for (int i = 0; i < activeBillboardBatchBuckets.Count; i++)
        {
            activeBillboardBatchBuckets[i].Clear();
        }

        activeBillboardBatchBuckets.Clear();
    }

    private void QueueBillboardInstance(
        Material material,
        bool useDedicatedBillboardMaterial,
        InstanceState state,
        Vector3 cameraPosition,
        float transitionFade)
    {
        if (material == null)
        {
            return;
        }

        BillboardBatchBucket bucket = GetOrCreateBillboardBatchBucket(material, useDedicatedBillboardMaterial);
        bucket.matrices.Add(CreateBillboardMatrix(state, cameraPosition));
        bucket.transitionFades.Add(transitionFade);

        if (!bucket.useDedicatedBillboardMaterial)
        {
            OutfitPreset outfit = outfits[Mathf.Clamp(state.outfitIndex, 0, outfits.Length - 1)];
            RuntimeClip runtimeClip = GetRuntimeClip(state.playbackState);
            bucket.colorRs.Add(outfit.colorR);
            bucket.colorGs.Add(outfit.colorG);
            bucket.colorBs.Add(outfit.colorB);
            bucket.colorAs.Add(outfit.colorA);
            bucket.animDatas.Add(new Vector4(
                (float)runtimeClip,
                state.clipTime,
                ResolveRenderModeFlag(true),
                ResolveDebugModeFlag()));
        }

        frameVisibleInstanceCount++;
        frameVisibleBillboardInstanceCount++;
    }

    private BillboardBatchBucket GetOrCreateBillboardBatchBucket(Material material, bool useDedicatedBillboardMaterial)
    {
        if (!billboardBatchBuckets.TryGetValue(material, out BillboardBatchBucket bucket))
        {
            bucket = new BillboardBatchBucket
            {
                material = material,
                useDedicatedBillboardMaterial = useDedicatedBillboardMaterial,
            };
            billboardBatchBuckets.Add(material, bucket);
        }

        if (!bucket.isActiveInFrame)
        {
            bucket.isActiveInFrame = true;
            activeBillboardBatchBuckets.Add(bucket);
        }

        return bucket;
    }

    private void FlushQueuedBillboards(Mesh drawMesh)
    {
        if (drawMesh == null)
        {
            return;
        }

        for (int bucketIndex = 0; bucketIndex < activeBillboardBatchBuckets.Count; bucketIndex++)
        {
            BillboardBatchBucket bucket = activeBillboardBatchBuckets[bucketIndex];
            int totalCount = bucket.matrices.Count;
            for (int startIndex = 0; startIndex < totalCount; startIndex += maxInstancesPerBatch)
            {
                int count = Mathf.Min(maxInstancesPerBatch, totalCount - startIndex);
                for (int i = 0; i < count; i++)
                {
                    int sourceIndex = startIndex + i;
                    matrixBatch[i] = bucket.matrices[sourceIndex];
                    transitionFadeBatch[i] = bucket.transitionFades[sourceIndex];

                    if (!bucket.useDedicatedBillboardMaterial)
                    {
                        colorRBatch[i] = bucket.colorRs[sourceIndex];
                        colorGBatch[i] = bucket.colorGs[sourceIndex];
                        colorBBatch[i] = bucket.colorBs[sourceIndex];
                        colorABatch[i] = bucket.colorAs[sourceIndex];
                        animDataBatch[i] = bucket.animDatas[sourceIndex];
                    }
                }

                FlushBillboardBatch(drawMesh, bucket.material, count, bucket.useDedicatedBillboardMaterial);
            }
        }
    }

    private void FlushBatch(Mesh drawMesh, Material material, int count)
    {
        if (drawMesh == null || material == null || count <= 0 || matrixBatch == null)
        {
            return;
        }

        frameDrawCallCount++;
        frameSetPassCount++;
        frameTriangleCount += GetTriangleCount(drawMesh) * (long)count;

        materialPropertyBlock.Clear();
        SetExactVectorArray(ColorRId, colorRBatch, count);
        SetExactVectorArray(ColorGId, colorGBatch, count);
        SetExactVectorArray(ColorBId, colorBBatch, count);
        SetExactVectorArray(ColorAId, colorABatch, count);
        SetExactVectorArray(AnimDataId, animDataBatch, count);
        SetExactFloatArray(TransitionFadeId, transitionFadeBatch, count);

        Graphics.DrawMeshInstanced(
            drawMesh,
            0,
            material,
            matrixBatch,
            count,
            materialPropertyBlock,
            ShadowCastingMode.Off,
            false,
            gameObject.layer);
    }

    private void FlushBillboardBatch(Mesh drawMesh, Material material, int count, bool useDedicatedBillboardMaterial)
    {
        if (!useDedicatedBillboardMaterial)
        {
            FlushBatch(drawMesh, material, count);
            return;
        }

        if (drawMesh == null || material == null || count <= 0 || matrixBatch == null)
        {
            return;
        }

        frameDrawCallCount++;
        frameSetPassCount++;
        frameTriangleCount += GetTriangleCount(drawMesh) * (long)count;

        materialPropertyBlock.Clear();
        SetExactFloatArray(TransitionFadeId, transitionFadeBatch, count);
        Graphics.DrawMeshInstanced(
            drawMesh,
            0,
            material,
            matrixBatch,
            count,
            materialPropertyBlock,
            ShadowCastingMode.Off,
            false,
            gameObject.layer);
    }

    private void DrawSingleMeshInstance(Mesh drawMesh, Material material, Matrix4x4 matrix, OutfitPreset outfit, Vector4 animData, float transitionFade)
    {
        if (drawMesh == null || material == null || materialPropertyBlock == null)
        {
            return;
        }

        frameDrawCallCount++;
        frameSetPassCount++;
        frameVisibleInstanceCount++;
        frameVisibleMeshInstanceCount++;
        frameTriangleCount += GetTriangleCount(drawMesh);

        materialPropertyBlock.Clear();
        materialPropertyBlock.SetVector(ColorRId, outfit.colorR);
        materialPropertyBlock.SetVector(ColorGId, outfit.colorG);
        materialPropertyBlock.SetVector(ColorBId, outfit.colorB);
        materialPropertyBlock.SetVector(ColorAId, outfit.colorA);
        materialPropertyBlock.SetVector(AnimDataId, animData);
        materialPropertyBlock.SetVector(FallbackColorRId, outfit.colorR);
        materialPropertyBlock.SetVector(FallbackColorGId, outfit.colorG);
        materialPropertyBlock.SetVector(FallbackColorBId, outfit.colorB);
        materialPropertyBlock.SetVector(FallbackColorAId, outfit.colorA);
        materialPropertyBlock.SetVector(FallbackAnimDataId, animData);
        materialPropertyBlock.SetFloat(FallbackTransitionFadeId, transitionFade);
        materialPropertyBlock.SetFloat(TransitionFadeId, transitionFade);

        Graphics.DrawMesh(
            drawMesh,
            matrix,
            material,
            gameObject.layer,
            null,
            0,
            materialPropertyBlock,
            ShadowCastingMode.Off,
            false);
    }

    private static int GetTriangleCount(Mesh mesh)
    {
        if (mesh == null || mesh.subMeshCount == 0)
        {
            return 0;
        }

        return (int)(mesh.GetIndexCount(0) / 3);
    }

    private bool HasEnabledBillboards()
    {
        return ResolveActiveDebugRenderMode() != DebugRenderMode.MeshesOnly &&
            enableBillboards &&
            billboardMesh != null &&
            billboardStandingFrontMaterials != null &&
            billboardStandingFrontMaterials.Length > 0;
    }

    private bool IsMeshLodEnabled(int lodIndex)
    {
        if (lodMeshes == null || lodIndex < 0 || lodIndex >= lodMeshes.Length || lodMeshes[lodIndex] == null)
        {
            return false;
        }

        return lodIndex switch
        {
            0 => enableLod0 && !ShouldSkipLod0OnWebGL(),
            1 => enableLod1,
            2 => enableLod2,
            3 => enableLod3,
            _ => true,
        };
    }

    private int FindNearestEnabledMeshLodIndex()
    {
        if (lodMeshes == null)
        {
            return -1;
        }

        for (int lodIndex = 0; lodIndex < lodMeshes.Length; lodIndex++)
        {
            if (IsMeshLodEnabled(lodIndex))
            {
                return lodIndex;
            }
        }

        return -1;
    }

    private void AllocateBatchBuffers()
    {
        maxInstancesPerBatch = Application.platform == RuntimePlatform.WebGLPlayer
            ? WebGLMaxInstancesPerBatch
            : MaxInstancesPerBatch;
        matrixBatch = new Matrix4x4[maxInstancesPerBatch];
        colorRBatch = new Vector4[maxInstancesPerBatch];
        colorGBatch = new Vector4[maxInstancesPerBatch];
        colorBBatch = new Vector4[maxInstancesPerBatch];
        colorABatch = new Vector4[maxInstancesPerBatch];
        animDataBatch = new Vector4[maxInstancesPerBatch];
        transitionFadeBatch = new float[maxInstancesPerBatch];
    }

    private void SetExactVectorArray(int propertyId, Vector4[] source, int count)
    {
        if (source == null || count <= 0)
        {
            return;
        }

        if (count == source.Length)
        {
            materialPropertyBlock.SetVectorArray(propertyId, source);
            return;
        }

        Vector4[] exactValues = new Vector4[count];
        Array.Copy(source, exactValues, count);
        materialPropertyBlock.SetVectorArray(propertyId, exactValues);
    }

    private void SetExactFloatArray(int propertyId, float[] source, int count)
    {
        if (source == null || count <= 0)
        {
            return;
        }

        if (count == source.Length)
        {
            materialPropertyBlock.SetFloatArray(propertyId, source);
            return;
        }

        float[] exactValues = new float[count];
        Array.Copy(source, exactValues, count);
        materialPropertyBlock.SetFloatArray(propertyId, exactValues);
    }

    private void ReleaseRuntimeResources()
    {
        if (runtimeMaterial != null)
        {
            if (Application.isPlaying)
            {
                Destroy(runtimeMaterial);
            }
            else
            {
                DestroyImmediate(runtimeMaterial);
            }
            runtimeMaterial = null;
        }

        if (billboardStandingFrontMaterials != null)
        {
            for (int i = 0; i < billboardStandingFrontMaterials.Length; i++)
            {
                if (billboardStandingFrontMaterials[i] == null)
                {
                    continue;
                }

                if (Application.isPlaying)
                {
                    Destroy(billboardStandingFrontMaterials[i]);
                }
                else
                {
                    DestroyImmediate(billboardStandingFrontMaterials[i]);
                }
            }

            billboardStandingFrontMaterials = null;
        }

        if (billboardStandingSideMaterials != null)
        {
            for (int i = 0; i < billboardStandingSideMaterials.Length; i++)
            {
                if (billboardStandingSideMaterials[i] == null)
                {
                    continue;
                }

                if (Application.isPlaying)
                {
                    Destroy(billboardStandingSideMaterials[i]);
                }
                else
                {
                    DestroyImmediate(billboardStandingSideMaterials[i]);
                }
            }

            billboardStandingSideMaterials = null;
        }

        if (billboardSeatedFrontMaterials != null)
        {
            for (int i = 0; i < billboardSeatedFrontMaterials.Length; i++)
            {
                if (billboardSeatedFrontMaterials[i] == null)
                {
                    continue;
                }

                if (Application.isPlaying)
                {
                    Destroy(billboardSeatedFrontMaterials[i]);
                }
                else
                {
                    DestroyImmediate(billboardSeatedFrontMaterials[i]);
                }
            }

            billboardSeatedFrontMaterials = null;
        }

        if (billboardSeatedSideMaterials != null)
        {
            for (int i = 0; i < billboardSeatedSideMaterials.Length; i++)
            {
                if (billboardSeatedSideMaterials[i] == null)
                {
                    continue;
                }

                if (Application.isPlaying)
                {
                    Destroy(billboardSeatedSideMaterials[i]);
                }
                else
                {
                    DestroyImmediate(billboardSeatedSideMaterials[i]);
                }
            }

            billboardSeatedSideMaterials = null;
        }

        if (poseTexture != null)
        {
            if (Application.isPlaying)
            {
                Destroy(poseTexture);
            }
            else
            {
                DestroyImmediate(poseTexture);
            }
            poseTexture = null;
        }

        if (billboardMesh != null)
        {
            if (Application.isPlaying)
            {
                Destroy(billboardMesh);
            }
            else
            {
                DestroyImmediate(billboardMesh);
            }
            billboardMesh = null;
        }

        if (hideSourceCharacter && resolvedSourceRoot != null && resolvedSourceRoot.scene.IsValid() && !resolvedSourceRoot.activeSelf)
        {
            resolvedSourceRoot.SetActive(true);
        }

        materialPropertyBlock = null;
        crowdMesh = null;
        lodMeshes = null;
        instances = null;
        bakedClips = null;
        billboardBatchBuckets.Clear();
        activeBillboardBatchBuckets.Clear();
        hasCrowdBounds = false;
        crowdBounds = default;
        chunks.Clear();
    }

    private void OnDrawGizmosSelected()
    {
        if (!drawChunkGizmos)
        {
            return;
        }

        Gizmos.color = new Color(0.9f, 0.75f, 0.2f, 0.7f);

        if (hasCrowdBounds)
        {
            Gizmos.DrawWireCube(crowdBounds.center, crowdBounds.size);
        }
        else if (layoutMode == LayoutMode.SeatLayoutAsset && TryResolveSeatLayoutPreviewBounds(out Bounds previewBounds))
        {
            Gizmos.DrawWireCube(previewBounds.center, previewBounds.size);
        }
        else
        {
            int instancesPerRow = ResolveInstancesPerRow();
            int rowCount = ResolveRowCount(instancesPerRow);
            float width = ResolveLayoutWidth(instancesPerRow);
            float depth = ResolveCrowdDepth(rowCount);

            Gizmos.DrawWireCube(
                transform.position + new Vector3(0f, ((rowCount - 1) * rowSpacingY + characterHeight) * 0.5f, depth * 0.5f),
                new Vector3(width, (rowCount - 1) * rowSpacingY + characterHeight, depth));
        }

        DrawSeatLayoutDebugGizmos();
        DrawSeatLayoutAlignmentGizmos();

        if (chunks.Count == 0)
        {
            if (layoutMode == LayoutMode.SeatLayoutAsset && TryResolveSeatLayoutPreviewBounds(out Bounds previewBounds))
            {
                Vector3 min = previewBounds.min;
                int previewChunkCountX = Mathf.Max(1, Mathf.CeilToInt(previewBounds.size.x / chunkSize.x));
                int previewChunkCountZ = Mathf.Max(1, Mathf.CeilToInt(previewBounds.size.z / chunkSize.y));
                for (int z = 0; z < previewChunkCountZ; z++)
                {
                    for (int x = 0; x < previewChunkCountX; x++)
                    {
                        Vector3 center = new(
                            min.x + (x + 0.5f) * chunkSize.x,
                            previewBounds.center.y,
                            min.z + (z + 0.5f) * chunkSize.y);
                        Gizmos.DrawWireCube(center, new Vector3(chunkSize.x, Mathf.Max(characterHeight, previewBounds.size.y), chunkSize.y));
                    }
                }

                return;
            }

            int instancesPerRow = ResolveInstancesPerRow();
            int rowCount = ResolveRowCount(instancesPerRow);
            float width = ResolveLayoutWidth(instancesPerRow);
            float depth = ResolveCrowdDepth(rowCount);
            int chunkCountX = Mathf.Max(1, Mathf.CeilToInt(width / chunkSize.x));
            int chunkCountZ = Mathf.Max(1, Mathf.CeilToInt(depth / chunkSize.y));
            for (int z = 0; z < chunkCountZ; z++)
            {
                for (int x = 0; x < chunkCountX; x++)
                {
                    Vector3 center = transform.position + new Vector3(
                        -width * 0.5f + (x + 0.5f) * chunkSize.x,
                        characterHeight * 0.5f,
                        (z + 0.5f) * chunkSize.y);
                    Gizmos.DrawWireCube(center, new Vector3(chunkSize.x, characterHeight, chunkSize.y));
                }
            }

            return;
        }

        Gizmos.color = new Color(0.15f, 0.7f, 1f, 0.65f);
        foreach (Chunk chunk in chunks)
        {
            if (chunk.hasBounds)
            {
                Gizmos.DrawWireCube(chunk.bounds.center, chunk.bounds.size);
            }
        }
    }

    private bool TryResolveSeatLayoutPreviewBounds(out Bounds previewBounds)
    {
        previewBounds = default;

        if (layoutMode != LayoutMode.SeatLayoutAsset ||
            !CrowdSeatLayoutUtility.TryParse(seatLayoutAsset, out CrowdSeatLayoutData layout, out _))
        {
            return false;
        }

        Vector3 min = new(float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity);
        Vector3 max = new(float.NegativeInfinity, float.NegativeInfinity, float.NegativeInfinity);
        for (int i = 0; i < layout.seats.Length; i++)
        {
            Vector3 worldPosition = ResolveSeatLayoutWorldPosition(layout, layout.seats[i]);
            min = Vector3.Min(min, worldPosition);
            max = Vector3.Max(max, worldPosition);
        }

        if (layout.seats.Length == 0)
        {
            return false;
        }

        previewBounds = new Bounds((min + max) * 0.5f, Vector3.Max(max - min, new Vector3(0.1f, characterHeight, 0.1f)));
        return true;
    }

    private void DrawSeatLayoutDebugGizmos()
    {
        if (!drawSeatLayoutDebugGizmos ||
            layoutMode != LayoutMode.SeatLayoutAsset ||
            !CrowdSeatLayoutUtility.TryParse(seatLayoutAsset, out CrowdSeatLayoutData layout, out _))
        {
            return;
        }

        int seatCount = layout.SeatCount;
        if (seatCount <= 0)
        {
            return;
        }

        int stride = Mathf.Max(1, seatLayoutDebugStride);
        if (seatCount > seatLayoutDebugMaxMarkers)
        {
            stride = Mathf.Max(stride, Mathf.CeilToInt(seatCount / (float)seatLayoutDebugMaxMarkers));
        }

        Color previousColor = Gizmos.color;
        for (int i = 0; i < seatCount; i += stride)
        {
            CrowdSeatLayoutSeat seat = layout.seats[i];
            Vector3 position = ResolveSeatLayoutWorldPosition(layout, seat);

            Gizmos.color = seatLayoutDebugMarkerColor;
            Gizmos.DrawCube(position, Vector3.one * seatLayoutDebugMarkerSize);

            if (!drawSeatLayoutForwardGizmos)
            {
                continue;
            }

            Vector3 forward = seat.forward;
            forward.y = 0f;
            if (forward.sqrMagnitude < 0.0001f)
            {
                continue;
            }

            Gizmos.color = seatLayoutDebugForwardColor;
            Gizmos.DrawLine(position, position + forward.normalized * seatLayoutDebugForwardLength);
        }

        Gizmos.color = previousColor;
    }

    private void DrawSeatLayoutAlignmentGizmos()
    {
        if (!drawSeatLayoutAlignmentGizmos ||
            layoutMode != LayoutMode.SeatLayoutAsset ||
            !CrowdSeatLayoutUtility.TryParse(seatLayoutAsset, out CrowdSeatLayoutData layout, out _))
        {
            return;
        }

        if (TryResolveSeatLayoutSourceBounds(layout, out Bounds sourceBounds))
        {
            Gizmos.color = seatLayoutSourceBoundsColor;
            Gizmos.DrawWireCube(sourceBounds.center, sourceBounds.size);
            Gizmos.DrawSphere(sourceBounds.center, seatLayoutCenterMarkerSize * 0.5f);
        }

        if (!TryResolveReferenceBounds(out Bounds referenceBounds))
        {
            return;
        }

        Gizmos.color = seatLayoutReferenceBoundsColor;
        Gizmos.DrawWireCube(referenceBounds.center, referenceBounds.size);
        Gizmos.DrawSphere(referenceBounds.center, seatLayoutCenterMarkerSize * 0.5f);

        if (TryResolveSeatLayoutSourceBounds(layout, out sourceBounds))
        {
            Gizmos.color = seatLayoutCenterDeltaColor;
            Gizmos.DrawLine(referenceBounds.center, sourceBounds.center);
        }
    }

    private bool TryResolveSeatLayoutSourceBounds(CrowdSeatLayoutData layout, out Bounds sourceBounds)
    {
        sourceBounds = default;
        if (layout == null)
        {
            return false;
        }

        Vector3 size = layout.sourceBoundsSize;
        if (size.sqrMagnitude <= 0.0001f)
        {
            size = layout.sourceBounds.max - layout.sourceBounds.min;
        }

        if (size.sqrMagnitude <= 0.0001f)
        {
            return false;
        }

        Vector3 center;
        if (layout.positionsRelativeToSourceCenter)
        {
            center = transform.position + seatLayoutWorldOffset;
        }
        else
        {
            center = layout.sourceBoundsCenter;
            if (center.sqrMagnitude <= 0.0001f)
            {
                center = (layout.sourceBounds.min + layout.sourceBounds.max) * 0.5f;
            }

            center += seatLayoutWorldOffset;
        }

        sourceBounds = new Bounds(center, size);
        return true;
    }

    private bool TryResolveReferenceBounds(out Bounds referenceBounds)
    {
        referenceBounds = default;
        if (seatLayoutReferenceObject == null)
        {
            return false;
        }

        Renderer[] renderers = seatLayoutReferenceObject.GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < renderers.Length; i++)
        {
            if (renderers[i] == null)
            {
                continue;
            }

            if (referenceBounds.size == Vector3.zero)
            {
                referenceBounds = renderers[i].bounds;
            }
            else
            {
                referenceBounds.Encapsulate(renderers[i].bounds.min);
                referenceBounds.Encapsulate(renderers[i].bounds.max);
            }
        }

        return referenceBounds.size.sqrMagnitude > 0.0001f;
    }

    [ContextMenu("Log Seat Layout Diagnostics")]
    private void LogSeatLayoutDiagnostics()
    {
        StringBuilder builder = new();
        builder.AppendLine($"CrowdController diagnostics for '{name}'");
        builder.AppendLine($"layoutMode: {layoutMode}");
        builder.AppendLine($"transform.position: {transform.position}");
        builder.AppendLine($"seatLayoutWorldOffset: {seatLayoutWorldOffset}");
        builder.AppendLine($"seatLayoutLateralJitter: {seatLayoutLateralJitter:F3}");
        builder.AppendLine($"activeDebugRenderMode: {ResolveActiveDebugRenderMode()}");
        builder.AppendLine($"enableBillboards: {enableBillboards}");
        builder.AppendLine($"activeBillboardDistance: {ResolveBillboardDistance():F3}");
        builder.AppendLine($"chunkSize: {chunkSize}");

        if (!CrowdSeatLayoutUtility.TryParse(seatLayoutAsset, out CrowdSeatLayoutData layout, out string parseError))
        {
            builder.AppendLine($"seatLayout parse: FAILED ({parseError})");
            Debug.Log(builder.ToString(), this);
            return;
        }

        builder.AppendLine($"seatLayout formatVersion: {layout.formatVersion}");
        builder.AppendLine($"seatLayout layoutName: {layout.layoutName}");
        builder.AppendLine($"seatLayout layoutType: {layout.layoutType}");
        builder.AppendLine($"seatLayout sourceObjectName: {layout.sourceObjectName}");
        builder.AppendLine($"seatLayout positionsRelativeToSourceCenter: {layout.positionsRelativeToSourceCenter}");
        builder.AppendLine($"seatLayout sourceBounds.min: {layout.sourceBounds.min}");
        builder.AppendLine($"seatLayout sourceBounds.max: {layout.sourceBounds.max}");
        builder.AppendLine($"seatLayout sourceBoundsCenter: {layout.sourceBoundsCenter}");
        builder.AppendLine($"seatLayout sourceBoundsSize: {layout.sourceBoundsSize}");
        builder.AppendLine($"seatLayout aisleConfig.aisleCount: {layout.aisleConfig.aisleCount}");
        builder.AppendLine($"seatLayout aisleConfig.aisleWidth: {layout.aisleConfig.aisleWidth:F3}");
        builder.AppendLine($"seatLayout aisleConfig.cornerSegments: {layout.aisleConfig.cornerSegments}");
        builder.AppendLine($"seatLayout aisleConfig.cornerAislesExcluded: {layout.aisleConfig.cornerAislesExcluded}");
        builder.AppendLine($"seatLayout aisleConfig.sectionsAreExplicit: {layout.aisleConfig.sectionsAreExplicit}");
        builder.AppendLine($"seatLayout rowSectionCount: {layout.RowSectionCount}");
        builder.AppendLine($"seatLayout seatCount: {layout.SeatCount}");

        if (TryResolveSeatLayoutSourceBounds(layout, out Bounds sourceBounds))
        {
            builder.AppendLine($"resolved sourceBounds.center: {sourceBounds.center}");
            builder.AppendLine($"resolved sourceBounds.size: {sourceBounds.size}");
        }
        else
        {
            builder.AppendLine("resolved sourceBounds: <unavailable>");
        }

        if (TryResolveReferenceBounds(out Bounds referenceBounds))
        {
            builder.AppendLine($"referenceObject: {seatLayoutReferenceObject.name}");
            builder.AppendLine($"referenceBounds.center: {referenceBounds.center}");
            builder.AppendLine($"referenceBounds.size: {referenceBounds.size}");

            if (TryResolveSeatLayoutSourceBounds(layout, out sourceBounds))
            {
                Vector3 centerDelta = sourceBounds.center - referenceBounds.center;
                Vector3 sizeDelta = sourceBounds.size - referenceBounds.size;
                builder.AppendLine($"centerDelta(source-reference): {centerDelta}");
                builder.AppendLine($"sizeDelta(source-reference): {sizeDelta}");
            }
        }
        else
        {
            builder.AppendLine("referenceBounds: <unavailable>");
        }

        if (TryResolveSeatLayoutPreviewBounds(out Bounds previewBounds))
        {
            builder.AppendLine($"resolved seatPreviewBounds.center: {previewBounds.center}");
            builder.AppendLine($"resolved seatPreviewBounds.size: {previewBounds.size}");
        }

        if (hasCrowdBounds)
        {
            builder.AppendLine($"runtime crowdBounds.center: {crowdBounds.center}");
            builder.AppendLine($"runtime crowdBounds.size: {crowdBounds.size}");
        }

        if (layout.seats != null && layout.seats.Length > 0)
        {
            int[] sampleIndices =
            {
                0,
                Mathf.Clamp(layout.seats.Length / 2, 0, layout.seats.Length - 1),
                layout.seats.Length - 1,
            };

            for (int i = 0; i < sampleIndices.Length; i++)
            {
                int sampleIndex = sampleIndices[i];
                CrowdSeatLayoutSeat seat = layout.seats[sampleIndex];
                Vector3 resolvedPosition = ResolveSeatLayoutWorldPosition(layout, seat);
                builder.AppendLine(
                    $"seat[{sampleIndex}] rawPos={seat.position} resolvedPos={resolvedPosition} forward={seat.forward} block={seat.blockIndex} floor={seat.floorIndex} row={seat.rowIndex} section={seat.sectionIndex} loopSection={seat.loopSectionIndex} kind={seat.sectionKind} side={seat.sideIndex} corner={seat.cornerIndex} seat={seat.seatIndex} localT={seat.sectionLocalT:F3} span=({seat.sectionLocalT0:F3},{seat.sectionLocalT1:F3}) rowHeight={seat.rowHeight:F3} seatSurfaceHeight={seat.seatSurfaceHeight:F3} anchorHeight={seat.anchorHeight:F3}");
            }
        }

        if (instances != null && instances.Length > 0)
        {
            int[] runtimeSampleIndices =
            {
                0,
                Mathf.Clamp(instances.Length / 2, 0, instances.Length - 1),
                instances.Length - 1,
            };

            for (int i = 0; i < runtimeSampleIndices.Length; i++)
            {
                int sampleIndex = runtimeSampleIndices[i];
                Vector3 runtimePosition = instances[sampleIndex].matrix.GetColumn(3);
                builder.AppendLine($"instance[{sampleIndex}] runtimePos={runtimePosition}");
            }
        }

        builder.AppendLine($"lastVisibleChunkCount: {lastVisibleChunkCount}");
        builder.AppendLine($"lastVisibleInstanceCount: {lastVisibleInstanceCount}");
        builder.AppendLine($"lastVisibleMeshInstanceCount: {lastVisibleMeshInstanceCount}");
        builder.AppendLine($"lastVisibleBillboardInstanceCount: {lastVisibleBillboardInstanceCount}");

        Debug.Log(builder.ToString(), this);
    }

    [ContextMenu("Log Seat Layout Section Diagnostics")]
    private void LogSeatLayoutSectionDiagnostics()
    {
        StringBuilder builder = new();
        builder.AppendLine($"CrowdController section diagnostics for '{name}'");
        builder.AppendLine($"transform.position: {transform.position}");
        builder.AppendLine($"seatLayoutWorldOffset: {seatLayoutWorldOffset}");
        builder.AppendLine($"seatLayoutLateralJitter: {seatLayoutLateralJitter:F3}");

        if (!CrowdSeatLayoutUtility.TryParse(seatLayoutAsset, out CrowdSeatLayoutData layout, out string parseError))
        {
            builder.AppendLine($"seatLayout parse: FAILED ({parseError})");
            Debug.Log(builder.ToString(), this);
            return;
        }

        builder.AppendLine($"layoutName: {layout.layoutName}");
        builder.AppendLine($"rowSectionCount: {layout.RowSectionCount}");
        builder.AppendLine($"seatCount: {layout.SeatCount}");
        builder.AppendLine($"aisleCount: {layout.aisleConfig.aisleCount}");
        builder.AppendLine($"aisleWidth: {layout.aisleConfig.aisleWidth:F3}");

        if (TryResolveReferenceBounds(out Bounds referenceBounds))
        {
            builder.AppendLine($"referenceObject: {seatLayoutReferenceObject.name}");
            builder.AppendLine($"referenceBounds.center: {referenceBounds.center}");
            builder.AppendLine($"referenceBounds.size: {referenceBounds.size}");
        }
        else
        {
            builder.AppendLine("referenceBounds: <unavailable>");
        }

        if (layout.rowSections == null || layout.rowSections.Length == 0)
        {
            builder.AppendLine("rowSections: <none>");
            Debug.Log(builder.ToString(), this);
            return;
        }

        int[] sampleIndices =
        {
            0,
            Mathf.Clamp(layout.rowSections.Length / 2, 0, layout.rowSections.Length - 1),
            layout.rowSections.Length - 1,
        };

        for (int i = 0; i < sampleIndices.Length; i++)
        {
            int sectionSampleIndex = sampleIndices[i];
            CrowdSeatLayoutRowSection rowSection = layout.rowSections[sectionSampleIndex];
            Vector3 worldStart = ResolveSeatLayoutWorldPoint(layout, rowSection.centerStart);
            Vector3 worldEnd = ResolveSeatLayoutWorldPoint(layout, rowSection.centerEnd);
            Vector3 worldMid = ResolveSeatLayoutWorldPoint(layout, rowSection.centerMid);
            float spanWidth = Vector3.Distance(worldStart, worldEnd);

            builder.AppendLine(
                $"rowSection[{sectionSampleIndex}] block={rowSection.blockIndex} row={rowSection.rowIndex} section={rowSection.sectionIndex} loopSection={rowSection.loopSectionIndex} kind={rowSection.sectionKind} side={rowSection.sideIndex} corner={rowSection.cornerIndex} seats={rowSection.seatCount} localSpan=({rowSection.localT0:F3},{rowSection.localT1:F3}) spanWidth={spanWidth:F3}");
            builder.AppendLine(
                $"rowSection[{sectionSampleIndex}] worldStart={worldStart} worldMid={worldMid} worldEnd={worldEnd}");

            int matchedSeatCount = 0;
            int firstSeatIndex = -1;
            int lastSeatIndex = -1;
            Vector3 seatMin = new(float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity);
            Vector3 seatMax = new(float.NegativeInfinity, float.NegativeInfinity, float.NegativeInfinity);
            Vector3 seatSum = Vector3.zero;

            for (int seatIndex = 0; seatIndex < layout.seats.Length; seatIndex++)
            {
                CrowdSeatLayoutSeat seat = layout.seats[seatIndex];
                if (seat.blockIndex != rowSection.blockIndex ||
                    seat.rowIndex != rowSection.rowIndex ||
                    seat.sectionIndex != rowSection.sectionIndex)
                {
                    continue;
                }

                Vector3 worldSeatPosition = ResolveSeatLayoutWorldPosition(layout, seat);
                if (firstSeatIndex < 0)
                {
                    firstSeatIndex = seatIndex;
                }

                lastSeatIndex = seatIndex;
                matchedSeatCount++;
                seatSum += worldSeatPosition;
                seatMin = Vector3.Min(seatMin, worldSeatPosition);
                seatMax = Vector3.Max(seatMax, worldSeatPosition);
            }

            if (matchedSeatCount == 0)
            {
                builder.AppendLine($"rowSection[{sectionSampleIndex}] matchedSeats=0");
                continue;
            }

            Vector3 averageSeatPosition = seatSum / matchedSeatCount;
            Vector3 averageDelta = averageSeatPosition - worldMid;
            builder.AppendLine(
                $"rowSection[{sectionSampleIndex}] matchedSeats={matchedSeatCount} firstSeatIndex={firstSeatIndex} lastSeatIndex={lastSeatIndex} avgSeatPos={averageSeatPosition} avgDeltaFromMid={averageDelta}");
            builder.AppendLine(
                $"rowSection[{sectionSampleIndex}] seatBounds.min={seatMin} seatBounds.max={seatMax}");

            CrowdSeatLayoutSeat firstSeat = layout.seats[firstSeatIndex];
            CrowdSeatLayoutSeat lastSeat = layout.seats[lastSeatIndex];
            Vector3 firstSeatWorld = ResolveSeatLayoutWorldPosition(layout, firstSeat);
            Vector3 lastSeatWorld = ResolveSeatLayoutWorldPosition(layout, lastSeat);
            builder.AppendLine(
                $"rowSection[{sectionSampleIndex}] firstSeatWorld={firstSeatWorld} lastSeatWorld={lastSeatWorld}");
        }

        Debug.Log(builder.ToString(), this);
    }
}
