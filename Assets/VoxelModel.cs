using UnityEngine;
using System.Collections.Generic;
using System.Linq;

[ExecuteInEditMode]
public class VoxelModel : MonoBehaviour {
    const float CHUNK_SIZE = 1.0f;
    const float CHUNK_HALF_SIZE = CHUNK_SIZE / 2.0f;
    readonly Vector3 CHUNK_BOUNDS_CENTRE = new Vector3(CHUNK_HALF_SIZE, CHUNK_HALF_SIZE, CHUNK_HALF_SIZE);
    readonly Vector3 CHUNK_BOUNDS_SIZE = new Vector3(CHUNK_HALF_SIZE, CHUNK_HALF_SIZE, CHUNK_HALF_SIZE);
    const int CHUNK_VOXEL_SIZE = 32;
    const int CHUNK_VOXELS = CHUNK_VOXEL_SIZE * CHUNK_VOXEL_SIZE * CHUNK_VOXEL_SIZE;
    const float VOXEL_SIZE = CHUNK_SIZE / (float)CHUNK_VOXEL_SIZE;
    const TextureFormat STORE_FORMAT = TextureFormat.ARGB32;
    const RenderTextureFormat WORK_FORMAT = RenderTextureFormat.ARGB32;
    const int THREAD_GROUP_SIZE = 8;

    public struct Address {
        private readonly int x, y, z;
        public int X { get { return x; } }
        public int Y { get { return y; } }
        public int Z { get { return z; } }

        public Address(int x, int y, int z) {
            this.x = x;
            this.y = y;
            this.z = z;
        }
        public Address(Address other) : this(other.x, other.y, other.z) { }
        public Address(Vector3 vector) : this(Mathf.FloorToInt(vector.x), Mathf.FloorToInt(vector.y), Mathf.FloorToInt(vector.z)) { }
    }

    public class Brush {
        public enum ShapeType {
            Spheroid,
            Cuboid,
            Cylinder,
        }

        public ShapeType Shape { get; set; }
        public Matrix4x4 Matrix { get; set; }

        public Brush() {
            Shape = ShapeType.Spheroid;
            Matrix = new Matrix4x4();
        }
    }

    public class Tool {
        public enum BlendMode {
            None,
            Erase,
            Replace,
            Mix,
            Add,
            Subtract,
            Multiply,
            Divide,
            Min,
            Max,
        }
        public BlendMode ColourBlend { get; set; }
        public BlendMode DensityBlend { get; set; }
        public Color Colour { get; set; }
        public Brush Brush { get; set; }

        public Tool() {
            ColourBlend = BlendMode.Mix;
            DensityBlend = BlendMode.Mix;
            Colour = new Color();
            Brush = new Brush();
        }
    }

    public class Chunk {
        private readonly Texture3D texture;
        public Texture3D Texture { get { return texture; } }
        private readonly int count;
        public int Count { get { return count; } }

        public static Texture3D CreateTexture() {
            return new Texture3D(CHUNK_VOXEL_SIZE, CHUNK_VOXEL_SIZE, CHUNK_VOXEL_SIZE, STORE_FORMAT, false);
        }

        public Chunk() {
            texture = CreateTexture();
            count = 0;
        }

        public Chunk(Chunk other) {
            //texture = Texture3D.Instantiate(other.texture);
            texture = CreateTexture();
            Graphics.CopyTexture(other.texture, texture);
            count = other.count;
        }
    }

    public enum VoxelMethod {
        Blocks,
        MarchingCubes,
        NaiveSurfaceNets,
        DualContouring,
    }

    [SerializeField]
    private float threshold = 0.5f;
    public float Threshold { get { return threshold; } set { threshold = value; } }
    [SerializeField]
    private VoxelMethod method = VoxelMethod.Blocks;
    public VoxelMethod Method { get { return method; } set { method = value; } }
    [SerializeField]
    private GameObject controllerManagerObject;
    public GameObject ControllerManagerObject { get { return controllerManagerObject; } set { controllerManagerObject = value; } }

    private Matrix4x4 localToVoxelMatrix;
    private Dictionary<Address, Chunk> chunks;
    private Dictionary<Address, GameObject> chunkObjects;
    private Mesh chunkMesh;
    private Material chunkMaterial;
    private ComputeShader brushCompute;
    private int brushComputePaint;
    private RenderTexture workBuffer;
    private ComputeBuffer brushMatrixBuffer;
    private Stack<Dictionary<Address, Chunk>> undoStack;
    private Stack<Dictionary<Address, Chunk>> redoStack;
    private Dictionary<Address, Chunk> undoStep = null;
    private bool painting = false;

    public void Start() {
        localToVoxelMatrix = Matrix4x4.Scale(new Vector3(VOXEL_SIZE, VOXEL_SIZE, VOXEL_SIZE)).inverse;

        undoStack = new Stack<Dictionary<Address, Chunk>>();
        redoStack = new Stack<Dictionary<Address, Chunk>>();
        chunks = new Dictionary<Address, Chunk>();
        chunkObjects = new Dictionary<Address, GameObject>();
        chunkMesh = new Mesh();
        List<Vector3> vertices = new List<Vector3>();
        List<Vector3> voxels = new List<Vector3>();
        for (int z = 0; z < CHUNK_VOXEL_SIZE; ++z) {
            for (int y = 0; y < CHUNK_VOXEL_SIZE; ++y) {
                for (int x = 0; x < CHUNK_VOXEL_SIZE; ++x) {
                    vertices.Add(new Vector3(x * VOXEL_SIZE, y * VOXEL_SIZE, z * VOXEL_SIZE));
                    voxels.Add(new Vector3(x, y, z));
                }
            }
        }
        chunkMesh.SetVertices(vertices);
        chunkMesh.SetUVs(0, voxels);
        chunkMesh.bounds = new Bounds(CHUNK_BOUNDS_CENTRE, CHUNK_BOUNDS_SIZE);
        chunkMesh.SetIndices(Enumerable.Range(0, chunkMesh.vertexCount).ToArray(), MeshTopology.Points, 0);

        chunkMaterial = new Material(Shader.Find("CarveVR/Voxel"));
        chunkMaterial.SetFloat("_ChunkSize", CHUNK_SIZE);
        chunkMaterial.SetInt("_ChunkVoxelSize", CHUNK_VOXEL_SIZE);
        chunkMaterial.SetFloat("_Threshold", threshold);
        chunkMaterial.SetInt("_VoxelMethod", (int)method);

        workBuffer = new RenderTexture(CHUNK_VOXEL_SIZE, CHUNK_VOXEL_SIZE, 0);
        workBuffer.volumeDepth = CHUNK_VOXEL_SIZE;
        workBuffer.dimension = UnityEngine.Rendering.TextureDimension.Tex3D;
        workBuffer.format = WORK_FORMAT;
        workBuffer.enableRandomWrite = true;
        workBuffer.Create();

        brushCompute = Resources.Load("Brush") as ComputeShader;
        brushComputePaint = brushCompute.FindKernel("Paint");
    }

    private int TouchpadButton(SteamVR_Controller.Device controller) {
        if (controller.GetPressUp(SteamVR_Controller.ButtonMask.Touchpad)) {
            Vector2 axis = controller.GetAxis(Valve.VR.EVRButtonId.k_EButton_Axis0);
            const float deadCentreThreshold = 0.1f;
            if (axis.magnitude > deadCentreThreshold) {
                if (axis.y < 0.0f) {
                    if (axis.x < 0.0f) return 1;
                    else if (axis.x > 0.0f) return 2;
                }
                else if (axis.y > 0.0f) {
                    if (axis.x < 0.0f) return 3;
                    else if (axis.x > 0.0f) return 4;
                }
            }
        }
        return 0;
    }

    private void Input() {
        SteamVR_ControllerManager controllerManager = controllerManagerObject.GetComponent<SteamVR_ControllerManager>();

        SteamVR_Controller.Device leftController = null;
        int leftIndex = (int)controllerManager.left.GetComponent<SteamVR_TrackedObject>().index;
        if (leftIndex != -1 && (leftController = SteamVR_Controller.Input(leftIndex)) != null) {
            float strength = leftController.GetAxis(Valve.VR.EVRButtonId.k_EButton_Axis1).x;
            const float paintThreshold = 0.1f;
            if (leftController.GetTouchDown(SteamVR_Controller.ButtonMask.Trigger)) {
                print("UNDO START!");
                painting = true;
                UndoStepBegin();
            }
            if (leftController.GetTouchUp(SteamVR_Controller.ButtonMask.Trigger)) {
                print("UNDO FINISH! " + undoStep.Count);
                painting = false;
                UndoStepEnd();
            }
            if (painting) {
                Vector4 pos = controllerManager.left.GetComponent<Transform>().position;
                Paint(pos, 8, new Color(1.0f, 0.0f, 0.0f, 1.0f), strength);
            }
            switch (TouchpadButton(leftController)) {
                case 1: if (CanUndo) Undo(); break;
                case 2: if (CanRedo) Redo(); break;
            }
        }

        SteamVR_Controller.Device rightController = null;
        int rightIndex = (int)controllerManager.right.GetComponent<SteamVR_TrackedObject>().index;
        if (rightIndex != -1 && (rightController = SteamVR_Controller.Input(rightIndex)) != null) {
        }
    }

    public void Update() {
        Input();
    }

    private void UndoStepBegin() {
        undoStep = new Dictionary<Address, Chunk>();
    }

    private void UndoStepEnd() {
        if (undoStep.Count > 0) {
            undoStack.Push(undoStep);
            redoStack.Clear();
        }
        undoStep = null;
    }

    private void UndoStepCopyChunk(Address address, Chunk chunk) {
        if (undoStep != null && !undoStep.ContainsKey(address)) {
            undoStep.Add(address, new Chunk(chunk));
        }
    }

    private void UndoRedo(Stack<Dictionary<Address, Chunk>> fromStack, Stack<Dictionary<Address, Chunk>> toStack) {
        Dictionary<Address, Chunk> fromStep = fromStack.Pop();
        Dictionary<Address, Chunk> toStep = new Dictionary<Address, Chunk>();
        foreach (KeyValuePair<Address, Chunk> entry in fromStep) {
            toStep.Add(entry.Key, chunks[entry.Key]);
            AddChunk(entry.Key, entry.Value);
        }
        toStack.Push(toStep);
    }

    private bool CanUndo { get { return this.undoStep == null && undoStack.Count > 0; } }
    private void Undo() { UndoRedo(undoStack, redoStack); }

    private bool CanRedo { get { return this.undoStep == null && redoStack.Count > 0; } }
    private void Redo() { UndoRedo(redoStack, undoStack); }

    private Chunk AddChunk(Address address, Chunk chunk = null) {
        if (chunk == null) chunk = new Chunk();
        GameObject gameObject = (GameObject)Instantiate(Resources.Load("Chunk"));
        gameObject.transform.SetParent(this.transform);
        gameObject.transform.position = new Vector3(address.X, address.Y, address.Z);
        MeshFilter filter = gameObject.GetComponent<MeshFilter>();
        filter.mesh = chunkMesh;
        filter.mesh.bounds = new Bounds(CHUNK_BOUNDS_CENTRE, CHUNK_BOUNDS_SIZE);
        MeshRenderer renderer = gameObject.GetComponent<MeshRenderer>();
        renderer.sharedMaterial = chunkMaterial;
        MaterialPropertyBlock propertyBlock = new MaterialPropertyBlock();
        propertyBlock.SetTexture("_Data", chunk.Texture);
        renderer.SetPropertyBlock(propertyBlock);
        chunks[address] = chunk;
        if (chunkObjects.ContainsKey(address)) Destroy(chunkObjects[address]);
        chunkObjects[address] = gameObject;
        return chunk;
    }

    private Chunk RemoveChunk(Address address) {
        Chunk chunk = chunks[address];
        chunks.Remove(address);
        Destroy(chunkObjects[address]);
        chunkObjects.Remove(address);
        return chunk;
    }

    private void Paint(Vector3 pos, float radius, Color colour, float strength) {
        radius = radius * VOXEL_SIZE * strength;
        ComputeBuffer countBuffer = new ComputeBuffer(1, sizeof(uint));
        brushCompute.SetFloat("BrushRadius", radius / VOXEL_SIZE);
        brushCompute.SetVector("BrushColour", colour);
        brushCompute.SetTexture(brushComputePaint, "Out", workBuffer);
        uint[] countBufferData = { 0 };
        countBuffer.SetData(countBufferData);
        brushCompute.SetBuffer(brushComputePaint, "Count", countBuffer);

        Address voxelAddress = new Address(localToVoxelMatrix.MultiplyPoint3x4(transform.worldToLocalMatrix.MultiplyPoint3x4(pos)));
        Address chunkAddress;
        int x0 = Util.DivDown(pos.x - radius, CHUNK_SIZE);
        int x1 = Util.DivDown(pos.x + radius, CHUNK_SIZE);
        int y0 = Util.DivDown(pos.y - radius, CHUNK_SIZE);
        int y1 = Util.DivDown(pos.y + radius, CHUNK_SIZE);
        int z0 = Util.DivDown(pos.z - radius, CHUNK_SIZE);
        int z1 = Util.DivDown(pos.z + radius, CHUNK_SIZE);
        for (int z = z0; z <= z1; ++z) {
            for (int y = y0; y <= y1; ++y) {
                for (int x = x0; x <= x1; ++x) {
                    chunkAddress = new Address(x, y, z);
                    Address chunkVoxelAddress = new Address(voxelAddress.X - chunkAddress.X * CHUNK_VOXEL_SIZE, voxelAddress.Y - chunkAddress.Y * CHUNK_VOXEL_SIZE, voxelAddress.Z - chunkAddress.Z * CHUNK_VOXEL_SIZE);
                    Chunk chunk;
                    try {
                        chunk = chunks[chunkAddress];
                    }
                    catch (KeyNotFoundException) {
                        chunk = AddChunk(chunkAddress);
                    }
                    Texture3D texture = chunk.Texture;
                    brushCompute.SetTexture(brushComputePaint, "In", texture);
                    Vector4 brushVector = new Vector4(chunkVoxelAddress.X, chunkVoxelAddress.Y, chunkVoxelAddress.Z);
                    brushCompute.SetVector("BrushVector", brushVector);
                    UndoStepCopyChunk(chunkAddress, chunk);
                    brushCompute.Dispatch(brushComputePaint, THREAD_GROUP_SIZE, THREAD_GROUP_SIZE, THREAD_GROUP_SIZE);
                    //UndoStepCopyChunk(chunkAddress, chunk);
                    Graphics.CopyTexture(workBuffer, texture);
                }
            }
        }
        countBuffer.GetData(countBufferData);
        countBuffer.Release();
    }
}
