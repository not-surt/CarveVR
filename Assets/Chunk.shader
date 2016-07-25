// Upgrade NOTE: replaced '_Object2World' with 'unity_ObjectToWorld'

// Upgrade NOTE: replaced '_World2Object' with 'unity_WorldToObject'

Shader "CarveVR/Chunk" {
	Properties{
		_ChunkSize("Chunk Size", Float) = 1.0
		_ChunkVoxelSize("Chunk Voxel Size", Int) = 16
		_Isolevel("Isosurface level", Range(0.0, 1.0)) = 0.5
		[KeywordEnum(Billboards, Blocks, Marching Cubes, Naive Surface Nets, Dual Contouring)] _VoxelMethod("Voxel Method", Int) = 1
		_Data("Voxel Data", 3D) = "white" {}
		[Toggle]_Smooth("Smooth Normals", Float) = 1.0
		_BillboardScale("Billboard Scale", Float) = 1.0
	}
	SubShader {
		Pass {
			Tags{ "LightMode" = "ForwardBase" }

			CGPROGRAM
			#pragma target 5.0
			#include "UnityCG.cginc"
			#include "UnityLightingCommon.cginc"

			#pragma vertex vertexShader
			#pragma geometry geometryShader
			#pragma fragment fragmentShader

			float4x4 _Camera2World;

			const float _ChunkSize;
			const int _ChunkVoxelSize;
			const float _Isolevel;
			const float _Smooth;
			const float _BillboardScale;
			Texture3D _Data;
			Texture3D _Neighborhood[3][3][3];
			bool _NeighborPresent[3][3][3];
			float4 voxelNeighborhood[3][3][3];
			float4x4 voxelMatrix;
			StructuredBuffer<int> edgeTable;
			StructuredBuffer<int> triTable;

			#define METHOD_BILLBOARDS 0
			#define METHOD_BLOCKS 1
			#define METHOD_MARCHING_CUBES 2
			#define METHOD_NAIVE_SURFACE_NETS 3
			#define METHOD_DUAL_CONTOURING 4
			const int _VoxelMethod;

			struct V2G {
				float4 pos : POSITION;
				nointerpolation int3 voxel : TEXCOORD0;
				fixed4 colour : COLOR;
			};

			struct G2F {
				float4 pos : POSITION;
				fixed4 colour : COLOR0;
				float3 normal : TEXCOORD0;
				fixed4 diffuse : COLOR1;
			};

			V2G vertexShader(float4 vertex : POSITION, float3 voxel : TEXCOORD0) {
				V2G OUT;
				OUT.pos = vertex;
				OUT.voxel = voxel;
				OUT.colour = _Data.Load(int4(voxel, 0));
				return OUT;
			}

			float isolevelPosition(const float a, const float b, const float isolevel) {
				return (isolevel - a) / (b - a);
			}

			void billboardsVoxel(const V2G IN, const float size, inout TriangleStream<G2F> stream) {
				if (IN.colour.a >= _Isolevel) {
					const int2 corners[4] = {
						{ 1, -1 },
						{ 1,  1 },
						{ -1, -1 },
						{ -1,  1 },
					};
					const float3 worldPos = mul(unity_ObjectToWorld, IN.pos);
					const float3 cameraUp = normalize(mul((float3x3)_Camera2World, float3(0.0f, 1.0f, 0.0f)));
					const float3 look = normalize(_WorldSpaceCameraPos - worldPos);
					const float3 up = normalize(cameraUp - (look * dot(cameraUp, look)));
					const float3 right = cross(up, look);

					const float halfSize = 0.5f * size * _BillboardScale;
					const float3 offset = float3(0.5f, 0.5f, 0.5f) * size;

					const float4x4 projectionMatrix = mul(UNITY_MATRIX_MVP, unity_WorldToObject);
					G2F OUT;
					OUT.colour = IN.colour;
					OUT.diffuse = float4(1.0f, 1.0f, 1.0f, 1.0f);
					OUT.normal = float3(0.0f, 0.0f, -1.0f);
					[unroll]
					for (int corner = 0; corner < 4; ++corner) {
						OUT.pos = mul(projectionMatrix, float4(worldPos + offset + (halfSize * right * corners[corner].x) + (halfSize * up * corners[corner].y), 1.0f));
						stream.Append(OUT);
					}
					stream.RestartStrip();
				}
			}

			void blocksVoxel(const V2G IN, const float size, inout TriangleStream<G2F> stream) {
				const int3 corners[8] = {
					{ 0, 0, 0 },
					{ 0, 0, 1 },
					{ 0, 1, 0 },
					{ 0, 1, 1 },
					{ 1, 0, 0 },
					{ 1, 0, 1 },
					{ 1, 1, 0 },
					{ 1, 1, 1 },
				};
				const int sides[6][4] = {
					{ 0, 2, 4, 6 },
					{ 7, 3, 5, 1 },
					{ 0, 4, 1, 5 },
					{ 2, 3, 6, 7 },
					{ 0, 1, 2, 3 },
					{ 4, 6, 5, 7 },
				};
				const int3 boundaries[6] = {
					{  0,  0, -1 },
					{  0,  0,  1 },
					{  0, -1,  0 },
					{  0,  1,  0 },
					{ -1,  0,  0 },
					{  1,  0,  0 },
				};
				G2F OUT;
				OUT.colour = float4(IN.colour.rgb, 1.0f);
				for (int boundary = 0; boundary < 6; ++boundary) {
					//float3 t = corners[sides[boundary][0]] - mul(unity_WorldToObject, _WorldSpaceCameraPos);
					//const bool cull = !(dot(t, OUT.normal) <= 0);
					//const bool cull = dot(normalize(ObjSpaceViewDir(IN.pos)), OUT.normal) < 0.0f;
					const bool cull = false;
					const float position = isolevelPosition(_Data.Load(int4(IN.voxel, 0)).a, _Data.Load(int4(IN.voxel + boundaries[boundary], 0)).a, _Isolevel);
					if (!cull && position >= 0.0 && position <= 1.0) {
						OUT.normal = float3(boundaries[boundary]);
						half3 worldNormal = UnityObjectToWorldNormal(OUT.normal);
						half nl = max(0, dot(worldNormal, _WorldSpaceLightPos0.xyz));
						OUT.diffuse = nl * _LightColor0;
						OUT.diffuse.rgb += ShadeSH9(half4(worldNormal, 1));
						[unroll]
						for (int corner = 0; corner < 4; ++corner) {
							OUT.pos = mul(UNITY_MATRIX_MVP, IN.pos + float4(corners[sides[boundary][corner]], 0.0f) * size);
							stream.Append(OUT);
						}
						stream.RestartStrip();
					}
				}
			}

			float3 VertexInterp(float isolevel, float3 p1, float3 p2, float valp1, float valp2) {
				float mu;
				float3 p;

				if (abs(isolevel - valp1) < 0.00001f) return(p1);
				if (abs(isolevel - valp2) < 0.00001f) return(p2);
				if (abs(valp1 - valp2) < 0.00001f) return(p1);
				mu = (isolevel - valp1) / (valp2 - valp1);
				p.x = p1.x + mu * (p2.x - p1.x);
				p.y = p1.y + mu * (p2.y - p1.y);
				p.z = p1.z + mu * (p2.z - p1.z);

				return(p);
			}

			void Polygonise(float3 p[8], double val[8], double isolevel, inout TriangleStream<G2F> stream) {
				//Determine the index into the edge table which tells us which vertices are inside of the surface
				int cubeindex = 0;
				if (val[0] < isolevel) cubeindex |= 1;
				if (val[1] < isolevel) cubeindex |= 2;
				if (val[2] < isolevel) cubeindex |= 4;
				if (val[3] < isolevel) cubeindex |= 8;
				if (val[4] < isolevel) cubeindex |= 16;
				if (val[5] < isolevel) cubeindex |= 32;
				if (val[6] < isolevel) cubeindex |= 64;
				if (val[7] < isolevel) cubeindex |= 128;

				// Cube is entirely in/out of the surface
				if (edgeTable[cubeindex] == 0) return;

				// Find the vertices where the surface intersects the cube
				float3 vertlist[12];
				if (edgeTable[cubeindex] & 1) vertlist[0] = VertexInterp(isolevel, p[0], p[1], val[0], val[1]);
				if (edgeTable[cubeindex] & 2) vertlist[1] = VertexInterp(isolevel, p[1], p[2], val[1], val[2]);
				if (edgeTable[cubeindex] & 4) vertlist[2] = VertexInterp(isolevel, p[2], p[3], val[2], val[3]);
				if (edgeTable[cubeindex] & 8) vertlist[3] = VertexInterp(isolevel, p[3], p[0], val[3], val[0]);
				if (edgeTable[cubeindex] & 16) vertlist[4] = VertexInterp(isolevel, p[4], p[5], val[4], val[5]);
				if (edgeTable[cubeindex] & 32) vertlist[5] = VertexInterp(isolevel, p[5], p[6], val[5], val[6]);
				if (edgeTable[cubeindex] & 64) vertlist[6] = VertexInterp(isolevel, p[6], p[7], val[6], val[7]);
				if (edgeTable[cubeindex] & 128) vertlist[7] = VertexInterp(isolevel, p[7], p[4], val[7], val[4]);
				if (edgeTable[cubeindex] & 256) vertlist[8] = VertexInterp(isolevel, p[0], p[4], val[0], val[4]);
				if (edgeTable[cubeindex] & 512) vertlist[9] = VertexInterp(isolevel, p[1], p[5], val[1], val[5]);
				if (edgeTable[cubeindex] & 1024) vertlist[10] = VertexInterp(isolevel, p[2], p[6], val[2], val[6]);
				if (edgeTable[cubeindex] & 2048) vertlist[11] = VertexInterp(isolevel, p[3], p[7], val[3], val[7]);

				G2F OUT;
				//OUT.colour = float4(IN.colour.rgb, 1.0);
				OUT.normal = float3(1.0f, 0.0f, 0.0f);
				OUT.colour = float4(1.0f, 0.0f, 0.0f, 1.0f);
				OUT.diffuse = fixed4(1.0f, 1.0f, 1.0f, 1.0f);
				//triTable[cubeindex][i]
				for (int i = 0; triTable[cubeindex * 16 + i] != -1; i += 3) {
					[unroll]
					for (int vertex = 0; vertex < 3; ++vertex) {
						OUT.pos = mul(UNITY_MATRIX_MVP, float4(vertlist[triTable[cubeindex * 16 + i + vertex]], 0.0f));
						stream.Append(OUT);
					}
					stream.RestartStrip();
				}
				OUT.pos = mul(UNITY_MATRIX_MVP, float4(0.0f, 0.0f, 0.0f, 0.0f));
				stream.Append(OUT);
				OUT.pos = mul(UNITY_MATRIX_MVP, float4(1.0f, 0.0f, 0.0f, 0.0f));
				stream.Append(OUT);
				OUT.pos = mul(UNITY_MATRIX_MVP, float4(0.0f, 1.0f, 0.0f, 0.0f));
				stream.Append(OUT);
				stream.RestartStrip();
			}

			void marchingCubesVoxel(const V2G IN, const float size, inout TriangleStream<G2F> stream) {
				int3 corners[8] = {
					{ 0, 0, 0 },
					{ 1, 0, 0 },
					{ 1, 1, 0 },
					{ 0, 1, 0 }, 
					{ 0, 0, 1 },
					{ 1, 0, 1 },
					{ 1, 1, 1 },
					{ 0, 1, 1 },
				};
				float3 p[8];
				double val[8];
				[unroll]
				for (int corner = 0; corner < 8; ++corner) {
					p[corner] = IN.pos + corners[corner] * size;
					val[corner] = _Data.Load(int4(IN.voxel + corners[corner], 0)).a;
				}
				Polygonise(p, val, _Isolevel, stream);
			}

			void naiveSurfaceNetsVoxel(const V2G IN, const float size, inout TriangleStream<G2F> stream) {
			}

			void dualContouringVoxel(const V2G IN, const float size, inout TriangleStream<G2F> stream) {
			}

			[maxvertexcount(24)]
			void geometryShader(const point V2G IN[1], inout TriangleStream<G2F> stream) {
				const float voxelSize = _ChunkSize / float(_ChunkVoxelSize);
				switch (_VoxelMethod) {
				case METHOD_BILLBOARDS: billboardsVoxel(IN[0], voxelSize, stream); break;
				case METHOD_BLOCKS: blocksVoxel(IN[0], voxelSize, stream); break;
				case METHOD_MARCHING_CUBES: marchingCubesVoxel(IN[0], voxelSize, stream); break;
				case METHOD_NAIVE_SURFACE_NETS: naiveSurfaceNetsVoxel(IN[0], voxelSize, stream); break;
				case METHOD_DUAL_CONTOURING: dualContouringVoxel(IN[0], voxelSize, stream); break;
				}
			}

			fixed4 fragmentShader(G2F IN) : SV_Target{
				switch (_VoxelMethod) {
				case METHOD_BILLBOARDS:
					return IN.colour * IN.diffuse;
					break;
				default:
					return IN.colour * IN.diffuse;
				}
			}

			ENDCG
		}
	}
}