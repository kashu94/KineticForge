using UnityEditor;
using UnityEngine;

namespace KineticForge.EditorTools
{
    /// <summary>
    /// Generates the forge's architectural meshes with world-scaled texture coordinates, so scanned materials keep the
    /// same texel density on every wall, beam and block instead of stretching across Unity's unit primitives.
    /// </summary>
    /// <remarks>
    /// Meshes are saved under <c>Assets/KineticForge/Generated/Meshes</c>, keyed by their dimensions, and reused when a
    /// matching one exists. Each gets a lightmap UV set, normals and tangents, ready for baked lighting and normal maps.
    /// </remarks>
    internal static class ForgeMeshes
    {
        const string Folder = "Assets/KineticForge/Generated/Meshes";

        /// <summary>
        /// A box of <paramref name="size"/> meters, centered on its origin, whose texture repeats every
        /// <paramref name="tile"/> meters on every face.
        /// </summary>
        /// <param name="size">Dimensions in meters.</param>
        /// <param name="tile">Meters per texture repeat.</param>
        /// <returns>The saved mesh.</returns>
        internal static Mesh Box(Vector3 size, float tile)
        {
            string name = "Box " + Key(size.x) + "x" + Key(size.y) + "x" + Key(size.z) + " t" + Key(tile);
            Mesh existing = Load(name);
            if (existing != null)
            {
                return existing;
            }

            Vector3 h = size * 0.5f;
            Vector3[] vertices = new Vector3[24];
            Vector3[] normals = new Vector3[24];
            Vector2[] uvs = new Vector2[24];
            int[] triangles = new int[36];

            // Each face: normal, the two in-plane axes (u, v) and their extents.
            Vector3[] faceNormals = { Vector3.forward, Vector3.back, Vector3.right, Vector3.left, Vector3.up, Vector3.down };
            Vector3[] faceU = { Vector3.left, Vector3.right, Vector3.forward, Vector3.back, Vector3.right, Vector3.right };
            Vector3[] faceV = { Vector3.up, Vector3.up, Vector3.up, Vector3.up, Vector3.forward, Vector3.back };
            for (int f = 0; f < 6; f++)
            {
                Vector3 n = faceNormals[f];
                Vector3 u = faceU[f];
                Vector3 v = faceV[f];
                float du = Mathf.Abs(Vector3.Dot(u, size)) * 0.5f;
                float dv = Mathf.Abs(Vector3.Dot(v, size)) * 0.5f;
                Vector3 center = Vector3.Scale(n, h);
                for (int c = 0; c < 4; c++)
                {
                    float su = c == 1 || c == 2 ? 1f : -1f;
                    float sv = c >= 2 ? 1f : -1f;
                    int index = f * 4 + c;
                    vertices[index] = center + u * (su * du) + v * (sv * dv);
                    normals[index] = n;
                    uvs[index] = new Vector2((su * du + du) / tile, (sv * dv + dv) / tile);
                }

                int t = f * 6;
                int b = f * 4;
                triangles[t] = b;
                triangles[t + 1] = b + 2;
                triangles[t + 2] = b + 1;
                triangles[t + 3] = b;
                triangles[t + 4] = b + 3;
                triangles[t + 5] = b + 2;
            }

            return Save(name, vertices, normals, uvs, triangles);
        }

        /// <summary>
        /// A tapered cylinder along +X from radius <paramref name="baseRadius"/> at x = 0 to
        /// <paramref name="tipRadius"/> at x = <paramref name="length"/>, such as an anvil's horn.
        /// </summary>
        /// <param name="baseRadius">Radius at the base, in meters.</param>
        /// <param name="tipRadius">Radius at the tip, in meters.</param>
        /// <param name="length">Length along +X, in meters.</param>
        /// <param name="tile">Meters per texture repeat.</param>
        /// <param name="segments">Sides around the axis.</param>
        /// <returns>The saved mesh.</returns>
        internal static Mesh Horn(float baseRadius, float tipRadius, float length, float tile, int segments = 20)
        {
            string name = "Horn " + Key(baseRadius) + "-" + Key(tipRadius) + "x" + Key(length) + " t" + Key(tile) + " s" + segments;
            Mesh existing = Load(name);
            if (existing != null)
            {
                return existing;
            }

            int ring = segments + 1;
            Vector3[] vertices = new Vector3[ring * 2 + 1];
            Vector3[] normals = new Vector3[vertices.Length];
            Vector2[] uvs = new Vector2[vertices.Length];
            int[] triangles = new int[segments * 6 + segments * 3];
            float slope = (baseRadius - tipRadius) / length;
            float circumference = 2f * Mathf.PI * baseRadius;
            for (int i = 0; i < ring; i++)
            {
                float angle = 2f * Mathf.PI * i / segments;
                Vector3 radial = new Vector3(0f, Mathf.Cos(angle), Mathf.Sin(angle));
                Vector3 normal = (radial + Vector3.right * slope).normalized;
                vertices[i] = radial * baseRadius;
                vertices[ring + i] = Vector3.right * length + radial * tipRadius;
                normals[i] = normal;
                normals[ring + i] = normal;
                float u = circumference * i / segments / tile;
                uvs[i] = new Vector2(u, 0f);
                uvs[ring + i] = new Vector2(u, length / tile);
            }

            int cap = ring * 2;
            vertices[cap] = Vector3.zero;
            normals[cap] = Vector3.left;
            uvs[cap] = Vector2.zero;
            int t = 0;
            for (int i = 0; i < segments; i++)
            {
                triangles[t++] = i;
                triangles[t++] = i + 1;
                triangles[t++] = ring + i + 1;
                triangles[t++] = i;
                triangles[t++] = ring + i + 1;
                triangles[t++] = ring + i;
                triangles[t++] = cap;
                triangles[t++] = i + 1;
                triangles[t++] = i;
            }

            return Save(name, vertices, normals, uvs, triangles);
        }

        static Mesh Save(string name, Vector3[] vertices, Vector3[] normals, Vector2[] uvs, int[] triangles)
        {
            Mesh mesh = new Mesh { name = name };
            mesh.vertices = vertices;
            mesh.normals = normals;
            mesh.uv = uvs;
            mesh.triangles = triangles;
            mesh.RecalculateTangents();
            mesh.RecalculateBounds();
            Unwrapping.GenerateSecondaryUVSet(mesh);
            ForgeEnvironmentBuilder.EnsureFolder(Folder);
            AssetDatabase.CreateAsset(mesh, Folder + "/" + name + ".asset");
            return mesh;
        }

        static Mesh Load(string name)
        {
            return AssetDatabase.LoadAssetAtPath<Mesh>(Folder + "/" + name + ".asset");
        }

        static string Key(float value)
        {
            return value.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
        }
    }
}
