using UnityEngine;
using UnityEngine.Rendering;

namespace KinoVR
{
    // A small mesh per chamber keeps culling local while sharing the font atlas.
    [ExecuteAlways, DisallowMultipleComponent]
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public sealed class KinoNumberBatch : MonoBehaviour
    {
        public KinoAirChamber chamber;
        public KinoNumberGeometry geometry;
        public MeshFilter farNumbers;
        public Vector3 glassCenter;
        Mesh mesh, farMesh;
        Vector3[] positions, normals;
        Vector4[] uv;
        int[] numbers;
        KinoNumberGeometry.NumberShape[] shapes;
        KinoNumberGeometry.NumberShape[] library;
        KinoBallNumber[] balls;
        int[] nearIndices, farIndices;

        void OnEnable() => Refresh();
        void LateUpdate() => Refresh();

        public void Refresh()
        {
            if (!chamber || !geometry || !geometry.material) return;
            bool rebuild = !mesh || (farNumbers && !farMesh) || balls != chamber.balls || library != geometry.numbers || numbers.Length != chamber.balls.Length;
            if (!rebuild)
                for (int i = 0; i < numbers.Length; i++)
                    if (numbers[i] != (chamber.balls[i] ? chamber.balls[i].Number : 0)) { rebuild = true; break; }
            if (rebuild) Rebuild();
            if (!mesh) return;

            var inverse = transform.worldToLocalMatrix;
            int vertex = 0, nearCount = 0, farCount = 0;
            Transform viewer = null;
            foreach (var ball in balls) if (ball && ball.Viewer) { viewer = ball.Viewer; break; }
            Vector3 center = chamber.transform.TransformPoint(glassCenter);
            float glassDistance = viewer ? (viewer.position - center).sqrMagnitude : 0;
            for (int i = 0; i < balls.Length; i++)
            {
                var shape = shapes[i];
                if (shape == null) continue;
                var ball = balls[i];
                ball.RefreshFacing();
                bool visible = ball.gameObject.activeInHierarchy && ball.face;
                var matrix = visible ? inverse * ball.face.localToWorldMatrix : Matrix4x4.zero;
                var normal = visible ? transform.InverseTransformDirection(ball.face.forward * -1).normalized : Vector3.back;
                float scale = Mathf.Abs(ball.transform.lossyScale.y);
                bool behindGlass = visible && farMesh && viewer &&
                    (ball.face.TransformPoint(Vector3.back * .005f) - viewer.position).sqrMagnitude > glassDistance;
                if (visible)
                    foreach (int t in shape.triangles)
                        if (behindGlass) farIndices[farCount++] = vertex + t;
                        else nearIndices[nearCount++] = vertex + t;
                for (int v = 0; v < shape.vertices.Length; v++, vertex++)
                {
                    positions[vertex] = matrix.MultiplyPoint3x4(shape.vertices[v]);
                    normals[vertex] = normal;
                    uv[vertex] = shape.uv[v];
                    uv[vertex].w *= scale;
                }
            }
            mesh.SetVertices(positions);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uv);
            mesh.SetTriangles(nearIndices, 0, nearCount, 0, false);
            GetComponent<MeshRenderer>().enabled = nearCount > 0;
            var towardViewer = viewer ? (viewer.position - center).normalized * .02f : Vector3.zero;
            SetSortBounds(mesh, inverse.MultiplyPoint3x4(center + towardViewer));
            if (farMesh)
            {
                farMesh.SetVertices(positions);
                farMesh.SetNormals(normals);
                farMesh.SetUVs(0, uv);
                farMesh.SetTriangles(farIndices, 0, farCount, 0, false);
                farNumbers.GetComponent<MeshRenderer>().enabled = farCount > 0;
                SetSortBounds(farMesh, inverse.MultiplyPoint3x4(center - towardViewer));
            }
        }

        void SetSortBounds(Mesh target, Vector3 center)
        {
            // Sorting uses this centre. Expand symmetrically to enclose every
            // vertex, so the depth split never clips a chamber's moving numbers.
            var extents = Vector3.zero;
            foreach (var position in positions)
            {
                var delta = position - center;
                extents = Vector3.Max(extents, new Vector3(Mathf.Abs(delta.x), Mathf.Abs(delta.y), Mathf.Abs(delta.z)));
            }
            target.bounds = new Bounds(center, extents * 2 + Vector3.one * .001f);
        }

        void Rebuild()
        {
            if (!mesh)
            {
                mesh = new Mesh { name = "KINO chamber numbers", hideFlags = HideFlags.HideAndDontSave };
                mesh.MarkDynamic();
            }
            mesh.Clear();
            GetComponent<MeshFilter>().sharedMesh = mesh;
            ConfigureRenderer(GetComponent<MeshRenderer>());
            if (farNumbers)
            {
                if (!farMesh)
                {
                    farMesh = new Mesh { name = "KINO numbers behind glass", hideFlags = HideFlags.HideAndDontSave };
                    farMesh.MarkDynamic();
                }
                farMesh.Clear();
                farNumbers.sharedMesh = farMesh;
                ConfigureRenderer(farNumbers.GetComponent<MeshRenderer>());
            }
            balls = chamber.balls;
            library = geometry.numbers;
            numbers = new int[balls.Length];
            shapes = new KinoNumberGeometry.NumberShape[balls.Length];
            int vertexCount = 0, indexCount = 0;
            for (int i = 0; i < balls.Length; i++)
            {
                numbers[i] = balls[i] ? balls[i].Number : 0;
                shapes[i] = geometry.Get(numbers[i]);
                if (shapes[i] == null) continue;
                vertexCount += shapes[i].vertices.Length;
                indexCount += shapes[i].triangles.Length;
            }
            positions = new Vector3[vertexCount]; normals = new Vector3[vertexCount]; uv = new Vector4[vertexCount];
            var colors = new Color32[vertexCount];
            nearIndices = new int[indexCount]; farIndices = new int[indexCount];
            int vertex = 0;
            foreach (var shape in shapes)
            {
                if (shape == null) continue;
                for (int v = 0; v < shape.vertices.Length; v++) colors[vertex + v] = shape.colors[v];
                vertex += shape.vertices.Length;
            }
            mesh.vertices = positions;
            mesh.colors32 = colors;
            if (farMesh) { farMesh.vertices = positions; farMesh.colors32 = colors; }
        }

        void ConfigureRenderer(MeshRenderer renderer)
        {
            renderer.sharedMaterial = geometry.material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            renderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
        }

        void OnDisable()
        {
            if (!mesh) return;
            GetComponent<MeshFilter>().sharedMesh = null;
            if (Application.isPlaying) Destroy(mesh); else DestroyImmediate(mesh);
            mesh = null;
            if (farNumbers) farNumbers.sharedMesh = null;
            if (farMesh) { if (Application.isPlaying) Destroy(farMesh); else DestroyImmediate(farMesh); }
            farMesh = null;
        }
    }
}
