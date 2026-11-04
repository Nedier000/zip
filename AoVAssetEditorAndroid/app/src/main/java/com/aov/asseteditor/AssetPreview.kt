package com.aov.asseteditor

import android.content.Context
import android.opengl.GLES30
import android.opengl.GLSurfaceView
import android.opengl.Matrix
import android.view.MotionEvent
import android.view.ScaleGestureDetector
import java.io.BufferedReader
import java.io.File
import java.io.FileReader
import java.nio.ByteBuffer
import java.nio.ByteOrder
import java.nio.FloatBuffer
import javax.microedition.khronos.egl.EGLConfig
import javax.microedition.khronos.opengles.GL10
import kotlin.math.max
import kotlin.math.min
import kotlin.math.sqrt

internal data class ObjModel(val interleaved: FloatArray, val truncated: Boolean) {
    val vertexCount: Int get() = interleaved.size / 8

    companion object {
        private data class Corner(val p: Int, val uv: Int, val n: Int)
        private class FloatGrow(initial: Int = 4096) {
            var a = FloatArray(initial); var size = 0
            fun add(v: Float) { if (size == a.size) a = a.copyOf(a.size * 2); a[size++] = if (v.isFinite()) v else 0f }
            fun toArray() = a.copyOf(size)
        }
        private fun index(raw: String, size: Int): Int {
            val v = raw.toIntOrNull() ?: return -1
            return if (v > 0) v - 1 else if (v < 0) size + v else -1
        }
        private fun parseCorner(token: String, pc: Int, uc: Int, nc: Int): Corner {
            val f = token.split('/')
            return Corner(index(f.getOrElse(0) { "" }, pc), index(f.getOrElse(1) { "" }, uc), index(f.getOrElse(2) { "" }, nc))
        }
        fun fromObj(file: File, maxTriangles: Int = 250_000): ObjModel {
            val positions = ArrayList<FloatArray>()
            val uvs = ArrayList<FloatArray>()
            val normals = ArrayList<FloatArray>()
            BufferedReader(FileReader(file)).use { r ->
                var line: String?
                while (true) {
                    line = r.readLine() ?: break
                    val p = line.trim().split(Regex("\\s+"))
                    if (p.size < 2) continue
                    when (p[0]) {
                        "v" -> if (p.size >= 4) positions.add(floatArrayOf(p[1].toFloatOrNull() ?: 0f, p[2].toFloatOrNull() ?: 0f, p[3].toFloatOrNull() ?: 0f))
                        "vt" -> if (p.size >= 3) uvs.add(floatArrayOf(p[1].toFloatOrNull() ?: 0f, p[2].toFloatOrNull() ?: 0f))
                        "vn" -> if (p.size >= 4) normals.add(floatArrayOf(p[1].toFloatOrNull() ?: 0f, p[2].toFloatOrNull() ?: 0f, p[3].toFloatOrNull() ?: 0f))
                    }
                }
            }
            if (positions.isEmpty()) throw IllegalArgumentException("OBJ không có vertex; Mesh này chưa được parser giải mã đầy đủ.")
            var minX=Float.POSITIVE_INFINITY; var minY=minX; var minZ=minX
            var maxX=Float.NEGATIVE_INFINITY; var maxY=maxX; var maxZ=maxX
            for (v in positions) {
                minX=min(minX,v[0]); minY=min(minY,v[1]); minZ=min(minZ,v[2])
                maxX=max(maxX,v[0]); maxY=max(maxY,v[1]); maxZ=max(maxZ,v[2])
            }
            val cx=(minX+maxX)*0.5f; val cy=(minY+maxY)*0.5f; val cz=(minZ+maxZ)*0.5f
            val extent=max(max(maxX-minX,maxY-minY),maxZ-minZ).coerceAtLeast(0.0001f)
            val scale=1.8f/extent
            val out=FloatGrow(4096); var triCount=0; var truncated=false
            BufferedReader(FileReader(file)).use { r ->
                var line: String?
                while (true) {
                    line=r.readLine() ?: break
                    val p=line.trim().split(Regex("\\s+"))
                    if (p.size < 4 || p[0] != "f") continue
                    val corners=p.drop(1).map { parseCorner(it,positions.size,uvs.size,normals.size) }
                    if (corners.size < 3) continue
                    for (k in 1 until corners.size-1) {
                        if (triCount >= maxTriangles) { truncated=true; break }
                        val cs=listOf(corners[0],corners[k],corners[k+1])
                        if (cs.any { it.p !in positions.indices }) continue
                        val a=positions[cs[0].p]; val b=positions[cs[1].p]; val c=positions[cs[2].p]
                        val ux=b[0]-a[0]; val uy=b[1]-a[1]; val uz=b[2]-a[2]
                        val vx=c[0]-a[0]; val vy=c[1]-a[1]; val vz=c[2]-a[2]
                        var nx=uy*vz-uz*vy; var ny=uz*vx-ux*vz; var nz=ux*vy-uy*vx
                        var nl=sqrt(nx*nx+ny*ny+nz*nz).coerceAtLeast(1e-8f); nx/=nl; ny/=nl; nz/=nl
                        for (corner in cs) {
                            val v=positions[corner.p]
                            out.add((v[0]-cx)*scale); out.add((v[1]-cy)*scale); out.add((v[2]-cz)*scale)
                            val n=if (corner.n in normals.indices) normals[corner.n] else floatArrayOf(nx,ny,nz)
                            out.add(n[0]); out.add(n[1]); out.add(n[2])
                            val uv=if(corner.uv in uvs.indices) uvs[corner.uv] else floatArrayOf(0f,0f)
                            out.add(uv[0]); out.add(uv[1])
                        }
                        triCount++
                    }
                    if (truncated) break
                }
            }
            if (triCount == 0) throw IllegalArgumentException("OBJ không có mặt tam giác để preview.")
            return ObjModel(out.toArray(),truncated)
        }
    }
}

internal data class PreviewTexture(val mode:String, val path:String, val width:Int, val height:Int, val glFormat:String="")

internal class MeshPreviewView(context: Context, model: ObjModel, texture: PreviewTexture?, tint: FloatArray, statusCallback:(String)->Unit) : GLSurfaceView(context) {
    private val meshRenderer = MeshRenderer(model,texture,tint,statusCallback)
    private var lastX=0f; private var lastY=0f
    private val scaleDetector = ScaleGestureDetector(context, object: ScaleGestureDetector.SimpleOnScaleGestureListener() {
        override fun onScale(detector: ScaleGestureDetector): Boolean { meshRenderer.zoom = (meshRenderer.zoom / detector.scaleFactor).coerceIn(1.5f, 8f); requestRender(); return true }
    })
    init { setEGLContextClientVersion(3); setRenderer(meshRenderer); renderMode=RENDERMODE_WHEN_DIRTY; setOnTouchListener { _, event ->
        scaleDetector.onTouchEvent(event)
        when(event.actionMasked) {
            MotionEvent.ACTION_DOWN -> { lastX=event.x; lastY=event.y; true }
            MotionEvent.ACTION_MOVE -> { if(event.pointerCount==1 && !scaleDetector.isInProgress) { meshRenderer.rotY += (event.x-lastX)*0.7f; meshRenderer.rotX += (event.y-lastY)*0.7f; lastX=event.x; lastY=event.y; requestRender() }; true }
            else -> true
        }
    } }
}

private class MeshRenderer(private val model: ObjModel, private val texture:PreviewTexture?, tintValues:FloatArray, private val statusCallback:(String)->Unit) : GLSurfaceView.Renderer {
    @Volatile var rotX=0f; @Volatile var rotY=0f; @Volatile var zoom=3.4f
    private var program=0; private var count=0; private lateinit var buf: FloatBuffer; private var textureId=0; private var hasTexture=0
    private val tint=FloatArray(4) { i -> tintValues.getOrElse(i){1f} }
    private val projection=FloatArray(16); private val view=FloatArray(16); private val modelM=FloatArray(16); private val vp=FloatArray(16); private val mvp=FloatArray(16)
    override fun onSurfaceCreated(gl: GL10?, config: EGLConfig?) {
        GLES30.glClearColor(0.055f,0.067f,0.085f,1f); GLES30.glEnable(GLES30.GL_DEPTH_TEST); GLES30.glDisable(GLES30.GL_CULL_FACE)
        val vs="#version 300 es\nprecision highp float; in vec3 aPosition; in vec3 aNormal; in vec2 aUv; uniform mat4 uMvp; uniform mat4 uModel; out vec3 vNormal; out vec2 vUv; void main(){ gl_Position=uMvp*vec4(aPosition,1.0); vNormal=mat3(uModel)*aNormal; vUv=aUv; }"
        val fs="#version 300 es\nprecision mediump float; in vec3 vNormal; in vec2 vUv; uniform sampler2D uTex; uniform int uHasTexture; uniform vec4 uTint; out vec4 fragColor; void main(){ vec3 n=normalize(vNormal); float l=0.32+0.68*max(dot(n,normalize(vec3(-0.45,0.75,0.55))),0.0); vec4 base=(uHasTexture==1)?texture(uTex,vUv):vec4(0.62,0.70,0.81,1.0); base*=uTint; fragColor=vec4(base.rgb*l,base.a); }"
        program=link(vs,fs)
        val bb=ByteBuffer.allocateDirect(model.interleaved.size*4).order(ByteOrder.nativeOrder()); buf=bb.asFloatBuffer(); buf.put(model.interleaved); buf.position(0); count=model.vertexCount
        loadTexture()
    }
    private fun loadTexture() {
        val t=texture ?: run { statusCallback("Mesh preview geometry only: chưa tìm được texture/material liên kết."); return }
        try {
            val ids=IntArray(1); GLES30.glGenTextures(1,ids,0); textureId=ids[0]; GLES30.glBindTexture(GLES30.GL_TEXTURE_2D,textureId)
            GLES30.glTexParameteri(GLES30.GL_TEXTURE_2D,GLES30.GL_TEXTURE_MIN_FILTER,GLES30.GL_LINEAR); GLES30.glTexParameteri(GLES30.GL_TEXTURE_2D,GLES30.GL_TEXTURE_MAG_FILTER,GLES30.GL_LINEAR); GLES30.glTexParameteri(GLES30.GL_TEXTURE_2D,GLES30.GL_TEXTURE_WRAP_S,GLES30.GL_REPEAT); GLES30.glTexParameteri(GLES30.GL_TEXTURE_2D,GLES30.GL_TEXTURE_WRAP_T,GLES30.GL_REPEAT)
            if(t.mode=="png") {
                val bitmap=android.graphics.BitmapFactory.decodeFile(t.path) ?: throw IllegalStateException("Không mở được texture PNG")
                android.opengl.GLUtils.texImage2D(GLES30.GL_TEXTURE_2D,0,bitmap,0); bitmap.recycle()
            } else if(t.mode=="gpu-compressed") {
                val extensions=GLES30.glGetString(GLES30.GL_EXTENSIONS) ?: ""; val fmt=java.lang.Long.decode(t.glFormat).toInt()
                val astc=t.glFormat.lowercase(java.util.Locale.ROOT).startsWith("0x93b")
                val s3tc=t.glFormat=="0x83F0" || t.glFormat=="0x83F3"
                val pvrtc=t.glFormat.startsWith("0x8C"); val atc=t.glFormat=="0x8C92" || t.glFormat=="0x87EE"
                val bc=t.glFormat.startsWith("0x8DB") || t.glFormat=="0x8E8F" || t.glFormat=="0x8E8C"
                val supported=when { astc -> extensions.contains("texture_compression_astc") || extensions.contains("ASTC"); s3tc -> extensions.contains("texture_compression_s3tc") || extensions.contains("texture_compression_dxt"); pvrtc -> extensions.contains("texture_compression_pvrtc"); atc -> extensions.contains("texture_compression_atc"); bc -> extensions.contains("texture_compression_bptc") || extensions.contains("texture_compression_rgtc"); else -> true }
                if(!supported) throw IllegalStateException("GPU không hỗ trợ ${t.glFormat}; vẫn preview được geometry nhưng không áp texture.")
                val bytes=File(t.path).readBytes(); val data=ByteBuffer.allocateDirect(bytes.size).order(ByteOrder.nativeOrder()); data.put(bytes); data.position(0)
                GLES30.glCompressedTexImage2D(GLES30.GL_TEXTURE_2D,0,fmt,t.width,t.height,0,bytes.size,data)
                val err=GLES30.glGetError(); if(err!=GLES30.GL_NO_ERROR) throw IllegalStateException("GPU từ chối texture (GL error $err).")
            } else throw IllegalStateException("Texture format chưa có decoder/preview GPU")
            hasTexture=1; statusCallback("Đã nạp texture thật ${t.width}×${t.height} cho UV của Mesh.")
        } catch(e:Exception) { hasTexture=0; statusCallback("Chỉ preview geometry: ${e.message}") }
    }
    override fun onSurfaceChanged(gl: GL10?, width: Int, height: Int) { GLES30.glViewport(0,0,width,height); Matrix.perspectiveM(projection,0,45f,width.toFloat()/max(1,height),0.1f,40f) }
    override fun onDrawFrame(gl: GL10?) {
        GLES30.glClear(GLES30.GL_COLOR_BUFFER_BIT or GLES30.GL_DEPTH_BUFFER_BIT); if (program==0 || count==0) return
        Matrix.setLookAtM(view,0,0f,0f,zoom,0f,0f,0f,0f,1f,0f)
        Matrix.setIdentityM(modelM,0); Matrix.rotateM(modelM,0,rotX,1f,0f,0f); Matrix.rotateM(modelM,0,rotY,0f,1f,0f)
        Matrix.multiplyMM(vp,0,projection,0,view,0); Matrix.multiplyMM(mvp,0,vp,0,modelM,0)
        GLES30.glUseProgram(program)
        GLES30.glUniformMatrix4fv(GLES30.glGetUniformLocation(program,"uMvp"),1,false,mvp,0); GLES30.glUniformMatrix4fv(GLES30.glGetUniformLocation(program,"uModel"),1,false,modelM,0)
        GLES30.glUniform4fv(GLES30.glGetUniformLocation(program,"uTint"),1,tint,0); GLES30.glUniform1i(GLES30.glGetUniformLocation(program,"uHasTexture"),hasTexture); GLES30.glUniform1i(GLES30.glGetUniformLocation(program,"uTex"),0)
        if(hasTexture==1) { GLES30.glEnable(GLES30.GL_BLEND); GLES30.glBlendFunc(GLES30.GL_SRC_ALPHA,GLES30.GL_ONE_MINUS_SRC_ALPHA); GLES30.glActiveTexture(GLES30.GL_TEXTURE0); GLES30.glBindTexture(GLES30.GL_TEXTURE_2D,textureId) }
        val p=GLES30.glGetAttribLocation(program,"aPosition"); val n=GLES30.glGetAttribLocation(program,"aNormal"); val uv=GLES30.glGetAttribLocation(program,"aUv")
        buf.position(0); GLES30.glEnableVertexAttribArray(p); GLES30.glVertexAttribPointer(p,3,GLES30.GL_FLOAT,false,32,buf)
        buf.position(3); GLES30.glEnableVertexAttribArray(n); GLES30.glVertexAttribPointer(n,3,GLES30.GL_FLOAT,false,32,buf)
        buf.position(6); GLES30.glEnableVertexAttribArray(uv); GLES30.glVertexAttribPointer(uv,2,GLES30.GL_FLOAT,false,32,buf); buf.position(0)
        GLES30.glDrawArrays(GLES30.GL_TRIANGLES,0,count)
        GLES30.glDisableVertexAttribArray(p); GLES30.glDisableVertexAttribArray(n); GLES30.glDisableVertexAttribArray(uv); GLES30.glDisable(GLES30.GL_BLEND)
    }
    private fun link(vs:String, fs:String):Int { val v=compile(GLES30.GL_VERTEX_SHADER,vs); val f=compile(GLES30.GL_FRAGMENT_SHADER,fs); val p=GLES30.glCreateProgram(); GLES30.glAttachShader(p,v); GLES30.glAttachShader(p,f); GLES30.glLinkProgram(p); val ok=IntArray(1); GLES30.glGetProgramiv(p,GLES30.GL_LINK_STATUS,ok,0); if(ok[0]==0) throw IllegalStateException("GL link: "+GLES30.glGetProgramInfoLog(p)); GLES30.glDeleteShader(v); GLES30.glDeleteShader(f); return p }
    private fun compile(type:Int,src:String):Int { val s=GLES30.glCreateShader(type); GLES30.glShaderSource(s,src); GLES30.glCompileShader(s); val ok=IntArray(1); GLES30.glGetShaderiv(s,GLES30.GL_COMPILE_STATUS,ok,0); if(ok[0]==0) throw IllegalStateException("GL shader: "+GLES30.glGetShaderInfoLog(s)); return s }
}

internal class TexturePreviewView(context: Context, private val payload: File, private val width: Int, private val height: Int, private val glFormat: String, private val statusCallback: (String)->Unit) : GLSurfaceView(context) {
    init { setEGLContextClientVersion(3); setRenderer(TextureRenderer(payload,width,height,glFormat,statusCallback)); renderMode=RENDERMODE_WHEN_DIRTY }
}

private class TextureRenderer(private val file:File,private val width:Int,private val height:Int,private val formatHex:String,private val report:(String)->Unit):GLSurfaceView.Renderer {
    private var program=0; private var texture=0; private var ready=false; private var reported=false
    private val coords=floatArrayOf(-1f,-1f,0f,1f, 1f,-1f,1f,1f, -1f,1f,0f,0f, 1f,1f,1f,0f)
    private lateinit var buffer:FloatBuffer
    override fun onSurfaceCreated(gl:GL10?,config:EGLConfig?) {
        GLES30.glClearColor(0.05f,0.06f,0.08f,1f)
        val vs="#version 300 es\nprecision highp float; in vec2 aPosition; in vec2 aUv; out vec2 vUv; void main(){gl_Position=vec4(aPosition,0.0,1.0);vUv=aUv;}"
        val fs="#version 300 es\nprecision mediump float; in vec2 vUv; uniform sampler2D uTex; out vec4 fragColor; void main(){fragColor=texture(uTex,vUv);}"
        program=link(vs,fs); val bb=ByteBuffer.allocateDirect(coords.size*4).order(ByteOrder.nativeOrder()); buffer=bb.asFloatBuffer(); buffer.put(coords); buffer.position(0)
        try {
            val extensions=GLES30.glGetString(GLES30.GL_EXTENSIONS) ?: ""
            val fmt=java.lang.Long.decode(formatHex).toInt()
            val astc=formatHex.lowercase(Locale.ROOT).startsWith("0x93b")
            val s3tc=formatHex=="0x83F0" || formatHex=="0x83F3"
            val pvrtc=formatHex.startsWith("0x8C")
            val atc=formatHex=="0x8C92" || formatHex=="0x87EE"
            val bc=formatHex.startsWith("0x8DB") || formatHex=="0x8E8F" || formatHex=="0x8E8C"
            val supported= when { astc -> extensions.contains("texture_compression_astc") || extensions.contains("ASTC"); s3tc -> extensions.contains("texture_compression_s3tc") || extensions.contains("texture_compression_dxt"); pvrtc -> extensions.contains("texture_compression_pvrtc"); atc -> extensions.contains("texture_compression_atc"); bc -> extensions.contains("texture_compression_bptc") || extensions.contains("texture_compression_rgtc"); else -> true }
            if (!supported) throw IllegalStateException("GPU không công bố extension cho format $formatHex. Texture gốc vẫn được bảo toàn; preview này không thể giải mã bằng CPU trong bản hiện tại.")
            val bytes=file.readBytes(); val data=ByteBuffer.allocateDirect(bytes.size).order(ByteOrder.nativeOrder()); data.put(bytes); data.position(0)
            val ids=IntArray(1); GLES30.glGenTextures(1,ids,0); texture=ids[0]; GLES30.glBindTexture(GLES30.GL_TEXTURE_2D,texture)
            GLES30.glTexParameteri(GLES30.GL_TEXTURE_2D,GLES30.GL_TEXTURE_MIN_FILTER,GLES30.GL_LINEAR); GLES30.glTexParameteri(GLES30.GL_TEXTURE_2D,GLES30.GL_TEXTURE_MAG_FILTER,GLES30.GL_LINEAR); GLES30.glTexParameteri(GLES30.GL_TEXTURE_2D,GLES30.GL_TEXTURE_WRAP_S,GLES30.GL_CLAMP_TO_EDGE); GLES30.glTexParameteri(GLES30.GL_TEXTURE_2D,GLES30.GL_TEXTURE_WRAP_T,GLES30.GL_CLAMP_TO_EDGE)
            GLES30.glCompressedTexImage2D(GLES30.GL_TEXTURE_2D,0,fmt,width,height,0,bytes.size,data)
            val err=GLES30.glGetError(); if(err!=GLES30.GL_NO_ERROR) throw IllegalStateException("GPU từ chối texture format $formatHex (GL error $err).")
            ready=true; send("Preview texture nén trực tiếp trên GPU · $width×$height · $formatHex")
        } catch(e:Exception) { ready=false; send("Không preview được: ${e.message}") }
    }
    override fun onSurfaceChanged(gl:GL10?,width:Int,height:Int) { GLES30.glViewport(0,0,width,height) }
    override fun onDrawFrame(gl:GL10?) {
        GLES30.glClear(GLES30.GL_COLOR_BUFFER_BIT or GLES30.GL_DEPTH_BUFFER_BIT); if(!ready || program==0) return
        GLES30.glUseProgram(program); GLES30.glActiveTexture(GLES30.GL_TEXTURE0); GLES30.glBindTexture(GLES30.GL_TEXTURE_2D,texture); GLES30.glUniform1i(GLES30.glGetUniformLocation(program,"uTex"),0)
        val p=GLES30.glGetAttribLocation(program,"aPosition"); val u=GLES30.glGetAttribLocation(program,"aUv")
        buffer.position(0); GLES30.glEnableVertexAttribArray(p); GLES30.glVertexAttribPointer(p,2,GLES30.GL_FLOAT,false,16,buffer)
        buffer.position(2); GLES30.glEnableVertexAttribArray(u); GLES30.glVertexAttribPointer(u,2,GLES30.GL_FLOAT,false,16,buffer); buffer.position(0)
        GLES30.glDrawArrays(GLES30.GL_TRIANGLE_STRIP,0,4); GLES30.glDisableVertexAttribArray(p); GLES30.glDisableVertexAttribArray(u)
    }
    private fun send(message:String) { if(!reported) { reported=true; android.os.Handler(android.os.Looper.getMainLooper()).post { report(message) } } }
    private fun link(vs:String,fs:String):Int { val v=compile(GLES30.GL_VERTEX_SHADER,vs); val f=compile(GLES30.GL_FRAGMENT_SHADER,fs); val p=GLES30.glCreateProgram(); GLES30.glAttachShader(p,v); GLES30.glAttachShader(p,f); GLES30.glLinkProgram(p); val ok=IntArray(1); GLES30.glGetProgramiv(p,GLES30.GL_LINK_STATUS,ok,0); if(ok[0]==0) throw IllegalStateException(GLES30.glGetProgramInfoLog(p)); return p }
    private fun compile(type:Int,src:String):Int { val s=GLES30.glCreateShader(type); GLES30.glShaderSource(s,src); GLES30.glCompileShader(s); val ok=IntArray(1); GLES30.glGetShaderiv(s,GLES30.GL_COMPILE_STATUS,ok,0); if(ok[0]==0) throw IllegalStateException(GLES30.glGetShaderInfoLog(s)); return s }
}
