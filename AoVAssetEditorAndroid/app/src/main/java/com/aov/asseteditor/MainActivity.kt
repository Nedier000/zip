package com.aov.asseteditor

import android.app.Activity
import android.app.AlertDialog
import android.content.Intent
import android.graphics.BitmapFactory
import android.graphics.Color
import android.net.Uri
import android.opengl.GLSurfaceView
import android.os.Bundle
import android.text.Editable
import android.text.InputType
import android.text.TextWatcher
import android.view.Gravity
import android.view.View
import android.view.ViewGroup
import android.widget.*
import com.chaquo.python.Python
import com.chaquo.python.android.AndroidPlatform
import org.json.JSONArray
import org.json.JSONObject
import java.io.File
import java.util.Locale

class MainActivity : Activity() {
    private lateinit var status: TextView
    private lateinit var summary: TextView
    private lateinit var list: LinearLayout
    private lateinit var search: EditText
    private lateinit var tabs: LinearLayout
    private var selectedFile: File? = null
    private var session = ""
    private var bundleData: JSONObject? = null
    private var currentTab = "Materials"
    private var pendingExport: File? = null
    private var activeGl: GLSurfaceView? = null
    private val tabNames = listOf("Materials", "Textures", "Meshes", "Objects")

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        if (!Python.isStarted()) Python.start(AndroidPlatform(this))
        buildUi()
    }

    override fun onPause() { activeGl?.onPause(); super.onPause() }
    override fun onResume() { super.onResume(); activeGl?.onResume() }

    private fun buildUi() {
        val root = LinearLayout(this).apply { orientation=LinearLayout.VERTICAL; setPadding(dp(14),dp(14),dp(14),dp(8)); setBackgroundColor(Color.rgb(16,19,24)) }
        root.addView(TextView(this).apply { text="AoV / HOK Asset Editor"; textSize=24f; setTextColor(Color.WHITE); typeface=android.graphics.Typeface.DEFAULT_BOLD })
        root.addView(TextView(this).apply { text="Thao tác trên object Unity thật · preview GPU/mesh thật"; textSize=12.5f; setTextColor(Color.rgb(150,165,180)); setPadding(0,dp(4),0,dp(12)) })
        root.addView(button("Mở AssetBundle") { openBundle() })
        status = TextView(this).apply { text="Chưa mở file"; setTextColor(Color.rgb(220,225,230)); setPadding(0,dp(10),0,dp(4)); textSize=13f }
        summary = TextView(this).apply { setTextColor(Color.rgb(120,220,232)); setPadding(0,dp(2),0,dp(8)); textSize=12f }
        root.addView(status); root.addView(summary)
        search = EditText(this).apply {
            hint="Tìm tên object hoặc PathID"; setSingleLine(true); textSize=14f
            setTextColor(Color.WHITE); setHintTextColor(Color.GRAY)
            setPadding(dp(10),dp(4),dp(10),dp(4)); setBackgroundColor(Color.rgb(33,39,48))
            addTextChangedListener(object: TextWatcher {
                override fun beforeTextChanged(s: CharSequence?, start: Int, count: Int, after: Int) {}
                override fun onTextChanged(s: CharSequence?, start: Int, before: Int, count: Int) { renderList() }
                override fun afterTextChanged(s: Editable?) {}
            })
        }
        root.addView(search, LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT,dp(46)))
        val tabScroll=HorizontalScrollView(this).apply { isHorizontalScrollBarEnabled=false }
        tabs=LinearLayout(this).apply { orientation=LinearLayout.HORIZONTAL; setPadding(0,dp(8),0,dp(6)) }
        for (tab in tabNames) {
            val b=button(tab) { currentTab=tab; updateTabs(); renderList() }
            val lp=LinearLayout.LayoutParams(ViewGroup.LayoutParams.WRAP_CONTENT,dp(42)); lp.marginEnd=dp(6); tabs.addView(b,lp)
        }
        tabScroll.addView(tabs); root.addView(tabScroll)
        val listScroll=ScrollView(this)
        list=LinearLayout(this).apply { orientation=LinearLayout.VERTICAL }
        listScroll.addView(list)
        root.addView(listScroll,LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT,0,1f))
        setContentView(root)
        updateTabs()
    }

    private fun dp(v: Int): Int = (v * resources.displayMetrics.density + 0.5f).toInt()
    private fun button(label: String, action: () -> Unit) = Button(this).apply { text=label; setOnClickListener { action() } }

    private fun openBundle() {
        val i=Intent(Intent.ACTION_OPEN_DOCUMENT).apply { addCategory(Intent.CATEGORY_OPENABLE); type="*/*" }
        startActivityForResult(i,101)
    }

    @Deprecated("Used for Android Storage Access Framework result handling")
    override fun onActivityResult(requestCode: Int, resultCode: Int, data: Intent?) {
        super.onActivityResult(requestCode,resultCode,data)
        if (requestCode==101 && resultCode==Activity.RESULT_OK) {
            val uri=data?.data ?: return
            Thread {
                try {
                    val name=uri.lastPathSegment?.substringAfterLast('/')?.substringAfterLast(':')?.ifBlank { "input.assetbundle" } ?: "input.assetbundle"
                    val out=File(cacheDir,"input_${System.currentTimeMillis()}_${name.replace(Regex("[^A-Za-z0-9._-]"),"_")}")
                    contentResolver.openInputStream(uri).use { input -> out.outputStream().use { output -> (input ?: error("Không mở được file nguồn")).copyTo(output) } }
                    selectedFile=out
                    runOnUiThread { status.text="Đang đọc bundle: ${out.name}"; summary.text="Đang phân tích object/material/texture/mesh…" }
                    val py=Python.getInstance().getModule("asset_engine")
                    val result=JSONObject(py.callAttr("inspect_bundle",out.absolutePath).toString())
                    bundleData=result; session=result.optString("session")
                    runOnUiThread { showBundle(result) }
                } catch(e:Exception) { runOnUiThread { status.text="Không đọc được bundle: ${rootError(e)}"; summary.text="Kiểm tra bundle và log phụ thuộc parser." } }
            }.start()
        } else if (requestCode==102) {
            val generated=pendingExport; pendingExport=null
            if (resultCode==Activity.RESULT_OK && generated!=null) {
                val uri=data?.data
                if (uri==null) { status.text="Đã tạo file tạm: ${generated.absolutePath}"; return }
                Thread {
                    try {
                        contentResolver.openOutputStream(uri,"w").use { stream -> generated.inputStream().use { input -> (stream ?: error("Không mở được file đích")).use { output -> input.copyTo(output) } } }
                        runOnUiThread { status.text="Đã lưu file: ${generated.name}"; summary.text="Đã ghi lại và đọc kiểm tra bundle trước khi xuất."; Toast.makeText(this,"Lưu AssetBundle thành công",Toast.LENGTH_LONG).show() }
                    } catch(e:Exception) { runOnUiThread { status.text="Đã tạo bản sửa trong cache nhưng sao chép thất bại: ${e.message}" } }
                }.start()
            } else if(generated!=null) {
                status.text="Bản đã sửa được tạo trong cache: ${generated.absolutePath} (chưa chọn nơi lưu)"
            }
        }
    }

    private fun showBundle(data: JSONObject) {
        session=data.optString("session")
        val counts=data.optJSONObject("counts")
        status.text="Đã nạp: ${data.optString("source")}"
        summary.text="${data.optInt("objectCount")} objects · ${data.optInt("materialCount")} Materials · ${data.optInt("textureCount")} Textures · ${data.optInt("meshCount")} Meshes"
        currentTab="Materials"; updateTabs(); renderList()
        val notes=counts?.let { " · types ${it.length()}" } ?: ""
        status.append(notes)
    }

    private fun updateTabs() {
        for (i in 0 until tabs.childCount) {
            val b=tabs.getChildAt(i) as? Button ?: continue
            val active=b.text.toString()==currentTab
            b.setTextColor(if(active) Color.BLACK else Color.WHITE)
            b.setBackgroundColor(if(active) Color.rgb(120,220,232) else Color.rgb(37,44,54))
        }
    }

    private fun arrayForTab(data: JSONObject): JSONArray = when(currentTab) {
        "Materials" -> data.optJSONArray("materials") ?: JSONArray()
        "Textures" -> data.optJSONArray("textures") ?: JSONArray()
        "Meshes" -> data.optJSONArray("meshes") ?: JSONArray()
        else -> data.optJSONArray("objects") ?: JSONArray()
    }

    private fun renderList() {
        if (!::list.isInitialized) return
        list.removeAllViews()
        val data=bundleData ?: run { list.addView(TextView(this).apply { text="Mở AssetBundle để bắt đầu."; setTextColor(Color.LTGRAY); setPadding(dp(12),dp(16),dp(12),dp(16)) }); return }
        val rows=arrayForTab(data); val q=search.text?.toString()?.trim()?.lowercase(Locale.ROOT) ?: ""
        var shown=0; var matched=0
        for (i in 0 until rows.length()) {
            val item=rows.optJSONObject(i) ?: continue
            val name=item.optString("name"); val path=item.optString("pathId"); val type=item.optString("type")
            if(q.isNotEmpty() && !name.lowercase(Locale.ROOT).contains(q) && !path.contains(q) && !type.lowercase(Locale.ROOT).contains(q)) continue
            matched++
            if(shown>=450) continue
            val card=LinearLayout(this).apply { orientation=LinearLayout.VERTICAL; setPadding(dp(12),dp(10),dp(12),dp(10)); setBackgroundColor(Color.rgb(27,32,40)); isClickable=true; isFocusable=true }
            val title=TextView(this).apply { text=name.ifBlank { type.ifBlank { currentTab } }; setTextColor(Color.WHITE); textSize=14f; typeface=android.graphics.Typeface.DEFAULT_BOLD }
            card.addView(title)
            val details=when(currentTab) {
                "Materials" -> "PathID $path · ${item.optJSONArray("colors")?.length() ?: 0} color · ${item.optJSONArray("floats")?.length() ?: 0} float"
                "Textures" -> "PathID $path · ${item.optInt("width")}×${item.optInt("height")} · ${item.optString("format")}"
                "Meshes" -> "PathID $path · ${item.optInt("vertices")} vertices · ${item.optInt("triangles")} triangles"
                else -> "${item.optString("type")} · PathID $path"
            }
            card.addView(TextView(this).apply { text=details; textSize=11.5f; setTextColor(Color.rgb(162,177,192)); setPadding(0,dp(4),0,0) })
            val actionText=when(currentTab) { "Materials"->"Mở bảng material / chỉnh màu và float"; "Textures"->"Preview texture thật"; "Meshes"->"Preview 3D mesh thật"; else->"Xem thông tin object" }
            card.addView(TextView(this).apply { text=actionText; textSize=11f; setTextColor(Color.rgb(120,220,232)); setPadding(0,dp(5),0,0) })
            card.setOnClickListener {
                when(currentTab) {
                    "Materials" -> showMaterialEditor(item)
                    "Textures" -> previewTexture(item)
                    "Meshes" -> previewMesh(item)
                    else -> showObjectDetails(item)
                }
            }
            val lp=LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT,ViewGroup.LayoutParams.WRAP_CONTENT); lp.bottomMargin=dp(7); list.addView(card,lp)
            shown++
        }
        if(shown==0) list.addView(TextView(this).apply { text="Không tìm thấy object phù hợp."; setTextColor(Color.LTGRAY); setPadding(dp(12),dp(16),dp(12),dp(16)) })
        status.text="${data.optString("source")} · $currentTab · $matched kết quả" + if(matched>shown) " (hiển thị $shown đầu tiên)" else ""
    }

    private fun showMaterialEditor(mat: JSONObject) {
        val content=LinearLayout(this).apply { orientation=LinearLayout.VERTICAL; setPadding(dp(12),dp(8),dp(12),dp(8)) }
        val colorArray=mat.optJSONArray("colors") ?: JSONArray()
        val floatArray=mat.optJSONArray("floats") ?: JSONArray()
        content.addView(sectionHeader("Color properties · ${colorArray.length()}"))
        if(colorArray.length()==0) content.addView(note("Material này không có m_Colors đã parse được. Có thể màu hiển thị đến từ texture, shader hoặc dữ liệu particle; không tự giả lập thuộc tính màu không tồn tại."))
        for(i in 0 until colorArray.length()) {
            val c=colorArray.optJSONObject(i) ?: continue
            val row=LinearLayout(this).apply { orientation=LinearLayout.HORIZONTAL; gravity=Gravity.CENTER_VERTICAL; setPadding(dp(7),dp(6),dp(7),dp(6)); setBackgroundColor(Color.rgb(36,43,53)); isClickable=true }
            val swatch=View(this).apply { setBackgroundColor(toAndroidColor(c)) }
            row.addView(swatch,LinearLayout.LayoutParams(dp(46),dp(36)))
            row.addView(TextView(this).apply { text="${c.optString("key")}\n${fmt(c.optDouble("r"))}, ${fmt(c.optDouble("g"))}, ${fmt(c.optDouble("b"))}, ${fmt(c.optDouble("a"))}"; setTextColor(Color.WHITE); textSize=12f; setPadding(dp(10),0,0,0) },LinearLayout.LayoutParams(0,ViewGroup.LayoutParams.WRAP_CONTENT,1f))
            row.setOnClickListener { editColor(mat,c) }
            val lp=LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT,ViewGroup.LayoutParams.WRAP_CONTENT); lp.bottomMargin=dp(5); content.addView(row,lp)
        }
        content.addView(sectionHeader("Float properties · ${floatArray.length()}"))
        for(i in 0 until floatArray.length()) {
            val f=floatArray.optJSONObject(i) ?: continue
            val row=button("${f.optString("key")} = ${fmt(f.optDouble("value"))}") { editFloat(mat,f) }
            row.textSize=12f
            content.addView(row)
        }
        val tx=mat.optJSONArray("textures") ?: JSONArray()
        val textureList=bundleData?.optJSONArray("textures") ?: JSONArray()
        content.addView(sectionHeader("Texture references · ${tx.length()}"))
        for(i in 0 until tx.length()) {
            val t=tx.optJSONObject(i) ?: continue
            val tid=t.optString("texturePathId")
            val linked=(0 until textureList.length()).asSequence().mapNotNull { textureList.optJSONObject(it) }
                .firstOrNull { it.optString("pathId")==tid }
            if(linked!=null && tid!="0") {
                content.addView(button("Preview ${t.optString("key")} · ${linked.optString("name")} (${linked.optInt("width")}×${linked.optInt("height")})") { previewTexture(linked) })
            } else content.addView(note("${t.optString("key")} → Texture PathID $tid (không có texture local tương ứng)"))
        }
        val scroll=ScrollView(this).apply { addView(content) }
        AlertDialog.Builder(this).setTitle("${mat.optString("name")} · PathID ${mat.optString("pathId")}").setView(scroll).setPositiveButton("Đóng",null).show()
    }

    private fun sectionHeader(text:String)=TextView(this).apply { this.text=text; textSize=14f; typeface=android.graphics.Typeface.DEFAULT_BOLD; setTextColor(Color.rgb(120,220,232)); setPadding(0,dp(12),0,dp(6)) }
    private fun note(text:String)=TextView(this).apply { this.text=text; textSize=12f; setTextColor(Color.LTGRAY); setPadding(dp(8),dp(6),dp(8),dp(6)) }
    private fun fmt(v:Double)=String.format(Locale.US,"%.4f",v)
    private fun toAndroidColor(c:JSONObject):Int { fun channel(k:String)=((c.optDouble(k,1.0).coerceIn(0.0,1.0)*255).toInt()); return Color.argb(channel("a"),channel("r"),channel("g"),channel("b")) }

    private fun editColor(mat: JSONObject, c: JSONObject) {
        val panel=LinearLayout(this).apply { orientation=LinearLayout.VERTICAL; setPadding(dp(18),dp(8),dp(18),dp(8)) }
        val preview=View(this).apply { setBackgroundColor(toAndroidColor(c)) }
        panel.addView(preview,LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT,dp(48)))
        val fields=linkedMapOf<String,EditText>()
        for(k in listOf("r","g","b","a")) {
            panel.addView(TextView(this).apply { text=k.uppercase(Locale.ROOT); setTextColor(Color.DKGRAY); setPadding(0,dp(7),0,0) })
            val et=EditText(this).apply { setSingleLine(true); setText(c.optDouble(k,1.0).toString()); inputType=InputType.TYPE_CLASS_NUMBER or InputType.TYPE_NUMBER_FLAG_DECIMAL or InputType.TYPE_NUMBER_FLAG_SIGNED; setTextColor(Color.WHITE) }
            fields[k]=et; panel.addView(et)
            et.addTextChangedListener(object:TextWatcher { override fun beforeTextChanged(s:CharSequence?,start:Int,count:Int,after:Int){}; override fun onTextChanged(s:CharSequence?,start:Int,before:Int,count:Int){ val temp=JSONObject(); for(key in fields.keys) temp.put(key,fields[key]?.text?.toString()?.toDoubleOrNull()?:1.0); preview.setBackgroundColor(toAndroidColor(temp)) }; override fun afterTextChanged(s:Editable?){} })
        }
        AlertDialog.Builder(this).setTitle("Sửa ${c.optString("key")}").setView(panel).setNegativeButton("Hủy",null).setPositiveButton("Ghi thuộc tính") { _,_ ->
            try {
                val v= listOf("r","g","b","a").map { fields[it]!!.text.toString().toDouble() }
                writeColor(mat,c,v)
            } catch(e:Exception) { status.text="Giá trị màu không hợp lệ: ${e.message}" }
        }.show()
    }

    private fun editFloat(mat:JSONObject, f:JSONObject) {
        val input=EditText(this).apply { setSingleLine(true); setText(f.optDouble("value").toString()); inputType=InputType.TYPE_CLASS_NUMBER or InputType.TYPE_NUMBER_FLAG_DECIMAL or InputType.TYPE_NUMBER_FLAG_SIGNED; setTextColor(Color.WHITE) }
        AlertDialog.Builder(this).setTitle("Sửa float ${f.optString("key")}").setMessage("Tham số shader/material thật. Thay đổi có thể không được game dùng nếu shader không tham chiếu nó.").setView(input).setNegativeButton("Hủy",null).setPositiveButton("Ghi thuộc tính") { _,_ ->
            val value=input.text.toString().toDoubleOrNull()
            if(value==null) status.text="Float không hợp lệ" else writeFloat(mat,f,value)
        }.show()
    }

    private fun writeColor(mat:JSONObject,c:JSONObject,v:List<Double>) {
        runWrite("Ghi Material color ${c.optString("key")}", { output ->
            val py=Python.getInstance().getModule("asset_engine")
            JSONObject(py.callAttr("set_material_color",session,mat.optString("pathId"),c.optString("key"),v[0],v[1],v[2],v[3],output.absolutePath).toString())
        }) {
            c.put("r",v[0]); c.put("g",v[1]); c.put("b",v[2]); c.put("a",v[3])
            status.text="Màu mới đã ghi và đọc xác minh. Preview 3D có thể mở từ tab Meshes."
        }
    }
    private fun writeFloat(mat:JSONObject,f:JSONObject,value:Double) {
        runWrite("Ghi Material float ${f.optString("key")}", { output ->
            val py=Python.getInstance().getModule("asset_engine")
            JSONObject(py.callAttr("set_material_float",session,mat.optString("pathId"),f.optString("key"),value,output.absolutePath).toString())
        }) { f.put("value",value) }
    }
    private fun runWrite(label:String, work:(File)->JSONObject, onSaved:(JSONObject)->Unit = {}) {
        val src=selectedFile ?: run { status.text="Chưa mở bundle"; return }
        val out=File(cacheDir,src.nameWithoutExtension+"_edited_"+System.currentTimeMillis()+".assetbundle")
        status.text="$label…"; summary.text="Đang ghi bundle và kiểm tra đọc lại giá trị đã sửa."
        Thread {
            try {
                val r=work(out)
                runOnUiThread {
                    onSaved(r)
                    status.text="${r.optString("status")} · ${out.name}"
                    summary.text="${r.optInt("readbackObjects")} objects đọc lại · ${r.optLong("fileSize")} bytes"
                    pendingExport=out; requestSaveLocation(out.name)
                }
            } catch(e:Exception) { runOnUiThread { status.text="Ghi thất bại: ${rootError(e)}"; summary.text="File gốc không bị ghi đè."; out.delete() } }
        }.start()
    }

    private fun requestSaveLocation(name:String) {
        val i=Intent(Intent.ACTION_CREATE_DOCUMENT).apply { addCategory(Intent.CATEGORY_OPENABLE); type="application/octet-stream"; putExtra(Intent.EXTRA_TITLE,name) }
        try { startActivityForResult(i,102) } catch(e:Exception) { status.text="Bản đã sửa nằm trong cache: ${pendingExport?.absolutePath}" }
    }

    private fun previewMesh(item:JSONObject) {
        val id=item.optString("pathId"); status.text="Xuất geometry Mesh ${item.optString("name")}…"
        Thread {
            try {
                val dir=File(cacheDir,"mesh_preview"); val py=Python.getInstance().getModule("asset_engine")
                val result=JSONObject(py.callAttr("export_mesh_preview",session,id,dir.absolutePath).toString())
                val model=ObjModel.fromObj(File(result.getString("path")))
                val linked=result.optJSONObject("material")
                var textureSource: PreviewTexture?=null
                var textureNote="Không tìm thấy texture liên kết tự động."
                if(linked!=null) {
                    val textureId=linked.optString("texturePathId")
                    if(textureId.isNotBlank() && textureId!="0") {
                        try {
                            val tr=JSONObject(py.callAttr("export_texture_preview",session,textureId,File(cacheDir,"mesh_material_preview").absolutePath).toString())
                            val mode=tr.optString("mode")
                            if(mode=="png" || mode=="gpu-compressed") {
                                textureSource=PreviewTexture(mode,tr.optString("path"),tr.optInt("width"),tr.optInt("height"),tr.optString("glFormat"))
                                textureNote="Material ${linked.optString("materialName")} · ${linked.optString("textureKey")} · ${tr.optString("format")}"
                            } else textureNote="Material ${linked.optString("materialName")}: texture không preview được (${tr.optString("format")})."
                        } catch(texError:Exception) { textureNote="Không giải mã được texture liên kết: ${rootError(texError)}" }
                    } else textureNote="Material ${linked.optString("materialName")} không có texture liên kết khác PathID 0."
                }
                val tintJson=linked?.optJSONArray("tint")
                val tint=FloatArray(4) { ix -> (tintJson?.optDouble(ix,1.0) ?: 1.0).toFloat().coerceIn(0f,1f) }
                runOnUiThread {
                    val view=MeshPreviewView(this,model,textureSource,tint) { msg -> runOnUiThread { status.text=msg } }; activeGl=view
                    val col=LinearLayout(this).apply { orientation=LinearLayout.VERTICAL; setPadding(dp(6),dp(6),dp(6),dp(6)) }
                    col.addView(view,LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT,dp(430)))
                    col.addView(note("${result.optString("name")} · ${result.optInt("vertices")} vertices · ${result.optInt("triangles")} triangles"))
                    col.addView(note(textureNote))
                    col.addView(note("Kéo để xoay · chụm/mở 2 ngón để zoom. Preview áp texture UV và tint khi tìm được mapping. Shader độc quyền, skinning, rig và animation chưa được mô phỏng."))
                    if(model.truncated) col.addView(note("Mesh lớn: preview bị giới hạn tam giác để tránh hết RAM. File AssetBundle gốc không bị thay đổi."))
                    val dialog=AlertDialog.Builder(this).setTitle("Preview 3D · Mesh thật").setView(col).setPositiveButton("Đóng",null).create()
                    dialog.setOnDismissListener { view.onPause(); if(activeGl===view) activeGl=null }; dialog.show(); dialog.window?.setLayout((resources.displayMetrics.widthPixels*0.96).toInt(),ViewGroup.LayoutParams.WRAP_CONTENT)
                    status.text="Preview 3D đã tạo từ geometry gốc."
                }
            } catch(e:Exception) { runOnUiThread { status.text="Preview Mesh lỗi: ${rootError(e)}" } }
        }.start()
    }

    private fun previewTexture(item:JSONObject) {
        val id=item.optString("pathId"); status.text="Đang lấy dữ liệu Texture2D gốc…"
        Thread {
            try {
                val py=Python.getInstance().getModule("asset_engine")
                val result=JSONObject(py.callAttr("export_texture_preview",session,id,File(cacheDir,"texture_preview").absolutePath).toString())
                val decodedBitmap=if(result.optString("mode")=="png") BitmapFactory.decodeFile(result.optString("path")) else null
                runOnUiThread {
                    when(result.optString("mode")) {
                        "png" -> {
                            val bitmap=decodedBitmap ?: run { status.text="Android không mở được PNG preview"; return@runOnUiThread }
                            val content=LinearLayout(this).apply { orientation=LinearLayout.VERTICAL; setPadding(dp(10),dp(10),dp(10),dp(10)) }
                            val image=ImageView(this).apply { setImageBitmap(bitmap); scaleType=ImageView.ScaleType.FIT_CENTER; adjustViewBounds=true; setBackgroundColor(Color.rgb(28,31,36)) }
                            content.addView(image,LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT,dp(440)))
                            content.addView(note("${result.optString("width")}×${result.optString("height")} · ${result.optString("format")} · PNG giải mã từ dữ liệu Texture2D"))
                            AlertDialog.Builder(this).setTitle("Preview Texture2D").setView(content).setPositiveButton("Đóng",null).show()
                        }
                        "gpu-compressed" -> {
                            val content=LinearLayout(this).apply { orientation=LinearLayout.VERTICAL; setPadding(dp(8),dp(8),dp(8),dp(8)) }
                            val info=TextView(this).apply { text="Đang khởi tạo preview GPU…\n${result.optString("width")}×${result.optString("height")} · ${result.optString("format")}"; textSize=12f; setTextColor(Color.LTGRAY); setPadding(dp(6),dp(6),dp(6),dp(6)) }
                            val gl=TexturePreviewView(this,File(result.optString("path")),result.optInt("width"),result.optInt("height"),result.optString("glFormat")) { message -> info.text=message }
                            activeGl=gl; content.addView(gl,LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT,dp(390))); content.addView(info)
                            val dialog=AlertDialog.Builder(this).setTitle("Preview Texture2D · GPU").setView(content).setPositiveButton("Đóng",null).create()
                            dialog.setOnDismissListener { gl.onPause(); if(activeGl===gl) activeGl=null }; dialog.show(); dialog.window?.setLayout((resources.displayMetrics.widthPixels*0.96).toInt(),ViewGroup.LayoutParams.WRAP_CONTENT)
                        }
                        else -> AlertDialog.Builder(this).setTitle("Texture chưa preview được").setMessage("${result.optString("format")} · ${result.optString("width")}×${result.optString("height")}\n${result.optString("message")}").setPositiveButton("Đóng",null).show()
                    }
                    status.text="Texture ${item.optString("name")} · ${result.optString("mode")}"
                }
            } catch(e:Exception) { runOnUiThread { status.text="Preview Texture2D lỗi: ${rootError(e)}" } }
        }.start()
    }

    private fun showObjectDetails(item:JSONObject) {
        AlertDialog.Builder(this).setTitle(item.optString("name")).setMessage("Type: ${item.optString("type")}\nPathID: ${item.optString("pathId")}\n\nObject metadata từ AssetBundle. Preview/chỉnh sửa phụ thuộc loại object và dữ liệu parser đọc được.").setPositiveButton("Đóng",null).show()
    }

    private fun rootError(e:Throwable):String { var t=e; var depth=0; while(t.cause!=null && depth<5) { t=t.cause!!; depth++ }; return "${t.javaClass.simpleName}: ${t.message ?: "không có thông báo"}" }
}
