using System;
using System.Collections.Generic;
using Android.Content;
using Android.Opengl;
using Android.Views;
using Java.Nio;
using Javax.Microedition.Khronos.Egl;
using Javax.Microedition.Khronos.Opengles;
using AssetStudio;

namespace HoKTool.Android;

/// <summary>
/// Android OpenGL ES 2.0 static Mesh preview. This replaces the WinForms
/// OpenTK control for mesh inspection; model animation/rig playback is separate.
/// </summary>
public sealed class StudioMeshPreviewView : GLSurfaceView
{
    private readonly MeshRenderer renderer;

    public StudioMeshPreviewView(Context context, Mesh mesh) : base(context)
    {
        SetEGLContextClientVersion(2);
        renderer = new MeshRenderer(mesh);
        SetRenderer(renderer);
        SetRenderMode(GLSurfaceView.RenderModeWhenDirty);
    }

    public override bool OnTouchEvent(MotionEvent? e)
    {
        if (e == null) return false;
        switch (e.ActionMasked)
        {
            case MotionEventActions.Down:
                renderer.BeginTouch(e.GetX(), e.GetY());
                return true;
            case MotionEventActions.PointerDown:
                if (e.PointerCount >= 2)
                    renderer.BeginPinch(e.GetX(0), e.GetY(0), e.GetX(1), e.GetY(1));
                return true;
            case MotionEventActions.Move:
                if (e.PointerCount >= 2)
                    renderer.Pinch(e.GetX(0), e.GetY(0), e.GetX(1), e.GetY(1));
                else
                    renderer.Drag(e.GetX(), e.GetY());
                RequestRender();
                return true;
            case MotionEventActions.Up:
            case MotionEventActions.Cancel:
                renderer.EndTouch();
                return true;
        }
        return true;
    }

    private sealed class MeshRenderer : Java.Lang.Object, GLSurfaceView.IRenderer
    {
        private const int FloatsPerVertex = 6;
        private readonly float[] data;
        private readonly string? geometryError;
        private int program;
        private int positionLocation;
        private int normalLocation;
        private int mvpLocation;
        private int vertexBuffer;
        private int vertexCount;
        private int width = 1;
        private int height = 1;
        private float yaw = 25f;
        private float pitch = -12f;
        private float zoom = 3.2f;
        private float lastX, lastY;
        private float pinchStartDistance;
        private float pinchStartZoom;
        private readonly float[] projection = new float[16];
        private readonly float[] view = new float[16];
        private readonly float[] model = new float[16];
        private readonly float[] viewModel = new float[16];
        private readonly float[] mvp = new float[16];

        public MeshRenderer(Mesh mesh)
        {
            try
            {
                data = BuildTriangleData(mesh, out vertexCount);
                if (vertexCount == 0) geometryError = "Mesh không có triangle hợp lệ để hiển thị.";
            }
            catch (Exception ex)
            {
                data = Array.Empty<float>();
                geometryError = ex.Message;
            }
        }

        public void BeginTouch(float x, float y)
        {
            lastX = x;
            lastY = y;
        }

        public void Drag(float x, float y)
        {
            yaw += (x - lastX) * 0.55f;
            pitch = Math.Clamp(pitch + (y - lastY) * 0.45f, -88f, 88f);
            lastX = x;
            lastY = y;
        }

        public void BeginPinch(float x1, float y1, float x2, float y2)
        {
            pinchStartDistance = Distance(x1, y1, x2, y2);
            pinchStartZoom = zoom;
        }

        public void Pinch(float x1, float y1, float x2, float y2)
        {
            float current = Distance(x1, y1, x2, y2);
            if (pinchStartDistance > 1f)
                zoom = Math.Clamp(pinchStartZoom * pinchStartDistance / Math.Max(1f, current), 1.2f, 12f);
        }

        public void EndTouch() { }

        public void OnSurfaceCreated(IGL10? gl, EGLConfig? config)
        {
            GLES20.GlClearColor(0.075f, 0.09f, 0.12f, 1f);
            GLES20.GlEnable(GLES20.GlDepthTest);
            GLES20.GlEnable(GLES20.GlCullFace);
            if (vertexCount == 0) return;

            const string vertexShader = @"
                attribute vec3 aPosition;
                attribute vec3 aNormal;
                uniform mat4 uMvp;
                varying vec3 vNormal;
                void main() {
                    gl_Position = uMvp * vec4(aPosition, 1.0);
                    vNormal = aNormal;
                }";

            const string fragmentShader = @"
                precision mediump float;
                varying vec3 vNormal;
                void main() {
                    vec3 n = normalize(vNormal);
                    vec3 lightDir = normalize(vec3(-0.35, 0.65, 0.8));
                    float diffuse = max(dot(n, lightDir), 0.18);
                    float rim = pow(1.0 - max(dot(n, vec3(0.0, 0.0, 1.0)), 0.0), 2.0) * 0.12;
                    vec3 baseColor = vec3(0.54, 0.68, 0.84);
                    gl_FragColor = vec4(baseColor * diffuse + rim, 1.0);
                }";

            int vs = CompileShader(GLES20.GlVertexShader, vertexShader);
            int fs = CompileShader(GLES20.GlFragmentShader, fragmentShader);
            program = GLES20.GlCreateProgram();
            GLES20.GlAttachShader(program, vs);
            GLES20.GlAttachShader(program, fs);
            GLES20.GlLinkProgram(program);
            GLES20.GlDeleteShader(vs);
            GLES20.GlDeleteShader(fs);

            var status = new int[1];
            GLES20.GlGetProgramiv(program, GLES20.GlLinkStatus, status, 0);
            if (status[0] == 0)
            {
                string error = GLES20.GlGetProgramInfoLog(program) ?? "OpenGL program link failed.";
                GLES20.GlDeleteProgram(program);
                program = 0;
                throw new InvalidOperationException(error);
            }

            positionLocation = GLES20.GlGetAttribLocation(program, "aPosition");
            normalLocation = GLES20.GlGetAttribLocation(program, "aNormal");
            mvpLocation = GLES20.GlGetUniformLocation(program, "uMvp");

            var buffers = new int[1];
            GLES20.GlGenBuffers(1, buffers, 0);
            vertexBuffer = buffers[0];
            var bytes = ByteBuffer.AllocateDirect(data.Length * sizeof(float));
            bytes.Order(ByteOrder.NativeOrder());
            var floats = bytes.AsFloatBuffer();
            floats.Put(data);
            floats.Position(0);
            GLES20.GlBindBuffer(GLES20.GlArrayBuffer, vertexBuffer);
            GLES20.GlBufferData(GLES20.GlArrayBuffer, data.Length * sizeof(float), floats, GLES20.GlStaticDraw);
            GLES20.GlBindBuffer(GLES20.GlArrayBuffer, 0);
        }

        public void OnSurfaceChanged(IGL10? gl, int width, int height)
        {
            this.width = Math.Max(1, width);
            this.height = Math.Max(1, height);
            GLES20.GlViewport(0, 0, this.width, this.height);
            Matrix.PerspectiveM(projection, 0, 42f, (float)this.width / this.height, 0.05f, 100f);
        }

        public void OnDrawFrame(IGL10? gl)
        {
            GLES20.GlClear(GLES20.GlColorBufferBit | GLES20.GlDepthBufferBit);
            if (vertexCount == 0 || program == 0 || vertexBuffer == 0) return;

            Matrix.SetLookAtM(view, 0, 0f, 0f, zoom, 0f, 0f, 0f, 0f, 1f, 0f);
            Matrix.SetIdentityM(model, 0);
            Matrix.RotateM(model, 0, yaw, 0f, 1f, 0f);
            Matrix.RotateM(model, 0, pitch, 1f, 0f, 0f);
            Matrix.MultiplyMM(viewModel, 0, view, 0, model, 0);
            Matrix.MultiplyMM(mvp, 0, projection, 0, viewModel, 0);

            GLES20.GlUseProgram(program);
            GLES20.GlUniformMatrix4fv(mvpLocation, 1, false, mvp, 0);
            GLES20.GlBindBuffer(GLES20.GlArrayBuffer, vertexBuffer);
            GLES20.GlEnableVertexAttribArray(positionLocation);
            GLES20.GlEnableVertexAttribArray(normalLocation);
            GLES20.GlVertexAttribPointer(positionLocation, 3, GLES20.GlFloat, false, FloatsPerVertex * sizeof(float), 0);
            GLES20.GlVertexAttribPointer(normalLocation, 3, GLES20.GlFloat, false, FloatsPerVertex * sizeof(float), 3 * sizeof(float));
            GLES20.GlDrawArrays(GLES20.GlTriangles, 0, vertexCount);
            GLES20.GlDisableVertexAttribArray(positionLocation);
            GLES20.GlDisableVertexAttribArray(normalLocation);
            GLES20.GlBindBuffer(GLES20.GlArrayBuffer, 0);
            GLES20.GlUseProgram(0);
        }

        private static int CompileShader(int type, string source)
        {
            int shader = GLES20.GlCreateShader(type);
            GLES20.GlShaderSource(shader, source);
            GLES20.GlCompileShader(shader);
            var status = new int[1];
            GLES20.GlGetShaderiv(shader, GLES20.GlCompileStatus, status, 0);
            if (status[0] == 0)
            {
                string error = GLES20.GlGetShaderInfoLog(shader) ?? "OpenGL shader compilation failed.";
                GLES20.GlDeleteShader(shader);
                throw new InvalidOperationException(error);
            }
            return shader;
        }

        private static float[] BuildTriangleData(Mesh mesh, out int count)
        {
            count = 0;
            if (mesh.m_VertexCount <= 0 || mesh.m_Vertices == null || mesh.m_Vertices.Length == 0)
                return Array.Empty<float>();

            int vertexCount = mesh.m_VertexCount;
            int positionStride = mesh.m_Vertices.Length >= vertexCount * 4 && mesh.m_Vertices.Length % vertexCount == 0
                ? mesh.m_Vertices.Length / vertexCount : 3;
            if (positionStride < 3 || positionStride > 4)
                positionStride = 3;

            var indices = mesh.m_Indices;
            int indexCount = indices?.Count ?? 0;
            if (indexCount < 3)
                indexCount = vertexCount - vertexCount % 3;
            int triangleCount = Math.Min(indexCount / 3, 300_000);
            if (triangleCount <= 0) return Array.Empty<float>();

            bool hasNormals = mesh.m_Normals != null &&
                (mesh.m_Normals.Length == vertexCount * 3 || mesh.m_Normals.Length == vertexCount * 4);
            int normalStride = hasNormals && mesh.m_Normals!.Length == vertexCount * 4 ? 4 : 3;
            var positions = mesh.m_Vertices;
            var output = new float[checked(triangleCount * 3 * FloatsPerVertex)];

            float minX = float.PositiveInfinity, minY = float.PositiveInfinity, minZ = float.PositiveInfinity;
            float maxX = float.NegativeInfinity, maxY = float.NegativeInfinity, maxZ = float.NegativeInfinity;
            for (int i = 0; i < vertexCount; i++)
            {
                float x = positions[i * positionStride], y = positions[i * positionStride + 1], z = positions[i * positionStride + 2];
                if (!float.IsFinite(x) || !float.IsFinite(y) || !float.IsFinite(z)) continue;
                minX = Math.Min(minX, x); minY = Math.Min(minY, y); minZ = Math.Min(minZ, z);
                maxX = Math.Max(maxX, x); maxY = Math.Max(maxY, y); maxZ = Math.Max(maxZ, z);
            }
            if (!float.IsFinite(minX)) return Array.Empty<float>();

            float cx = (minX + maxX) * 0.5f;
            float cy = (minY + maxY) * 0.5f;
            float cz = (minZ + maxZ) * 0.5f;
            float extent = Math.Max(maxX - minX, Math.Max(maxY - minY, maxZ - minZ));
            float scale = extent > 0.000001f ? 2.0f / extent : 1.0f;

            int outIndex = 0;
            for (int tri = 0; tri < triangleCount; tri++)
            {
                uint i0 = indices != null && indices.Count >= 3 ? indices[tri * 3] : (uint)(tri * 3);
                uint i1 = indices != null && indices.Count >= 3 ? indices[tri * 3 + 1] : (uint)(tri * 3 + 1);
                uint i2 = indices != null && indices.Count >= 3 ? indices[tri * 3 + 2] : (uint)(tri * 3 + 2);
                if (i0 >= vertexCount || i1 >= vertexCount || i2 >= vertexCount) continue;

                ReadPosition(positions, positionStride, (int)i0, cx, cy, cz, scale, out float x0, out float y0, out float z0);
                ReadPosition(positions, positionStride, (int)i1, cx, cy, cz, scale, out float x1, out float y1, out float z1);
                ReadPosition(positions, positionStride, (int)i2, cx, cy, cz, scale, out float x2, out float y2, out float z2);

                float nx, ny, nz;
                if (hasNormals)
                {
                    nx = mesh.m_Normals![(int)i0 * normalStride];
                    ny = mesh.m_Normals[(int)i0 * normalStride + 1];
                    nz = mesh.m_Normals[(int)i0 * normalStride + 2];
                    Normalize(ref nx, ref ny, ref nz);
                }
                else
                {
                    nx = (y1 - y0) * (z2 - z0) - (z1 - z0) * (y2 - y0);
                    ny = (z1 - z0) * (x2 - x0) - (x1 - x0) * (z2 - z0);
                    nz = (x1 - x0) * (y2 - y0) - (y1 - y0) * (x2 - x0);
                    Normalize(ref nx, ref ny, ref nz);
                }

                WriteVertex(output, ref outIndex, x0, y0, z0, nx, ny, nz);
                WriteVertex(output, ref outIndex, x1, y1, z1, nx, ny, nz);
                WriteVertex(output, ref outIndex, x2, y2, z2, nx, ny, nz);
            }

            count = outIndex / FloatsPerVertex;
            if (count == 0) return Array.Empty<float>();
            if (count == triangleCount * 3) return output;
            var trimmed = new float[outIndex];
            Array.Copy(output, trimmed, outIndex);
            return trimmed;
        }

        private static void ReadPosition(float[] values, int stride, int index, float cx, float cy, float cz, float scale,
            out float x, out float y, out float z)
        {
            x = (values[index * stride] - cx) * scale;
            y = (values[index * stride + 1] - cy) * scale;
            z = (values[index * stride + 2] - cz) * scale;
        }

        private static void WriteVertex(float[] output, ref int index, float x, float y, float z, float nx, float ny, float nz)
        {
            output[index++] = x; output[index++] = y; output[index++] = z;
            output[index++] = nx; output[index++] = ny; output[index++] = nz;
        }

        private static void Normalize(ref float x, ref float y, ref float z)
        {
            float length = MathF.Sqrt(x * x + y * y + z * z);
            if (length < 0.000001f) { x = 0; y = 1; z = 0; return; }
            x /= length; y /= length; z /= length;
        }

        private static float Distance(float x1, float y1, float x2, float y2)
        {
            float dx = x2 - x1, dy = y2 - y1;
            return MathF.Sqrt(dx * dx + dy * dy);
        }
    }
}
