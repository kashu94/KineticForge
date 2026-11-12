using UnityEngine;
using UnityEngine.Rendering;

namespace KineticForge.Crafting
{
    /// <summary>
    /// Builds the relic tablet mesh: a slab centered on its origin whose front face (−Z, the carved face) is a dense
    /// vertex grid that chisel strikes can dent, framed by a cast chamfer, with single quads for the other faces.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The front face comes first: vertex <c>j·(n+1)+i</c> sits at UV (i/n, j/n), with U along +X and V along +Y, the same
    /// face UV as <see cref="RelicCarvingSurface.WorldToUV"/>. Its vertex color is red (1, 0, 0), which the Forge Metal
    /// shader uses to show runes and Chladni lines on that face only. Every face has its own vertices, so the edges stay
    /// sharp when normals are recalculated after carving.
    /// </para>
    /// <para>
    /// The face keeps exactly <c>size.x</c> by <c>size.y</c>; a 45-degree chamfer of up to <see cref="MaxChamfer"/> runs
    /// from its edges out to the sides, so the body is slightly wider and taller than the face and its edges catch the
    /// light like cast bronze.
    /// </para>
    /// </remarks>
    public static class RelicForgeMesh
    {
        /// <summary>Default grid resolution of the front face, in quads per side.</summary>
        public const int DefaultResolution = 112;

        /// <summary>Largest chamfer (m) around the carvable face.</summary>
        public const float MaxChamfer = 0.008f;

        /// <summary>Chamfer as a share of the slab's depth, so thin plaques get a proportionate one.</summary>
        public const float ChamferDepthShare = 0.3f;

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
            int vertexCount = frontVertices + 4 * 4 + 5 * 4;
            Vector3[] positions = new Vector3[vertexCount];
            Vector3[] normals = new Vector3[vertexCount];
            Vector2[] uvs = new Vector2[vertexCount];
            Color32[] colors = new Color32[vertexCount];
            int[] triangles = new int[(n * n + 4 + 5) * 6];

            float a = size.x * 0.5f;
            float b = size.y * 0.5f;
            float c = size.z * 0.5f;

            // A cast chamfer frames the face: the carvable face keeps its exact size and grid, and the body grows by the
            // chamfer around it, so its edges catch the light like cast bronze instead of a sharp box.
            float e = Mathf.Min(MaxChamfer, size.z * ChamferDepthShare);
            float outerA = a + e;
            float outerB = b + e;
            float sideFront = -c + e;
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
            Vector3 innerBL = new Vector3(-a, -b, -c);
            Vector3 innerBR = new Vector3(a, -b, -c);
            Vector3 innerTL = new Vector3(-a, b, -c);
            Vector3 innerTR = new Vector3(a, b, -c);
            Vector3 outerBL = new Vector3(-outerA, -outerB, sideFront);
            Vector3 outerBR = new Vector3(outerA, -outerB, sideFront);
            Vector3 outerTL = new Vector3(-outerA, outerB, sideFront);
            Vector3 outerTR = new Vector3(outerA, outerB, sideFront);
            AddStrip(innerTL, innerTR, outerTL, outerTR, new Vector3(0f, 1f, -1f).normalized);
            AddStrip(innerBR, innerBL, outerBR, outerBL, new Vector3(0f, -1f, -1f).normalized);
            AddStrip(innerTR, innerBR, outerTR, outerBR, new Vector3(1f, 0f, -1f).normalized);
            AddStrip(innerBL, innerTL, outerBL, outerTL, new Vector3(-1f, 0f, -1f).normalized);

            float sideDepth = c - sideFront;
            AddQuad(new Vector3(outerA, -outerB, c), new Vector3(-2f * outerA, 0f, 0f), new Vector3(0f, 2f * outerB, 0f), Vector3.forward);
            AddQuad(new Vector3(outerA, -outerB, sideFront), new Vector3(0f, 0f, sideDepth), new Vector3(0f, 2f * outerB, 0f), Vector3.right);
            AddQuad(new Vector3(-outerA, -outerB, c), new Vector3(0f, 0f, -sideDepth), new Vector3(0f, 2f * outerB, 0f), Vector3.left);
            AddQuad(new Vector3(-outerA, outerB, sideFront), new Vector3(2f * outerA, 0f, 0f), new Vector3(0f, 0f, sideDepth), Vector3.up);
            AddQuad(new Vector3(-outerA, -outerB, sideFront), new Vector3(0f, 0f, sideDepth), new Vector3(2f * outerA, 0f, 0f), Vector3.down);

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

            // A trapezoid from an inner edge (p00 to p10) to its outer edge (p01 to p11), wound to face outward.
            void AddStrip(Vector3 p00, Vector3 p10, Vector3 p01, Vector3 p11, Vector3 normal)
            {
                if (Vector3.Dot(Vector3.Cross(p10 - p00, p01 - p00), normal) > 0f)
                {
                    (p00, p10) = (p10, p00);
                    (p01, p11) = (p11, p01);
                }

                int start = vertex;
                positions[vertex] = p00;
                positions[vertex + 1] = p10;
                positions[vertex + 2] = p01;
                positions[vertex + 3] = p11;
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
