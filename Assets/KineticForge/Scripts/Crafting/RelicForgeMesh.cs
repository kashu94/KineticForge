using UnityEngine;
using UnityEngine.Rendering;

namespace KineticForge.Crafting
{
    /// <summary>
    /// Builds the relic tablet mesh: a slab centered on its origin whose front face (−Z, the carved face) is a dense
    /// vertex grid that chisel strikes can dent, and whose other faces are single quads.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The front face comes first: vertex <c>j·(n+1)+i</c> sits at UV (i/n, j/n), with U along +X and V along +Y, the same
    /// face UV as <see cref="RelicCarvingSurface.WorldToUV"/>. Its vertex color is red (1, 0, 0), which the Forge Metal
    /// shader uses to show runes and Chladni lines on that face only. Every face has its own vertices, so the edges stay
    /// sharp when normals are recalculated after carving.
    /// </para>
    /// </remarks>
    public static class RelicForgeMesh
    {
        /// <summary>Default grid resolution of the front face, in quads per side.</summary>
        public const int DefaultResolution = 112;

        /// <summary>Number of vertices on the front face for a grid resolution.</summary>
        /// <param name="resolution">Quads per side.</param>
        /// <returns>The vertex count.</returns>
        public static int FrontVertexCount(int resolution)
        {
            return (resolution + 1) * (resolution + 1);
        }

        /// <summary>Builds a new relic tablet mesh.</summary>
        /// <param name="size">Width (X), height (Y) and depth (Z), in meters.</param>
        /// <param name="resolution">Front face quads per side, 2 to 250.</param>
        /// <returns>The mesh, named "Relic Tablet".</returns>
        public static Mesh Build(Vector3 size, int resolution)
        {
            int n = Mathf.Clamp(resolution, 2, 250);
            int frontVertices = FrontVertexCount(n);
            int vertexCount = frontVertices + 5 * 4;
            Vector3[] positions = new Vector3[vertexCount];
            Vector3[] normals = new Vector3[vertexCount];
            Vector2[] uvs = new Vector2[vertexCount];
            Color32[] colors = new Color32[vertexCount];
            int[] triangles = new int[(n * n + 5) * 6];

            float a = size.x * 0.5f;
            float b = size.y * 0.5f;
            float c = size.z * 0.5f;
            Color32 front = new Color32(255, 0, 0, 255);
            Color32 plain = new Color32(0, 0, 0, 255);

            int t = 0;
            for (int j = 0; j <= n; j++)
            {
                for (int i = 0; i <= n; i++)
                {
                    int index = j * (n + 1) + i;
                    float u = (float)i / n;
                    float v = (float)j / n;
                    positions[index] = new Vector3(-a + u * size.x, -b + v * size.y, -c);
                    normals[index] = Vector3.back;
                    uvs[index] = new Vector2(u, v);
                    colors[index] = front;
                }
            }

            for (int j = 0; j < n; j++)
            {
                for (int i = 0; i < n; i++)
                {
                    int bl = j * (n + 1) + i;
                    int br = bl + 1;
                    int tl = bl + n + 1;
                    int tr = tl + 1;
                    triangles[t++] = bl;
                    triangles[t++] = tl;
                    triangles[t++] = tr;
                    triangles[t++] = bl;
                    triangles[t++] = tr;
                    triangles[t++] = br;
                }
            }

            // Each quad spans origin + U + V with cross(U, V) = −normal, so (p00, p01, p11) winds clockwise from outside.
            int vertex = frontVertices;
            AddQuad(new Vector3(a, -b, c), new Vector3(-size.x, 0f, 0f), new Vector3(0f, size.y, 0f), Vector3.forward);
            AddQuad(new Vector3(a, -b, -c), new Vector3(0f, 0f, size.z), new Vector3(0f, size.y, 0f), Vector3.right);
            AddQuad(new Vector3(-a, -b, c), new Vector3(0f, 0f, -size.z), new Vector3(0f, size.y, 0f), Vector3.left);
            AddQuad(new Vector3(-a, b, -c), new Vector3(size.x, 0f, 0f), new Vector3(0f, 0f, size.z), Vector3.up);
            AddQuad(new Vector3(-a, -b, -c), new Vector3(0f, 0f, size.z), new Vector3(size.x, 0f, 0f), Vector3.down);

            Mesh mesh = new Mesh { name = "Relic Tablet" };
            mesh.indexFormat = vertexCount > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16;
            mesh.SetVertices(positions);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            mesh.SetColors(colors);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateTangents();
            mesh.RecalculateBounds();
            return mesh;

            void AddQuad(Vector3 origin, Vector3 axisU, Vector3 axisV, Vector3 normal)
            {
                int start = vertex;
                positions[vertex] = origin;
                positions[vertex + 1] = origin + axisU;
                positions[vertex + 2] = origin + axisV;
                positions[vertex + 3] = origin + axisU + axisV;
                uvs[vertex] = new Vector2(0f, 0f);
                uvs[vertex + 1] = new Vector2(1f, 0f);
                uvs[vertex + 2] = new Vector2(0f, 1f);
                uvs[vertex + 3] = new Vector2(1f, 1f);
                for (int k = 0; k < 4; k++)
                {
                    normals[vertex + k] = normal;
                    colors[vertex + k] = plain;
                }

                triangles[t++] = start;
                triangles[t++] = start + 2;
                triangles[t++] = start + 3;
                triangles[t++] = start;
                triangles[t++] = start + 3;
                triangles[t++] = start + 1;
                vertex += 4;
            }
        }
    }
}
