using UnityEngine;
using System.Collections.Generic;

using ProceduralNoiseProject;

namespace SimpleProceduralTerrainProject
{

    public class TerrainGenerator : MonoBehaviour
    {
        //Prototypes
        public Texture2D m_splat0, m_splat1;
        public float m_splatTileSize0 = 10.0f;
        public float m_splatTileSize1 = 2.0f;
        public Texture2D m_detail0, m_detail1, m_detail2;
        public GameObject m_tree0, m_tree1, m_tree2;

        //noise settings
        public int m_seed = 0;
        public float m_groundFrq = 0.001f;
        public float m_treeFrq = 0.005f;
        public float m_detailFrq = 0.01f;

        //Terrain settings
        public int m_tilesX = 2; //Number of terrain tiles on the x axis
        public int m_tilesZ = 2; //Number of terrain tiles on the z axis
        public float m_pixelMapError = 6.0f; //A lower pixel error will draw terrain at a higher Level of detail but will be slower
        public float m_baseMapDist = 1000.0f; //The distance at which the low res base map will be drawn. Decrease to increase performance

        //Terrain data settings
        public int m_heightMapSize = 513; //Higher number will create more detailed height maps
        public int m_alphaMapSize = 1024; //This is the control map that controls how the splat textures will be blended
        public int m_terrainSize = 2048;
        public int m_terrainHeight = 512;
        public int m_detailMapSize = 512; //Resolutions of detail (Grass) layers

        //Tree settings
        public int m_treeSpacing = 32; //spacing between trees
        public float m_treeDistance = 2000.0f; //The distance at which trees will no longer be drawn
        public float m_treeBillboardDistance = 400.0f; //The distance at which trees meshes will turn into tree billboards
        public float m_treeCrossFadeLength = 20.0f; //As trees turn to billboards there transform is rotated to match the meshes, a higher number will make this transition smoother
        public int m_treeMaximumFullLODCount = 400; //The maximum number of trees that will be drawn in a certain area. 

        //Detail settings
        public int m_detailObjectDistance = 400; //The distance at which details will no longer be drawn
        public float m_detailObjectDensity = 4.0f; //Creates more dense details within patch
        public int m_detailResolutionPerPatch = 32; //The size of detail patch. A higher number may reduce draw calls as details will be batch in larger patches
        public float m_wavingGrassStrength = 0.4f;
        public float m_wavingGrassAmount = 0.2f;
        public float m_wavingGrassSpeed = 0.4f;
        public Color m_wavingGrassTint = Color.white;
        public Color m_grassHealthyColor = Color.white;
        public Color m_grassDryColor = Color.white;

        //Private
        private FractalNoise m_groundNoise, m_mountainNoise, m_treeNoise, m_detailNoise;
        private Terrain[,] m_terrain;
        private SplatPrototype[] m_splatPrototypes;
        private TreePrototype[] m_treeProtoTypes;
        private DetailPrototype[] m_detailProtoTypes;
        private Vector2 m_offset;

        void Start()
        {

            m_groundNoise = new FractalNoise(new PerlinNoise(m_seed, m_groundFrq), 6, 1.0f, 0.1f);
            m_treeNoise = new FractalNoise(new PerlinNoise(m_seed + 1, m_treeFrq), 6, 1.0f);
            m_detailNoise = new FractalNoise(new PerlinNoise(m_seed + 2, m_detailFrq), 6, 1.0f);

            m_heightMapSize = Mathf.ClosestPowerOfTwo(m_heightMapSize) + 1;
            m_alphaMapSize = Mathf.ClosestPowerOfTwo(m_alphaMapSize);
            m_detailMapSize = Mathf.ClosestPowerOfTwo(m_detailMapSize);

            if (m_detailResolutionPerPatch < 8)
                m_detailResolutionPerPatch = 8;

            float[,] htmap = new float[m_heightMapSize, m_heightMapSize];

            m_terrain = new Terrain[m_tilesX, m_tilesZ];

            //this will center terrain at origin
            m_offset = new Vector2(-m_terrainSize * m_tilesX * 0.5f, -m_terrainSize * m_tilesZ * 0.5f);

            CreateProtoTypes();

            for (int x = 0; x < m_tilesX; x++)
            {
                for (int z = 0; z < m_tilesZ; z++)
                {
                    FillHeights(htmap, x, z);

                    TerrainData terrainData = new TerrainData();

                    terrainData.heightmapResolution = m_heightMapSize;
                    terrainData.SetHeights(0, 0, htmap);
                    terrainData.size = new Vector3(m_terrainSize, m_terrainHeight, m_terrainSize);
                    terrainData.splatPrototypes = m_splatPrototypes;
                    terrainData.treePrototypes = m_treeProtoTypes;
                    terrainData.detailPrototypes = m_detailProtoTypes;

                    FillAlphaMap(terrainData);

                    m_terrain[x, z] = Terrain.CreateTerrainGameObject(terrainData).GetComponent<Terrain>();
                    m_terrain[x, z].transform.position = new Vector3(m_terrainSize * x + m_offset.x, 0, m_terrainSize * z + m_offset.y);
                    m_terrain[x, z].heightmapPixelError = m_pixelMapError;
                    m_terrain[x, z].basemapDistance = m_baseMapDist;
                    m_terrain[x, z].castShadows = false;

                    FillTreeInstances(m_terrain[x, z], x, z);
                    FillDetailMap(m_terrain[x, z], x, z);

                }
            }

            //Set the neighbours of terrain to remove seams.
            for (int x = 0; x < m_tilesX; x++)
            {
                for (int z = 0; z < m_tilesZ; z++)
                {
                    Terrain right = null;
                    Terrain left = null;
                    Terrain bottom = null;
                    Terrain top = null;

                    if (x > 0) left = m_terrain[(x - 1), z];
                    if (x < m_tilesX - 1) right = m_terrain[(x + 1), z];

                    if (z > 0) bottom = m_terrain[x, (z - 1)];
                    if (z < m_tilesZ - 1) top = m_terrain[x, (z + 1)];

                    m_terrain[x, z].SetNeighbors(left, top, right, bottom);

                }
            }
        }

        void CreateProtoTypes()
        {
            //Ive hard coded 2 splat prototypes, 3 tree prototypes and 3 detail prototypes.
            m_splatPrototypes = new SplatPrototype[2];

            m_splatPrototypes[0] = new SplatPrototype();
            m_splatPrototypes[0].texture = m_splat0;
            m_splatPrototypes[0].tileSize = new Vector2(m_splatTileSize0, m_splatTileSize0);

            m_splatPrototypes[1] = new SplatPrototype();
            m_splatPrototypes[1].texture = m_splat1;
            m_splatPrototypes[1].tileSize = new Vector2(m_splatTileSize1, m_splatTileSize1);

            m_treeProtoTypes = new TreePrototype[3];

            m_treeProtoTypes[0] = new TreePrototype();
            m_treeProtoTypes[0].prefab = m_tree0;

            m_treeProtoTypes[1] = new TreePrototype();
            m_treeProtoTypes[1].prefab = m_tree1;

            m_treeProtoTypes[2] = new TreePrototype();
            m_treeProtoTypes[2].prefab = m_tree2;

            m_detailProtoTypes = new DetailPrototype[3];

            m_detailProtoTypes[0] = new DetailPrototype();
            m_detailProtoTypes[0].prototypeTexture = m_detail0;
            m_detailProtoTypes[0].renderMode = DetailRenderMode.GrassBillboard;
            m_detailProtoTypes[0].healthyColor = m_grassHealthyColor;
            m_detailProtoTypes[0].dryColor = m_grassDryColor;

            m_detailProtoTypes[1] = new DetailPrototype();
            m_detailProtoTypes[1].prototypeTexture = m_detail1;
            m_detailProtoTypes[1].renderMode = DetailRenderMode.GrassBillboard;
            m_detailProtoTypes[1].healthyColor = m_grassHealthyColor;
            m_detailProtoTypes[1].dryColor = m_grassDryColor;

            m_detailProtoTypes[2] = new DetailPrototype();
            m_detailProtoTypes[2].prototypeTexture = m_detail2;
            m_detailProtoTypes[2].renderMode = DetailRenderMode.GrassBillboard;
            m_detailProtoTypes[2].healthyColor = m_grassHealthyColor;
            m_detailProtoTypes[2].dryColor = m_grassDryColor;


        }

        void FillHeights(float[,] htmap, int tileX, int tileZ)
        {
            float ratio = (float)m_terrainSize / (float)m_heightMapSize;

            for (int x = 0; x < m_heightMapSize; x++)
            {
                for (int z = 0; z < m_heightMapSize; z++)
                {
                    float worldPosX = (x + tileX * (m_heightMapSize - 1)) * ratio;
                    float worldPosZ = (z + tileZ * (m_heightMapSize - 1)) * ratio;

                    htmap[z, x] = m_groundNoise.Amplitude + m_groundNoise.Sample2D(worldPosX, worldPosZ);
                }
            }
        }

        void FillAlphaMap(TerrainData terrainData)
        {
            float[,,] map = new float[m_alphaMapSize, m_alphaMapSize, 2];

            for (int x = 0; x < m_alphaMapSize; x++)
            {
                for (int z = 0; z < m_alphaMapSize; z++)
                {
                    // Get the normalized terrain coordinate that
                    // corresponds to the the point.
                    float normX = x * 1.0f / (m_alphaMapSize - 1);
                    float normZ = z * 1.0f / (m_alphaMapSize - 1);

                    // Get the steepness value at the normalized coordinate.
                    float angle = terrainData.GetSteepness(normX, normZ);

                    // Steepness is given as an angle, 0..90 degrees. Divide
                    // by 90 to get an alpha blending value in the range 0..1.
                    float frac = angle / 90.0f;
                    map[z, x, 0] = frac;
                    map[z, x, 1] = 1.0f - frac;

                }
            }

            terrainData.alphamapResolution = m_alphaMapSize;
            terrainData.SetAlphamaps(0, 0, map);
        }

        void FillTreeInstances(Terrain terrain, int tileX, int tileZ)
        {
            Random.InitState(0);

            for (int x = 0; x < m_terrainSize; x += m_treeSpacing)
            {
                for (int z = 0; z < m_terrainSize; z += m_treeSpacing)
                {

                    float unit = 1.0f / (m_terrainSize - 1);

                    float offsetX = Random.value * unit * m_treeSpacing;
                    float offsetZ = Random.value * unit * m_treeSpacing;

                    float normX = x * unit + offsetX;
                    float normZ = z * unit + offsetZ;

                    // Get the steepness value at the normalized coordinate.
                    float angle = terrain.terrainData.GetSteepness(normX, normZ);

                    // Steepness is given as an angle, 0..90 degrees. Divide
                    // by 90 to get an alpha blending value in the range 0..1.
                    float frac = angle / 90.0f;

                    if (frac < 0.5f) //make sure tree are not on steep slopes
                    {
                        float worldPosX = x + tileX * (m_terrainSize - 1);
                        float worldPosZ = z + tileZ * (m_terrainSize - 1);

                        float noise = m_treeNoise.Sample2D(worldPosX, worldPosZ);
                        float ht = terrain.terrainData.GetInterpolatedHeight(normX, normZ);

                        if (noise > 0.0f && ht < m_terrainHeight * 0.4f)
                        {

                            TreeInstance temp = new TreeInstance();
                            temp.position = new Vector3(normX, ht, normZ);
                            temp.prototypeIndex = Random.Range(0, 3);
                            temp.widthScale = 1;
                            temp.heightScale = 1;
                            temp.color = Color.white;
                            temp.lightmapColor = Color.white;

                            terrain.AddTreeInstance(temp);
                        }
                    }

                }
            }

            terrain.treeDistance = m_treeDistance;
            terrain.treeBillboardDistance = m_treeBillboardDistance;
            terrain.treeCrossFadeLength = m_treeCrossFadeLength;
            terrain.treeMaximumFullLODCount = m_treeMaximumFullLODCount;

        }

        void FillDetailMap(Terrain terrain, int tileX, int tileZ)
        {
            //each layer is drawn separately so if you have a lot of layers your draw calls will increase 
            int[,] detailMap0 = new int[m_detailMapSize, m_detailMapSize];
            int[,] detailMap1 = new int[m_detailMapSize, m_detailMapSize];
            int[,] detailMap2 = new int[m_detailMapSize, m_detailMapSize];

            float ratio = (float)m_terrainSize / (float)m_detailMapSize;

            Random.InitState(0);

            for (int x = 0; x < m_detailMapSize; x++)
            {
                for (int z = 0; z < m_detailMapSize; z++)
                {
                    detailMap0[z, x] = 0;
                    detailMap1[z, x] = 0;
                    detailMap2[z, x] = 0;

                    float unit = 1.0f / (m_detailMapSize - 1);

                    float normX = x * unit;
                    float normZ = z * unit;

                    // Get the steepness value at the normalized coordinate.
                    float angle = terrain.terrainData.GetSteepness(normX, normZ);

                    // Steepness is given as an angle, 0..90 degrees. Divide
                    // by 90 to get an alpha blending value in the range 0..1.
                    float frac = angle / 90.0f;

                    if (frac < 0.5f)
                    {
                        float worldPosX = (x + tileX * (m_detailMapSize - 1)) * ratio;
                        float worldPosZ = (z + tileZ * (m_detailMapSize - 1)) * ratio;

                        float noise = m_detailNoise.Sample2D(worldPosX, worldPosZ);

                        if (noise > 0.0f)
                        {
                            float rnd = Random.value;
                            //Randomly select what layer to use
                            if (rnd < 0.33f)
                                detailMap0[z, x] = 1;
                            else if (rnd < 0.66f)
                                detailMap1[z, x] = 1;
                            else
                                detailMap2[z, x] = 1;
                        }
                    }

                }
            }

            terrain.terrainData.wavingGrassStrength = m_wavingGrassStrength;
            terrain.terrainData.wavingGrassAmount = m_wavingGrassAmount;
            terrain.terrainData.wavingGrassSpeed = m_wavingGrassSpeed;
            terrain.terrainData.wavingGrassTint = m_wavingGrassTint;
            terrain.detailObjectDensity = m_detailObjectDensity;
            terrain.detailObjectDistance = m_detailObjectDistance;
            terrain.terrainData.SetDetailResolution(m_detailMapSize, m_detailResolutionPerPatch);

            terrain.terrainData.SetDetailLayer(0, 0, 0, detailMap0);
            terrain.terrainData.SetDetailLayer(0, 0, 1, detailMap1);
            terrain.terrainData.SetDetailLayer(0, 0, 2, detailMap2);

        }

    }
}


