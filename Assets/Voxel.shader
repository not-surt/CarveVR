Shader "CarveVR/Voxel" {
	Properties{
		_ChunkSize("Chunk Size", Float) = 1.0
		_ChunkVoxelSize("Chunk Voxel Size", Int) = 16
		_Threshold("Isosurface Threshold", Range(0.0, 1.0)) = 0.5
		[KeywordEnum(Blocks, Marching Cubes, Naive Surface Nets, Dual Contouring)] _VoxelMethod("Voxel Method", Int) = 0
		_Data("Voxel Data", 3D) = "white" {}
	}
	SubShader {
		Pass {
			CGPROGRAM
			#pragma target 5.0
			#include "UnityCG.cginc"

			#pragma vertex vertexShader
			#pragma geometry geometryShader
			#pragma fragment fragmentShader

			const float _ChunkSize;
			const int _ChunkVoxelSize;
			const float _Threshold;
			Texture3D _Data;
			Texture3D _Neighborhood[3][3][3];
			bool _NeighborPresent[3][3][3];

			#define METHOD_BLOCKS 0
			#define METHOD_MARCHING_CUBES 1
			#define NAIVE_SURFACE_NETS 2
			#define METHOD_DUAL_CONTOURING 3
			const int _VoxelMethod;

			struct Vertex {
				float4 pos : POSITION;
				nointerpolation int3 voxel : TEXCOORD0;
				fixed4 colour : COLOR;
				bool boundary : TEXCOORD1;
			};

			Vertex vertexShader(float4 vertex : POSITION, float3 voxel : TEXCOORD0) {
				Vertex OUT;
				OUT.pos = vertex;
				OUT.voxel = voxel;
				OUT.colour = _Data.Load(int4(voxel, 0));
				const int3 neighbours[8] = {
					int3(-1, -1, -1),
					int3(-1, -1, 1),
					int3(-1, 1, -1),
					int3(-1, 1, 1),
					int3(1, -1, -1),
					int3(1, -1, 1),
					int3(1, 1, -1),
					int3(1, 1, 1),
				};
				int neighbourhood = 0;
				if (OUT.voxel.x == 0 || OUT.voxel.x == _ChunkVoxelSize - 1 ||
					OUT.voxel.y == 0 || OUT.voxel.y == _ChunkVoxelSize - 1 ||
					OUT.voxel.z == 0 || OUT.voxel.z == _ChunkVoxelSize - 1) {
					neighbourhood = 0;
				}
				else {
					for (int i = 0; i < 8; ++i) {
						neighbourhood += (_Data.Load(int4(voxel + neighbours[i], 0)).a > 1.0 - _Threshold) ? 1 : 0;
					}
				}
				OUT.boundary = (OUT.colour.a > (1.0 - _Threshold) && neighbourhood < 8) ? true : false;
				return OUT;
			}

			Vertex vertices[24];

			float isolevelPosition(const float a, const float b, const float isolevel) {
				return (isolevel - a) / (b - a);
			}

			void blocksVoxel(const Vertex IN, const float size, inout TriangleStream<Vertex> stream) {
				const int3 corners[8] = {
					int3(0, 0, 0),
					int3(0, 0, 1),
					int3(0, 1, 0),
					int3(0, 1, 1),
					int3(1, 0, 0),
					int3(1, 0, 1),
					int3(1, 1, 0),
					int3(1, 1, 1),
				};
				const int sides[6][4] = {
					{0, 1, 3, 2},
					{0, 2, 6, 4},
					{4, 6, 7, 5},
					{7, 3, 1, 5},
					{2, 3, 7, 6},
					{0, 4, 5, 1},
				};
				const int3 boundaries[6] = {
					int3(0, 0, -1),
					int3(0, 0, 1),
					int3(0, -1, 0),
					int3(0, 1, 0),
					int3(-1, 0, 0),
					int3(1, 0, 0),
				};
				//if (IN.boundary) {
				if (IN.colour.a >= _Threshold) {
					Vertex OUT;
					OUT.voxel = IN.voxel;
					OUT.colour = float4(IN.colour.rgb, 1.0);
					OUT.boundary = IN.boundary;
					for (int boundary = 0; boundary < 6; ++boundary) {
						const float position = isolevelPosition(_Data.Load(int4(IN.voxel, 0)).a, _Data.Load(int4(IN.voxel + boundaries[boundary], 0)).a, _Threshold);
						if (position >= 0.0 && position <= 1.0) {

						}
					}
					for (int side = 0; side < 6; ++side) {
						/*const float3 ab = corners[sides[side * 4 + 1]] - corners[sides[side * 4 + 0]];
						const float3 ac = corners[sides[side * 4 + 3]] - corners[sides[side * 4 + 0]];
						const float3 normal = normalize(cross(ab, ac));
						const float3 transformed_normal = mul(UNITY_MATRIX_MVP, float4(normal, 0.0)).xyz;
						const float3 vt = normalize(IN.pos.xyz - WorldSpaceViewDir(IN.pos));
						if (dot(vt, normal) > 0.0) {*/
							OUT.pos = mul(UNITY_MATRIX_MVP, IN.pos + float4(corners[sides[side][0]], 0.0) * size);
							stream.Append(OUT);
							OUT.pos = mul(UNITY_MATRIX_MVP, IN.pos + float4(corners[sides[side][1]], 0.0) * size);
							stream.Append(OUT);
							OUT.pos = mul(UNITY_MATRIX_MVP, IN.pos + float4(corners[sides[side][3]], 0.0) * size);
							stream.Append(OUT);
							OUT.pos = mul(UNITY_MATRIX_MVP, IN.pos + float4(corners[sides[side][2]], 0.0) * size);
							stream.Append(OUT);
							stream.RestartStrip();
						//}
					}
				}
			}

			void marchingCubesVoxel(const Vertex IN, const float size, inout TriangleStream<Vertex> stream) {
			}

			void naiveSurfaceNetsVoxel(const Vertex IN, const float size, inout TriangleStream<Vertex> stream) {
			}

			void dualContouringVoxel(const Vertex IN, const float size, inout TriangleStream<Vertex> stream) {
			}

			[maxvertexcount(24)]
			void geometryShader(const point Vertex IN[1], inout TriangleStream<Vertex> stream) {
				const float voxelSize = _ChunkSize / float(_ChunkVoxelSize);
				switch (_VoxelMethod) {
				case METHOD_BLOCKS: blocksVoxel(IN[0], voxelSize, stream); break;
				case METHOD_MARCHING_CUBES: marchingCubesVoxel(IN[0], voxelSize, stream); break;
				case NAIVE_SURFACE_NETS: naiveSurfaceNetsVoxel(IN[0], voxelSize, stream); break;
				case METHOD_DUAL_CONTOURING: dualContouringVoxel(IN[0], voxelSize, stream); break;
				}
			}

			fixed4 fragmentShader(Vertex IN) : SV_Target {
				return IN.colour;
			}

			ENDCG
		}
	}
}