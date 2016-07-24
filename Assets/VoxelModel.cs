using UnityEngine;
using System.Collections.Generic;
using System.Linq;

//[ExecuteInEditMode]
public class VoxelModel : MonoBehaviour {
    const float CHUNK_SIZE = 1.0f;
    const float CHUNK_HALF_SIZE = CHUNK_SIZE / 2.0f;
    readonly Vector3 CHUNK_BOUNDS_CENTRE = new Vector3(CHUNK_HALF_SIZE, CHUNK_HALF_SIZE, CHUNK_HALF_SIZE);
    readonly Vector3 CHUNK_BOUNDS_SIZE = new Vector3(CHUNK_HALF_SIZE, CHUNK_HALF_SIZE, CHUNK_HALF_SIZE);
    const int CHUNK_DEPTH = 4;
    const int CHUNK_VOXEL_SIZE = (int)(0x1u << CHUNK_DEPTH);
    const int CHUNK_VOXELS = CHUNK_VOXEL_SIZE * CHUNK_VOXEL_SIZE * CHUNK_VOXEL_SIZE;
    const float VOXEL_SIZE = CHUNK_SIZE / (float)CHUNK_VOXEL_SIZE;
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

    public enum VoxelMethod {
        Blocks,
        MarchingCubes,
        NaiveSurfaceNets,
        DualContouring,
    }

    [SerializeField]
    private float isolevel = 0.5f;
    public float Isolevel { get { return isolevel; } set { isolevel = value; } }
    [SerializeField]
    private VoxelMethod method = VoxelMethod.Blocks;
    public VoxelMethod Method { get { return method; } set { method = value; } }
    [SerializeField]
    private GameObject controllerManagerObject;
    public GameObject ControllerManagerObject { get { return controllerManagerObject; } set { controllerManagerObject = value; } }
    [SerializeField]
    private GameObject chunkManagerObject;
    public GameObject ChunkManagerObject { get { return chunkManagerObject; } set { chunkManagerObject = value; } }

    private ChunkManager chunkManager;

    private Matrix4x4 localToVoxelMatrix;
    private Dictionary<Address, Chunk> chunks;
    private Dictionary<Address, GameObject> chunkObjects;
    private Mesh chunkMesh;
    private Material chunkMaterial;
    private ComputeShader brushCompute;
    private int brushComputePaint;
    private RenderTexture workBuffer;
    static private Texture3D emptyTexture;
    private ComputeBuffer countBuffer;
    private ComputeBuffer brushMatrixBuffer;
    private ComputeBuffer testingBuffer;
    //private ComputeBuffer test;
    private Stack<Dictionary<Address, Chunk>> undoStack;
    private Stack<Dictionary<Address, Chunk>> redoStack;
    private Dictionary<Address, Chunk> undoStep = null;
    private bool painting = false;
    private Vector3 lastPaintPos;
    private float lastPaintStrength;
    private List<Color> palette;
    private int colour = 0;
    static readonly private int[,] paletteData = new int[11, 3]{
        { 255,   0,   0 },
        { 255, 255,   0 },
        {   0, 255,   0 },
        {   0, 255, 255 },
        {   0,   0, 255 },
        { 255,   0, 255 },
        {   0,   0,   0 },
        {  63,  63,  63 },
        { 127, 127, 127 },
        { 191, 191, 191 },
        { 255, 255, 255 },
    };

    public void Start() {
        chunkManager = new ChunkManager(CHUNK_VOXEL_SIZE);

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

        palette = new List<Color>();
        for (int index = 0; index < paletteData.GetLength(0); ++index) {
            palette.Add(new Color((float)paletteData[index, 0] / 255.0f, (float)paletteData[index, 1] / 255.0f, (float)paletteData[index, 2] / 255.0f));
        }

        chunkMaterial = new Material(Shader.Find("CarveVR/Chunk"));
        chunkMaterial.SetFloat("_ChunkSize", CHUNK_SIZE);
        chunkMaterial.SetInt("_ChunkVoxelSize", CHUNK_VOXEL_SIZE);
        chunkMaterial.SetInt("_VoxelMethod", (int)method);
        chunkMaterial.SetBuffer("edgeTable", ChunkManager.marchingCubesEdgeTable);
        chunkMaterial.SetBuffer("triTable", ChunkManager.marchingCubesTriTable);

        countBuffer = new ComputeBuffer(1, sizeof(uint));

        workBuffer = ChunkManager.CreateRenderTexture(CHUNK_VOXEL_SIZE);

        emptyTexture = ChunkManager.CreateEmptyTexture(CHUNK_VOXEL_SIZE);

        brushCompute = Resources.Load("Brush") as ComputeShader;
        brushComputePaint = brushCompute.FindKernel("Paint");
    }

    public void OnDestroy() {
        chunkManager.Dispose();
        countBuffer.Release();
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
            switch (TouchpadButton(leftController)) {
                case 1: if (CanUndo) Undo(); break;
                case 2: if (CanRedo) Redo(); break;
                case 3: if (--colour < 0) colour += palette.Count; break;
                case 4: if (++colour >= palette.Count) colour -= palette.Count; break;
            }

            const float paintThreshold = 0.05f;
            float strength = leftController.GetAxis(Valve.VR.EVRButtonId.k_EButton_Axis1).x;
            float interpolatedStrength = Mathf.Lerp(0.0f, 1.0f, strength - paintThreshold);
            bool firstPaint = false;
            if (!painting && strength >= paintThreshold) {
                painting = true;
                UndoStepBegin();
                firstPaint = true;
            }
            if (painting && strength < paintThreshold) {
                painting = false;
                UndoStepEnd();
            }
            const float radius = 0.25f;
            const float spacing = radius * 0.5f;
            float strengthSpacing = interpolatedStrength * 0.5f;
            if (painting) {
                Vector3 pos = controllerManager.left.GetComponent<Transform>().position;
                if (firstPaint || Vector3.Distance(pos, lastPaintPos) >= spacing || Mathf.Abs(interpolatedStrength - lastPaintStrength) >= strengthSpacing) {
                    Paint(pos, radius, palette[colour], interpolatedStrength);
                    lastPaintPos = pos;
                    lastPaintStrength = interpolatedStrength;
                }
            }

            if (leftController.GetPressUp(SteamVR_Controller.ButtonMask.ApplicationMenu)) {
                System.Text.StringBuilder stringBuilder = new System.Text.StringBuilder(256);
                SteamVR.instance.overlay.GetKeyboardText(stringBuilder, 256);
                print(stringBuilder.ToString());
            }
        }

        SteamVR_Controller.Device rightController = null;
        int rightIndex = (int)controllerManager.right.GetComponent<SteamVR_TrackedObject>().index;
        if (rightIndex != -1 && (rightController = SteamVR_Controller.Input(rightIndex)) != null) {
            switch (TouchpadButton(rightController)) {
                case 1: break;
                case 2: break;
                case 3: if ((isolevel -= 0.1f) < 0.0f) isolevel += 1.0f; break;
                case 4: if ((isolevel += 0.1f) >= 1.0f) isolevel -= 1.0f; break;
            }

        }
    }

    public void Update() {
        Input();
        chunkManager.material.SetFloat("_Isolevel", isolevel);
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

    private void UndoStepAddChunk(Address address, Chunk chunk) {
        if (undoStep != null && !undoStep.ContainsKey(address)) {
            undoStep.Add(address, chunk);
        }
    }

    private void UndoRedo(Stack<Dictionary<Address, Chunk>> fromStack, Stack<Dictionary<Address, Chunk>> toStack) {
        Dictionary<Address, Chunk> fromStep = fromStack.Pop();
        Dictionary<Address, Chunk> toStep = new Dictionary<Address, Chunk>();
        foreach (KeyValuePair<Address, Chunk> entry in fromStep) {
            toStep.Add(entry.Key, chunks.ContainsKey(entry.Key) ? chunks[entry.Key] : null);
            SetChunk(entry.Key, entry.Value);
        }
        toStack.Push(toStep);
    }

    private bool CanUndo { get { return this.undoStep == null && undoStack.Count > 0; } }
    private void Undo() { UndoRedo(undoStack, redoStack); }

    private bool CanRedo { get { return this.undoStep == null && redoStack.Count > 0; } }
    private void Redo() { UndoRedo(redoStack, undoStack); }

    private void SetChunk(Address address, Chunk chunk) {
        if (chunks.ContainsKey(address)) {
            chunks.Remove(address);
        }
        if (chunkObjects.ContainsKey(address)) {
            Destroy(chunkObjects[address]);
            chunkObjects.Remove(address);
        }
        if (chunk != null) {
            GameObject chunkObject = chunkManager.CreateGameObject(chunk, gameObject, new Vector3(address.X, address.Y, address.Z));
            chunkObjects[address] = chunkObject;
            chunks[address] = chunk;
        }
    }

    private void Paint(Vector3 pos, float radius, Color colour, float strength) {
        radius = radius * strength;
        brushCompute.SetFloat("BrushRadius", radius / VOXEL_SIZE);
        brushCompute.SetVector("BrushColour", colour);
        brushCompute.SetTexture(brushComputePaint, "Out", workBuffer);
        uint[] countBufferData = { 0 };
        countBuffer.SetData(countBufferData);
        brushCompute.SetBuffer(brushComputePaint, "Count", countBuffer);

        Vector3 voxelPos = localToVoxelMatrix.MultiplyPoint3x4(transform.worldToLocalMatrix.MultiplyPoint3x4(pos));
        int x0 = Util.DivDown(pos.x - radius, CHUNK_SIZE);
        int x1 = Util.DivDown(pos.x + radius, CHUNK_SIZE);
        int y0 = Util.DivDown(pos.y - radius, CHUNK_SIZE);
        int y1 = Util.DivDown(pos.y + radius, CHUNK_SIZE);
        int z0 = Util.DivDown(pos.z - radius, CHUNK_SIZE);
        int z1 = Util.DivDown(pos.z + radius, CHUNK_SIZE);
        for (int z = z0; z <= z1; ++z) {
            for (int y = y0; y <= y1; ++y) {
                for (int x = x0; x <= x1; ++x) {
                    Address chunkAddress = new Address(x, y, z);
                    Vector3 chunkVoxelPos = voxelPos - (new Vector3(x, y, z) * CHUNK_VOXEL_SIZE);
                    Chunk oldChunk = null;
                    Texture3D oldTexture = emptyTexture;
                    if (chunks.ContainsKey(chunkAddress)) {
                        oldChunk = chunks[chunkAddress];
                        oldTexture = oldChunk.texture;
                    }
                    UndoStepAddChunk(chunkAddress, oldChunk);
                    Chunk newChunk;
                    if (oldChunk != null) newChunk = new Chunk(oldChunk);
                    else newChunk = new Chunk(CHUNK_VOXEL_SIZE);
                    brushCompute.SetTexture(brushComputePaint, "In", oldTexture);
                    brushCompute.SetVector("BrushVector", chunkVoxelPos);
                    brushCompute.Dispatch(brushComputePaint, THREAD_GROUP_SIZE, THREAD_GROUP_SIZE, THREAD_GROUP_SIZE);
                    Graphics.CopyTexture(workBuffer, newChunk.texture);
                    SetChunk(chunkAddress, newChunk);
                }
            }
        }
        countBuffer.GetData(countBufferData);
    }
}
