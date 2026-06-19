using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

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

    [Header("Source")]
    [SerializeField] private GameObject crowdSource;
    [SerializeField] private AnimationClip idleClip;
    [SerializeField] private AnimationClip sitClip;
    [SerializeField] private AnimationClip standClip;
    [SerializeField] private Texture2D atlasMap;
    [SerializeField] private Texture2D billboardColor01;
    [SerializeField] private Texture2D billboardColor02;
    [SerializeField] private Texture2D outfitDataMap;
    [SerializeField] private Material materialTemplate;
    [SerializeField] private Material billboardMaterialTemplate;
    [SerializeField] private Material webGLMeshFallbackMaterialTemplate;
    [SerializeField] private bool hideSourceCharacter = true;
    [SerializeField] private Vector3 modelRotationEuler = new(-90f, 0f, 0f);

    [Header("Crowd Layout")]
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
    [SerializeField] private float billboardDistance = 48f;
    [SerializeField] private bool useWebGLLodDistanceOverrides = true;
    [SerializeField] private float webGLLod1Distance = 4f;
    [SerializeField] private float webGLLod2Distance = 7f;
    [SerializeField] private float webGLBillboardDistance = 10f;
    [SerializeField] private float billboardScale = 1f;
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
    [SerializeField] private DebugRenderMode debugRenderMode = DebugRenderMode.Normal;
    [SerializeField] private bool useWebGLBillboardFallback = true;
    [SerializeField] private bool useWebGLNonInstancedMeshFallback = true;
    [SerializeField] private bool useWebGLUnskinnedMeshFallback = true;
    [SerializeField] private bool useWebGLSolidMeshFallback = true;

    private readonly Plane[] frustumPlanes = new Plane[6];
    private readonly List<Chunk> chunks = new();
    private MaterialPropertyBlock materialPropertyBlock;
    private Material runtimeMaterial;
    private Material[] billboardMaterials;
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
    public bool IsWebGLNonInstancedMeshFallbackActive =>
        Application.platform == RuntimePlatform.WebGLPlayer && useWebGLNonInstancedMeshFallback;
    public bool IsWebGLUnskinnedMeshFallbackActive =>
        Application.platform == RuntimePlatform.WebGLPlayer && useWebGLUnskinnedMeshFallback;
    public bool IsWebGLSolidMeshFallbackActive =>
        Application.platform == RuntimePlatform.WebGLPlayer && useWebGLSolidMeshFallback;
    public bool UsesDedicatedWebGLMeshFallbackMaterial =>
        webGLMeshFallbackMaterialTemplate != null && webGLMeshFallbackMaterialTemplate.shader != null;
    public bool HasBillboardMesh => billboardMesh != null;
    public int BillboardMaterialCount => billboardMaterials?.Length ?? 0;
    public bool UsesDedicatedBillboardShader => UsesDedicatedBillboardMaterial();
    public string BillboardShaderName =>
        billboardMaterials != null &&
        billboardMaterials.Length > 0 &&
        billboardMaterials[0] != null &&
        billboardMaterials[0].shader != null
            ? billboardMaterials[0].shader.name
            : "<none>";
    public float ActiveLod1Distance => ResolveLod1Distance();
    public float ActiveLod2Distance => ResolveLod2Distance();
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
        billboardDistance = Mathf.Max(lod2Distance, billboardDistance);
        webGLLod1Distance = Mathf.Max(0.1f, webGLLod1Distance);
        webGLLod2Distance = Mathf.Max(webGLLod1Distance, webGLLod2Distance);
        webGLBillboardDistance = Mathf.Max(webGLLod2Distance, webGLBillboardDistance);
        billboardScale = Mathf.Max(0.01f, billboardScale);
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

        billboardMaterials = BuildBillboardMaterials();

        AllocateBatchBuffers();
        materialPropertyBlock = new MaterialPropertyBlock();
        BuildInstances();

        if (Application.platform == RuntimePlatform.WebGLPlayer)
        {
            Debug.Log(
                $"CrowdController WebGL mode: {ResolveActiveDebugRenderMode()}, " +
                $"instancing={SystemInfo.supportsInstancing}, " +
                $"billboardsReady={billboardMesh != null && billboardMaterials != null && billboardMaterials.Length > 0}, " +
                $"dedicatedBillboardShader={UsesDedicatedBillboardMaterial()}, " +
                $"billboardShader={BillboardShaderName}");
        }

        if (hideSourceCharacter && resolvedSourceRoot != null && resolvedSourceRoot.scene.IsValid())
        {
            resolvedSourceRoot.SetActive(false);
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
        instances = new InstanceState[instanceCount];
        chunks.Clear();

        int instancesPerRow = ResolveInstancesPerRow();
        int rowCount = ResolveRowCount(instancesPerRow);
        float layoutWidth = ResolveLayoutWidth(instancesPerRow);
        computedCrowdDepth = ResolveCrowdDepth(rowCount);

        int chunkCountX = Mathf.Max(1, Mathf.CeilToInt(layoutWidth / chunkSize.x));
        int chunkCountZ = Mathf.Max(1, Mathf.CeilToInt(computedCrowdDepth / chunkSize.y));
        for (int z = 0; z < chunkCountZ; z++)
        {
            for (int x = 0; x < chunkCountX; x++)
            {
                chunks.Add(new Chunk());
            }
        }

        float leftEdge = transform.position.x - (layoutWidth * 0.5f);
        float frontEdge = transform.position.z;

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

            Vector3 position = transform.position + new Vector3(
                xOffset + jitterX,
                row * rowSpacingY + jitterY,
                row * rowSpacingZ);

            PlaybackState initialState = RandomValue() > 0.5f ? PlaybackState.StandingHold : PlaybackState.SeatedHold;
            float holdDuration = initialState == PlaybackState.StandingHold
                ? RandomRange(standingHoldRange.x, standingHoldRange.y)
                : RandomRange(seatedHoldRange.x, seatedHoldRange.y);

            instances[i] = new InstanceState
            {
                matrix = Matrix4x4.TRS(position, facingRotation, Vector3.one * characterScale),
                playbackState = initialState,
                clipTime = initialState == PlaybackState.SeatedHold ? 1f : 0f,
                holdTimer = holdDuration,
                outfitIndex = outfits.Length > 0 ? randomGenerator.Next(0, outfits.Length) : 0,
            };

            int chunkX = Mathf.Clamp(Mathf.FloorToInt((position.x - leftEdge) / chunkSize.x), 0, chunkCountX - 1);
            int chunkZ = Mathf.Clamp(Mathf.FloorToInt((position.z - frontEdge) / chunkSize.y), 0, chunkCountZ - 1);
            Chunk chunk = chunks[(chunkZ * chunkCountX) + chunkX];
            chunk.instanceIndices.Add(i);

            Bounds instanceBounds = new Bounds(
                position + new Vector3(0f, characterHeight * 0.5f, 0f),
                new Vector3(rowSpacingX, characterHeight, 1f));

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
        bool useBillboard;
        Mesh drawMesh = SelectLodMesh(chunk, out useBillboard);
        if (drawMesh == null)
        {
            return;
        }

        if (useBillboard)
        {
            DrawBillboardChunk(chunk, drawMesh);
            return;
        }

        if (UseNonInstancedWebGLMeshFallback())
        {
            DrawMeshChunkNonInstanced(chunk, drawMesh);
            return;
        }

        DrawMeshChunk(chunk, drawMesh);
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

            DrawSingleMeshInstance(drawMesh, runtimeMaterial, state.matrix, outfit, animData);
        }
    }

    private void DrawBillboardChunk(Chunk chunk, Mesh drawMesh)
    {
        Vector3 cameraPosition = Camera.main != null ? Camera.main.transform.position : Vector3.zero;
        bool useDedicatedBillboardMaterial = UsesDedicatedBillboardMaterial();
        for (int variantIndex = 0; variantIndex < billboardMaterials.Length; variantIndex++)
        {
            Material variantMaterial = ResolveBillboardMaterial(variantIndex);
            if (variantMaterial == null)
            {
                continue;
            }

            int countInBatch = 0;
            for (int i = 0; i < chunk.instanceIndices.Count; i++)
            {
                int instanceIndex = chunk.instanceIndices[i];
                InstanceState state = instances[instanceIndex];
                if (GetBillboardVariantIndex(state.outfitIndex) != variantIndex)
                {
                    continue;
                }

                matrixBatch[countInBatch] = CreateBillboardMatrix(state, cameraPosition);
                if (!useDedicatedBillboardMaterial)
                {
                    OutfitPreset outfit = outfits[Mathf.Clamp(state.outfitIndex, 0, outfits.Length - 1)];
                    RuntimeClip runtimeClip = GetRuntimeClip(state.playbackState);
                    colorRBatch[countInBatch] = outfit.colorR;
                    colorGBatch[countInBatch] = outfit.colorG;
                    colorBBatch[countInBatch] = outfit.colorB;
                    colorABatch[countInBatch] = outfit.colorA;
                    animDataBatch[countInBatch] = new Vector4(
                        (float)runtimeClip,
                        state.clipTime,
                        ResolveRenderModeFlag(true),
                        ResolveDebugModeFlag());
                }

                frameVisibleInstanceCount++;
                frameVisibleBillboardInstanceCount++;
                countInBatch++;

                if (countInBatch == maxInstancesPerBatch)
                {
                    FlushBillboardBatch(drawMesh, variantMaterial, countInBatch, useDedicatedBillboardMaterial);
                    countInBatch = 0;
                }
            }

            if (countInBatch > 0)
            {
                FlushBillboardBatch(drawMesh, variantMaterial, countInBatch, useDedicatedBillboardMaterial);
            }
        }
    }

    private Mesh SelectLodMesh(Chunk chunk, out bool useBillboard)
    {
        useBillboard = false;
        float activeLod1Distance = ResolveLod1Distance();
        float activeLod2Distance = ResolveLod2Distance();
        float activeBillboardDistance = ResolveBillboardDistance();

        if (ShouldForceBillboards())
        {
            if (billboardMesh != null && billboardMaterials != null && billboardMaterials.Length > 0)
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
            return lodMeshes[0];
        }

        float distance = Vector3.Distance(Camera.main.transform.position, chunk.bounds.center);
        if (billboardMesh != null && billboardMaterials != null && billboardMaterials.Length > 0 && distance >= activeBillboardDistance)
        {
            useBillboard = true;
            return billboardMesh;
        }

        if (lodMeshes.Length >= 3 && distance >= activeLod2Distance)
        {
            return lodMeshes[2];
        }

        if (lodMeshes.Length >= 2 && distance >= activeLod1Distance)
        {
            return lodMeshes[1];
        }

        return lodMeshes[0];
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
            PlaybackState.StandingHold => 0f,
            PlaybackState.SittingDown => -Mathf.SmoothStep(0f, characterHeight * 0.3f, state.clipTime),
            PlaybackState.SeatedHold => -characterHeight * 0.3f,
            PlaybackState.StandingUp => -Mathf.SmoothStep(characterHeight * 0.3f, 0f, state.clipTime),
            _ => 0f,
        };
    }

    private bool UsesDedicatedBillboardMaterial()
    {
        return billboardMaterialTemplate != null && billboardMaterialTemplate.shader != null;
    }

    private Material[] BuildBillboardMaterials()
    {
        List<Material> materials = new();
        Texture2D[] variants = { billboardColor01, billboardColor02 };
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

    private int GetBillboardVariantIndex(int outfitIndex)
    {
        if (billboardMaterials == null || billboardMaterials.Length == 0)
        {
            return -1;
        }

        int presetIndex = Mathf.Clamp(outfitIndex, 0, outfits.Length - 1);
        int variantIndex = outfits[presetIndex].billboardVariant;
        return Mathf.Clamp(variantIndex, 0, billboardMaterials.Length - 1);
    }

    private Material ResolveBillboardMaterial(int variantIndex)
    {
        if (billboardMaterials == null || variantIndex < 0 || variantIndex >= billboardMaterials.Length)
        {
            return null;
        }

        return billboardMaterials[variantIndex];
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

        bool webGlFallbackAvailable =
            useWebGLBillboardFallback &&
            Application.platform == RuntimePlatform.WebGLPlayer &&
            billboardMesh != null &&
            billboardMaterials != null &&
            billboardMaterials.Length > 0;

        return webGlFallbackAvailable ? DebugRenderMode.BillboardsOnly : DebugRenderMode.Normal;
    }

    private bool ShouldForceBillboards()
    {
        return ResolveActiveDebugRenderMode() == DebugRenderMode.BillboardsOnly;
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

        Graphics.DrawMeshInstanced(
            drawMesh,
            0,
            material,
            matrixBatch,
            count,
            null,
            ShadowCastingMode.Off,
            false,
            gameObject.layer);
    }

    private void DrawSingleMeshInstance(Mesh drawMesh, Material material, Matrix4x4 matrix, OutfitPreset outfit, Vector4 animData)
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

        Vector4 effectiveAnimData = animData;
        if (Application.platform == RuntimePlatform.WebGLPlayer && useWebGLUnskinnedMeshFallback)
        {
            effectiveAnimData.w = useWebGLSolidMeshFallback
                ? (float)DebugRenderMode.UnskinnedSolid
                : (float)DebugRenderMode.UnskinnedLit;
        }

        bool useDedicatedWebGLMeshFallbackMaterial =
            Application.platform == RuntimePlatform.WebGLPlayer &&
            useWebGLUnskinnedMeshFallback &&
            UsesDedicatedWebGLMeshFallbackMaterial;

        Material drawMaterial = useDedicatedWebGLMeshFallbackMaterial
            ? webGLMeshFallbackMaterialTemplate
            : material;

        materialPropertyBlock.Clear();
        if (useDedicatedWebGLMeshFallbackMaterial)
        {
            materialPropertyBlock.SetTexture(BaseMapId, atlasMap);
            materialPropertyBlock.SetTexture(OutfitDataMapId, outfitDataMap);
            materialPropertyBlock.SetVector(ColorRId, outfit.colorR);
            materialPropertyBlock.SetVector(ColorGId, outfit.colorG);
            materialPropertyBlock.SetVector(ColorBId, outfit.colorB);
            materialPropertyBlock.SetVector(ColorAId, outfit.colorA);
        }
        else
        {
            materialPropertyBlock.SetVector(ColorRId, outfit.colorR);
            materialPropertyBlock.SetVector(ColorGId, outfit.colorG);
            materialPropertyBlock.SetVector(ColorBId, outfit.colorB);
            materialPropertyBlock.SetVector(ColorAId, outfit.colorA);
            materialPropertyBlock.SetVector(AnimDataId, effectiveAnimData);
            materialPropertyBlock.SetVector(FallbackColorRId, outfit.colorR);
            materialPropertyBlock.SetVector(FallbackColorGId, outfit.colorG);
            materialPropertyBlock.SetVector(FallbackColorBId, outfit.colorB);
            materialPropertyBlock.SetVector(FallbackColorAId, outfit.colorA);
            materialPropertyBlock.SetVector(FallbackAnimDataId, effectiveAnimData);
        }

        Graphics.DrawMesh(
            drawMesh,
            matrix,
            drawMaterial,
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

        if (billboardMaterials != null)
        {
            for (int i = 0; i < billboardMaterials.Length; i++)
            {
                if (billboardMaterials[i] == null)
                {
                    continue;
                }

                if (Application.isPlaying)
                {
                    Destroy(billboardMaterials[i]);
                }
                else
                {
                    DestroyImmediate(billboardMaterials[i]);
                }
            }

            billboardMaterials = null;
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
        chunks.Clear();
    }

    private void OnDrawGizmosSelected()
    {
        if (!drawChunkGizmos)
        {
            return;
        }

        int instancesPerRow = ResolveInstancesPerRow();
        int rowCount = ResolveRowCount(instancesPerRow);
        float width = ResolveLayoutWidth(instancesPerRow);
        float depth = ResolveCrowdDepth(rowCount);

        Gizmos.color = new Color(0.9f, 0.75f, 0.2f, 0.7f);
        Gizmos.DrawWireCube(
            transform.position + new Vector3(0f, ((rowCount - 1) * rowSpacingY + characterHeight) * 0.5f, depth * 0.5f),
            new Vector3(width, (rowCount - 1) * rowSpacingY + characterHeight, depth));

        if (chunks.Count == 0)
        {
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
}
