"""AssetBundle bridge used by AoV/HOK Android editor.

All operations act on a parser session opened from a real Unity AssetBundle.
Mesh preview exports real vertices/indices to OBJ; texture preview exports real
Texture2D payloads (PNG for supported raw formats, compressed blocks for GPU preview).
"""
import os, json, math, struct, zlib, binascii, shutil, traceback
from UnityPy_AOV import load

_sessions = {}


def _type_name(reader):
    try: return reader.type.name
    except Exception: return str(getattr(reader, 'type', 'Unknown'))


def _obj_name(obj, reader):
    for owner in (obj, getattr(reader, 'reader', None), reader):
        if owner is None: continue
        for attr in ('m_Name', 'name'):
            try:
                val = getattr(owner, attr)
                if isinstance(val, str) and val: return val
            except Exception: pass
    return ''


def _find_reader(env, path_id, expected_type=None):
    for reader in env.objects:
        if str(reader.path_id) == str(path_id) and (expected_type is None or _type_name(reader) == expected_type):
            return reader
    raise ValueError(f'{expected_type or "Object"} PathID not found: {path_id}')


def _f(v):
    try: return float(v)
    except Exception: return 0.0


def _pairs(value):
    """Unity typetree maps are exposed as ordered list-of-pairs by this parser."""
    if isinstance(value, dict): return list(value.items())
    if isinstance(value, (list, tuple)):
        out=[]
        for item in value:
            if isinstance(item, (list, tuple)) and len(item)>=2: out.append((item[0],item[1]))
            elif isinstance(item, dict):
                key=item.get('first',item.get('key',item.get('m_Key')))
                val=item.get('second',item.get('value',item.get('m_Value')))
                if key is not None: out.append((key,val))
        return out
    return []


def _map_get(value, key):
    for k,v in _pairs(value):
        if str(k)==str(key): return v
    raise KeyError(key)


def _map_set(value, key, new_value):
    if isinstance(value, dict):
        if key not in value: raise KeyError(key)
        value[key]=new_value; return
    if isinstance(value, list):
        for i,item in enumerate(value):
            if isinstance(item,(list,tuple)) and len(item)>=2 and str(item[0])==str(key):
                value[i]=(item[0],new_value); return
            if isinstance(item,dict):
                k=item.get('first',item.get('key',item.get('m_Key')))
                if str(k)==str(key):
                    if 'second' in item: item['second']=new_value
                    elif 'value' in item: item['value']=new_value
                    else: item['m_Value']=new_value
                    return
    raise KeyError(key)


def inspect_bundle(path):
    """Inspect a real bundle while avoiding full decoding of unrelated components."""
    if not os.path.isfile(path): raise FileNotFoundError(path)
    env = load(path)
    sid = str(abs(hash(os.path.abspath(path))))
    _sessions[sid] = {'env': env, 'path': path}
    materials=[]; textures=[]; meshes=[]; objects=[]
    counts={}
    relevant = {'GameObject','Transform','MeshRenderer','SkinnedMeshRenderer','ParticleSystemRenderer','TrailRenderer','ParticleSystem','Animator','Material','Texture2D','Mesh','Shader','SpriteRenderer','MeshFilter','MonoBehaviour'}
    readers = env.objects
    for reader in readers:
        typ = _type_name(reader); counts[typ] = counts.get(typ, 0) + 1
        if typ not in relevant:
            continue
        name = ''
        try:
            # peek_name reads only the leading serialized name and does not invoke
            # type-specific class deserializers. This avoids dozens of game-specific
            # MonoBehaviour/ParticleSystem parser failures during a simple inventory.
            name = reader.peek_name(default='') or ''
        except Exception:
            name = ''
        if typ == 'Material':
            colors=[]; floats=[]; texenvs=[]
            try:
                tree=reader.read_typetree()
                name=str(tree.get('m_Name') or name or 'Material')
                sp=tree.get('m_SavedProperties',{}) or {}
                for key, color in _pairs(sp.get('m_Colors',[])):
                    if not isinstance(color,dict): continue
                    colors.append({'key':str(key),'r':_f(color.get('r',1)),'g':_f(color.get('g',1)),'b':_f(color.get('b',1)),'a':_f(color.get('a',1))})
                for key, value in _pairs(sp.get('m_Floats',[])):
                    floats.append({'key':str(key),'value':_f(value)})
                for key, value in _pairs(sp.get('m_TexEnvs',[])):
                    tex=value.get('m_Texture',{}) if isinstance(value,dict) else {}
                    texenvs.append({'key':str(key), 'texturePathId':str(tex.get('m_PathID',0) if isinstance(tex,dict) else 0)})
            except Exception:
                # Keep unreadable materials searchable, but do not fabricate properties.
                pass
            materials.append({'pathId':str(reader.path_id),'name':name or 'Material','colors':colors,'floats':floats,'textures':texenvs})
        elif typ == 'Texture2D':
            try:
                obj=reader.read()
                fmt = getattr(obj, 'm_TextureFormat', None)
                textures.append({'pathId':str(reader.path_id),'name':name or _obj_name(obj,reader) or 'Texture2D','width':int(getattr(obj,'m_Width',0)),'height':int(getattr(obj,'m_Height',0)),'format':getattr(fmt,'name',str(fmt)),'formatId':int(getattr(fmt,'value',fmt) or 0),'mipCount':int(getattr(obj,'m_MipCount',1) or 1)})
            except Exception as exc:
                textures.append({'pathId':str(reader.path_id),'name':name or 'Texture2D','width':0,'height':0,'format':'unreadable','formatId':0,'mipCount':0,'error':str(exc)})
        elif typ == 'Mesh':
            try:
                obj=reader.read()
                vc=int(getattr(obj,'m_VertexCount',0) or 0)
                try: tris=sum(int(sub.indexCount)//3 for sub in obj.m_SubMeshes)
                except Exception: tris=0
                meshes.append({'pathId':str(reader.path_id),'name':name or _obj_name(obj,reader) or 'Mesh','vertices':vc,'triangles':tris,'subMeshes':len(getattr(obj,'m_SubMeshes',[]) or [])})
            except Exception as exc:
                meshes.append({'pathId':str(reader.path_id),'name':name or 'Mesh','vertices':0,'triangles':0,'subMeshes':0,'error':str(exc)})
        elif typ == 'GameObject' and not name:
            # GameObject has no leading m_Name field in the binary layout. Read only
            # this common Unity type to get its hierarchy label, not custom scripts.
            try: name=_obj_name(reader.read(),reader)
            except Exception: pass
        objects.append({'pathId':str(reader.path_id),'type':typ,'name':name or typ})
    return json.dumps({'session':sid,'source':os.path.basename(path),'fileSize':os.path.getsize(path),'objectCount':len(readers),'counts':counts,'materialCount':len(materials),'textureCount':len(textures),'meshCount':len(meshes),'materials':materials,'textures':textures,'meshes':meshes,'objects':objects}, ensure_ascii=False, separators=(',',':'))


def _write_png_rgba(path, width, height, pixels):
    if width <= 0 or height <= 0 or len(pixels) != width*height*4:
        raise ValueError('Invalid RGBA image dimensions/data length')
    def chunk(kind, data):
        return struct.pack('>I', len(data)) + kind + data + struct.pack('>I', binascii.crc32(kind+data) & 0xffffffff)
    scan = bytearray()
    row_size = width*4
    for y in range(height):
        scan.append(0)
        scan.extend(pixels[y*row_size:(y+1)*row_size])
    png = b'\x89PNG\r\n\x1a\n'
    png += chunk(b'IHDR', struct.pack('>IIBBBBB', width,height,8,6,0,0,0))
    png += chunk(b'IDAT', zlib.compress(bytes(scan), 6))
    png += chunk(b'IEND', b'')
    with open(path,'wb') as f: f.write(png)


def _raw_texture_to_png(obj, output_path):
    width, height = int(obj.m_Width), int(obj.m_Height)
    fmt_obj = obj.m_TextureFormat
    fmt = getattr(fmt_obj, 'name', str(fmt_obj))
    data = bytes(obj.image_data or b'')
    expected = width*height
    rgba = bytearray(expected*4)
    if fmt == 'RGBA32':
        need=expected*4
        if len(data)<need: raise ValueError(f'RGBA32 payload short: {len(data)} < {need}')
        rgba[:] = data[:need]
    elif fmt == 'BGRA32':
        need=expected*4
        if len(data)<need: raise ValueError(f'BGRA32 payload short: {len(data)} < {need}')
        for i in range(expected):
            b,g,r,a=data[i*4:i*4+4]; rgba[i*4:i*4+4]=bytes((r,g,b,a))
    elif fmt == 'ARGB32':
        need=expected*4
        if len(data)<need: raise ValueError(f'ARGB32 payload short: {len(data)} < {need}')
        for i in range(expected):
            a,r,g,b=data[i*4:i*4+4]; rgba[i*4:i*4+4]=bytes((r,g,b,a))
    elif fmt == 'RGB24':
        need=expected*3
        if len(data)<need: raise ValueError(f'RGB24 payload short: {len(data)} < {need}')
        for i in range(expected):
            r,g,b=data[i*3:i*3+3]; rgba[i*4:i*4+4]=bytes((r,g,b,255))
    elif fmt == 'Alpha8':
        if len(data)<expected: raise ValueError('Alpha8 payload short')
        for i,a in enumerate(data[:expected]): rgba[i*4:i*4+4]=bytes((255,255,255,a))
    elif fmt == 'R8':
        if len(data)<expected: raise ValueError('R8 payload short')
        for i,v in enumerate(data[:expected]): rgba[i*4:i*4+4]=bytes((v,v,v,255))
    elif fmt == 'RGB565':
        need=expected*2
        if len(data)<need: raise ValueError('RGB565 payload short')
        for i in range(expected):
            n=data[2*i] | (data[2*i+1]<<8)
            r=((n>>11)&31)*255//31; g=((n>>5)&63)*255//63; b=(n&31)*255//31
            rgba[i*4:i*4+4]=bytes((r,g,b,255))
    else:
        return False
    _write_png_rgba(output_path,width,height,bytes(rgba))
    return True


def _base_level_size(width, height, fmt):
    # First-mip byte count for supported GPU block-compressed Unity formats.
    if fmt.startswith('ASTC_'):
        dims=fmt.rsplit('_',1)[1].split('x'); bw,bh=int(dims[0]),int(dims[1])
        return max(1,(width+bw-1)//bw)*max(1,(height+bh-1)//bh)*16
    block = None
    if fmt in ('ETC_RGB4','ETC_RGB4_3DS','ETC2_RGB','ETC2_RGBA1','EAC_R','EAC_R_SIGNED','DXT1','BC4','ATC_RGB4'): block=(4,4,8)
    elif fmt in ('ETC_RGBA8_3DS','EAC_RG','EAC_RG_SIGNED','DXT5','BC5','BC6H','BC7','ETC2_RGBA8','ATC_RGBA8'): block=(4,4,16)
    if block:
        bw,bh,bbytes=block
        return max(1,(width+bw-1)//bw)*max(1,(height+bh-1)//bh)*bbytes
    if fmt.startswith('PVRTC'):
        bpp=2 if fmt.endswith('2') else 4
        return max(32 if bpp==2 else 32, (width*height*bpp+7)//8)
    return len(b'')


def export_texture_preview(session, path_id, output_dir):
    state=_sessions.get(str(session))
    if not state: raise ValueError('Bundle session expired; reopen the file')
    reader=_find_reader(state['env'],path_id,'Texture2D'); obj=reader.read()
    os.makedirs(output_dir,exist_ok=True)
    safe=''.join(c if c.isalnum() or c in '-_.' else '_' for c in (_obj_name(obj,reader) or 'texture'))[:80]
    base=f'{safe}_{path_id}'
    png=os.path.join(output_dir,base+'.png')
    try:
        if _raw_texture_to_png(obj,png):
            return json.dumps({'mode':'png','path':png,'name':safe,'pathId':str(path_id),'width':obj.m_Width,'height':obj.m_Height,'format':getattr(obj.m_TextureFormat,'name',str(obj.m_TextureFormat)),'message':'Decoded from original Texture2D pixel data'})
    except Exception as e:
        raw_decode_error=str(e)
    else:
        raw_decode_error='Raw pixel format is compressed or unsupported by the lightweight decoder'
    fmt=getattr(obj.m_TextureFormat,'name',str(obj.m_TextureFormat))
    data=bytes(obj.image_data or b'')
    base_size=_base_level_size(int(obj.m_Width),int(obj.m_Height),fmt)
    if base_size > 0 and len(data)>=base_size:
        out=os.path.join(output_dir,base+'.texbin')
        with open(out,'wb') as f: f.write(data[:base_size])
        gl_map={
            'ETC_RGB4':'0x8D64','ETC_RGB4_3DS':'0x8D64','ETC2_RGB':'0x9274','ETC2_RGBA1':'0x9276','ETC2_RGBA8':'0x9278','ETC_RGBA8_3DS':'0x9278',
            'DXT1':'0x83F0','DXT1Crunched':'0x83F0','DXT5':'0x83F3','DXT5Crunched':'0x83F3',
            'PVRTC_RGB2':'0x8C01','PVRTC_RGBA2':'0x8C03','PVRTC_RGB4':'0x8C00','PVRTC_RGBA4':'0x8C02',
            'ATC_RGB4':'0x8C92','ATC_RGBA8':'0x87EE','BC4':'0x8DBB','BC5':'0x8DBD','BC6H':'0x8E8F','BC7':'0x8E8C',
            'EAC_R':'0x9270','EAC_R_SIGNED':'0x9271','EAC_RG':'0x9272','EAC_RG_SIGNED':'0x9273'}
        if fmt.startswith('ASTC_'):
            dims=fmt.rsplit('_',1)[1]
            gl_astc={'4x4':0x93B0,'5x5':0x93B2,'6x6':0x93B4,'8x8':0x93B7,'10x10':0x93BB,'12x12':0x93BD}
            gl_fmt=hex(gl_astc[dims]) if dims in gl_astc else ''
        else: gl_fmt=gl_map.get(fmt,'')
        if gl_fmt:
            return json.dumps({'mode':'gpu-compressed','path':out,'name':safe,'pathId':str(path_id),'width':obj.m_Width,'height':obj.m_Height,'format':fmt,'glFormat':gl_fmt,'byteSize':base_size,'message':'Upload original compressed blocks to GPU; availability depends on device extensions'})
    # Last attempt uses the full decoder backend if bundled/available.
    try:
        image=obj.image
        image.save(png)
        return json.dumps({'mode':'png','path':png,'name':safe,'pathId':str(path_id),'width':obj.m_Width,'height':obj.m_Height,'format':fmt,'message':'Decoded using UnityPy_AOV texture decoder'})
    except Exception as e:
        return json.dumps({'mode':'unsupported','name':safe,'pathId':str(path_id),'width':obj.m_Width,'height':obj.m_Height,'format':fmt,'message':f'No decoder path for this format. {raw_decode_error}; decoder: {e}'})


def _ptr_path_id(value):
    if isinstance(value,dict): return int(value.get('m_PathID',value.get('path_id',0)) or 0)
    try: return int(getattr(value,'path_id',0) or 0)
    except Exception: return 0


def _linked_material_for_mesh(env, mesh_path_id):
    """Resolve MeshFilter/SkinnedMeshRenderer -> Renderer -> Material -> main texture via typetrees."""
    target=str(mesh_path_id); go_ids=[]; direct_renderers=[]
    for reader in env.objects:
        typ=_type_name(reader)
        if typ not in ('MeshFilter','SkinnedMeshRenderer'): continue
        try:
            tree=reader.read_typetree()
            mid=_ptr_path_id(tree.get('m_Mesh',{}))
            if str(mid)!=target: continue
            goid=_ptr_path_id(tree.get('m_GameObject',{}))
            if typ=='SkinnedMeshRenderer': direct_renderers.append((reader,tree,goid))
            elif goid: go_ids.append(goid)
        except Exception: continue
    renderer_candidates=list(direct_renderers)
    if go_ids:
        go_set=set(go_ids)
        for reader in env.objects:
            if _type_name(reader)!='MeshRenderer': continue
            try:
                tree=reader.read_typetree(); goid=_ptr_path_id(tree.get('m_GameObject',{}))
                if goid in go_set: renderer_candidates.append((reader,tree,goid))
            except Exception: continue
    for renderer,rtree,goid in renderer_candidates:
        material_ptrs=rtree.get('m_Materials',[]) or []
        for mp in material_ptrs:
            mid=_ptr_path_id(mp)
            if not mid: continue
            try:
                mr=_find_reader(env,mid,'Material'); mtree=mr.read_typetree()
                mat_name=mtree.get('m_Name') or 'Material'
                sp=mtree.get('m_SavedProperties',{}) or {}
                tex_entries=_pairs(sp.get('m_TexEnvs',[]))
                tex_by_key={str(k):v for k,v in tex_entries if isinstance(v,dict)}
                texid=0; texkey=''
                for key in ('_MainTex','_BaseMap','_DiffuseMap','_DiffuseTex','_BaseTex','_ColorTex'):
                    if key in tex_by_key:
                        tid=_ptr_path_id(tex_by_key[key].get('m_Texture',{}))
                        if tid: texid=tid; texkey=key; break
                if not texid:
                    for key,val in tex_entries:
                        if isinstance(val,dict):
                            tid=_ptr_path_id(val.get('m_Texture',{}))
                            if tid: texid=tid; texkey=str(key); break
                tint=[1.0,1.0,1.0,1.0]; tintkey=''
                color_entries=_pairs(sp.get('m_Colors',[])); cmap={str(k):v for k,v in color_entries if isinstance(v,dict)}
                for key in ('_TintColor','_BaseColor','_Color','_MainColor'):
                    if key in cmap:
                        cc=cmap[key]; tint=[_f(cc.get(k,1)) for k in ('r','g','b','a')]; tintkey=key; break
                return {'rendererPathId':str(renderer.path_id),'materialPathId':str(mid),'materialName':str(mat_name),'texturePathId':str(texid) if texid else '', 'textureKey':texkey,'tint':tint,'tintKey':tintkey}
            except Exception: continue
    return {'rendererPathId':'','materialPathId':'','materialName':'','texturePathId':'','textureKey':'','tint':[1.0,1.0,1.0,1.0],'tintKey':''}


def export_mesh_preview(session, path_id, output_dir):
    state=_sessions.get(str(session))
    if not state: raise ValueError('Bundle session expired; reopen the file')
    reader=_find_reader(state['env'],path_id,'Mesh'); obj=reader.read()
    data=obj.export()
    if not isinstance(data,str) or not data.strip() or data is False:
        raise RuntimeError('Mesh parser could not export vertices/indices for this Mesh')
    os.makedirs(output_dir,exist_ok=True)
    safe=''.join(c if c.isalnum() or c in '-_.' else '_' for c in (_obj_name(obj,reader) or 'mesh'))[:80]
    out=os.path.join(output_dir,f'{safe}_{path_id}.obj')
    with open(out,'w',encoding='utf-8',newline='') as f: f.write(data)
    tris=0
    try: tris=sum(int(s.indexCount)//3 for s in obj.m_SubMeshes)
    except Exception: pass
    linked=_linked_material_for_mesh(state['env'],path_id)
    return json.dumps({'path':out,'name':safe,'pathId':str(path_id),'vertices':int(getattr(obj,'m_VertexCount',0) or 0),'triangles':tris,'subMeshes':len(getattr(obj,'m_SubMeshes',[]) or []),'bytes':os.path.getsize(out),'material':linked,'message':'OBJ exported from actual Mesh geometry; first linked material/texture is resolved when available'})


def _save_edited_environment(state, found, output_path):
    env=state['env']; found.assets_file.is_changed=True
    def walk(node):
        for child in getattr(node,'files',{}).values():
            yield child
            yield from walk(child)
    roots=[]
    for key, top in env.files.items():
        if top is found.assets_file or any(v is found.assets_file for v in walk(top)):
            top.is_changed=True; roots.append((key,top))
    if not roots: raise RuntimeError('Changed SerializedFile is not attached to a writable root file')
    output_path=os.path.abspath(output_path); outdir=os.path.dirname(output_path)
    os.makedirs(outdir,exist_ok=True)
    tempdir=os.path.join(outdir,'.aov_tmp_'+str(os.getpid()))
    os.makedirs(tempdir,exist_ok=True)
    try:
        env.out_path=tempdir
        env.save(pack='original')
        produced=os.path.join(tempdir,os.path.basename(roots[0][0]))
        if not os.path.isfile(produced) or os.path.getsize(produced)==0:
            raise RuntimeError('Parser writer did not produce the changed bundle')
        shutil.copy2(produced,output_path)
    finally:
        shutil.rmtree(tempdir,ignore_errors=True)
    check=load(output_path)
    return output_path,check


def set_material_color(session, path_id, color_key, r, g, b, a, output_path):
    state = _sessions.get(str(session))
    if not state: raise ValueError('Bundle session expired; reopen the file.')
    found=_find_reader(state['env'],path_id,'Material')
    tree=found.read_typetree(); sp=tree.get('m_SavedProperties',{})
    color=dict(_map_get(sp.get('m_Colors',[]),color_key))
    color.update({'r':float(r),'g':float(g),'b':float(b),'a':float(a)})
    _map_set(sp.get('m_Colors',[]),color_key,color)
    found.save_typetree(tree)
    output_path,check=_save_edited_environment(state,found,output_path)
    found2=_find_reader(check,path_id,'Material'); tree2=found2.read_typetree()
    actual=_map_get(tree2.get('m_SavedProperties',{}).get('m_Colors',[]),color_key)
    values=[_f(actual.get(k)) for k in ('r','g','b','a')]
    if any(abs(x-y)>0.003 for x,y in zip(values,[r,g,b,a])):
        raise RuntimeError(f'Read-back color mismatch: {values}')
    return json.dumps({'output':output_path,'fileSize':os.path.getsize(output_path),'readbackObjects':len(check.objects),'status':'readback-ok','savedColor':values})

def set_material_float(session, path_id, float_key, value, output_path):
    state = _sessions.get(str(session))
    if not state: raise ValueError('Bundle session expired; reopen the file.')
    found=_find_reader(state['env'],path_id,'Material')
    tree=found.read_typetree(); sp=tree.get('m_SavedProperties',{})
    _map_set(sp.get('m_Floats',[]),float_key,float(value))
    found.save_typetree(tree)
    output_path,check=_save_edited_environment(state,found,output_path)
    found2=_find_reader(check,path_id,'Material'); tree2=found2.read_typetree()
    actual=_f(_map_get(tree2.get('m_SavedProperties',{}).get('m_Floats',[]),float_key))
    if abs(actual-float(value)) > max(0.0001,abs(float(value))*0.0001):
        raise RuntimeError(f'Read-back float mismatch: {actual}')
    return json.dumps({'output':output_path,'fileSize':os.path.getsize(output_path),'readbackObjects':len(check.objects),'status':'readback-ok','savedValue':actual})

