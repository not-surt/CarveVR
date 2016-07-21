// Upgrade NOTE: replaced '_World2Object' with 'unity_WorldToObject'

Shader "CarveVR/Chunk" {
	Properties{
		_ChunkSize("Chunk Size", Float) = 1.0
		_ChunkVoxelSize("Chunk Voxel Size", Int) = 16
		_Isolevel("Isosurface level", Range(0.0, 1.0)) = 0.5
		[KeywordEnum(Blocks, Marching Cubes, Naive Surface Nets, Dual Contouring)] _VoxelMethod("Voxel Method", Int) = 0
		_Data("Voxel Data", 3D) = "white" {}
		[Toggle]_Smooth("Smooth Normals", Float) = 1.0
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

			const float _ChunkSize;
			const int _ChunkVoxelSize;
			const float _Isolevel;
			const float _Smooth;
			Texture3D _Data;
			Texture3D _Neighborhood[3][3][3];
			bool _NeighborPresent[3][3][3];
			float4x4 voxelMatrix;

			#define METHOD_BLOCKS 0
			#define METHOD_MARCHING_CUBES 1
			#define METHOD_NAIVE_SURFACE_NETS 2
			#define METHOD_DUAL_CONTOURING 3
			const int _VoxelMethod;

			struct V2G {
				float4 pos : POSITION;
				nointerpolation int3 voxel : TEXCOORD0;
				fixed4 colour : COLOR;
			};

			struct G2F {
				float4 pos : POSITION;
				nointerpolation int3 voxel : TEXCOORD0;
				fixed4 colour : COLOR0;
				float3 normal : TEXCOORD1;
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

			void blocksVoxel(const V2G IN, const float size, inout TriangleStream<G2F> stream) {
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
					{ 0, 2, 4, 6 },
					{ 7, 3, 5, 1 },
					{ 0, 4, 1, 5 },
					{ 2, 3, 6, 7 },
					{ 0, 1, 2, 3 },
					{ 4, 6, 5, 7 },
				};
				const int3 boundaries[6] = {
					int3(0, 0, -1),
					int3(0, 0, 1),
					int3(0, -1, 0),
					int3(0, 1, 0),
					int3(-1, 0, 0),
					int3(1, 0, 0),
				};
				//if (IN.colour.a >= _Isolevel) {
					G2F OUT;
					OUT.voxel = IN.voxel;
					OUT.colour = float4(IN.colour.rgb, 1.0);
					for (int boundary = 0; boundary < 6; ++boundary) {
						OUT.normal = float3(boundaries[boundary]);
						half3 worldNormal = UnityObjectToWorldNormal(OUT.normal);
						half nl = max(0, dot(worldNormal, _WorldSpaceLightPos0.xyz));
						OUT.diffuse = nl * _LightColor0;
						//float3 t = corners[sides[boundary][0]] - mul(unity_WorldToObject, _WorldSpaceCameraPos);
						//const bool cull = !(dot(t, OUT.normal) <= 0);
						//const bool cull = dot(normalize(ObjSpaceViewDir(IN.pos)), OUT.normal) < 0.0;
						const bool cull = false;
						const float position = isolevelPosition(_Data.Load(int4(IN.voxel, 0)).a, _Data.Load(int4(IN.voxel + boundaries[boundary], 0)).a, _Isolevel);
						if (!cull && position >= 0.0 && position < 1.0) {
							for (int corner = 0; corner < 4; ++corner) {
								OUT.pos = mul(UNITY_MATRIX_MVP, IN.pos + float4(corners[sides[boundary][corner]], 0.0) * size);
								stream.Append(OUT);
							}
							stream.RestartStrip();
						}
					}
				//}
			}

			void marchingCubesVoxel(const V2G IN, const float size, inout TriangleStream<G2F> stream) {
			}

			void naiveSurfaceNetsVoxel(const V2G IN, const float size, inout TriangleStream<G2F> stream) {
			}

			void dualContouringVoxel(const V2G IN, const float size, inout TriangleStream<G2F> stream) {
			}

			[maxvertexcount(24)]
			void geometryShader(const point V2G IN[1], inout TriangleStream<G2F> stream) {
				const float voxelSize = _ChunkSize / float(_ChunkVoxelSize);
				switch (_VoxelMethod) {
				case METHOD_BLOCKS: blocksVoxel(IN[0], voxelSize, stream); break;
				case METHOD_MARCHING_CUBES: marchingCubesVoxel(IN[0], voxelSize, stream); break;
				case METHOD_NAIVE_SURFACE_NETS: naiveSurfaceNetsVoxel(IN[0], voxelSize, stream); break;
				case METHOD_DUAL_CONTOURING: dualContouringVoxel(IN[0], voxelSize, stream); break;
				}
			}

			fixed4 fragmentShader(G2F IN) : SV_Target {
				return IN.colour *= IN.diffuse;
			}

			ENDCG
		}
	}
}