"""Minimal pure-Python AES-128 CBC implementation used only for UnityFS headers.
No padding is added or removed. Inputs must be a multiple of 16 bytes.
"""
MODE_CBC = 2
_S = bytes.fromhex(
'637c777bf26b6fc53001672bfed7ab76ca82c97dfa5947f0add4a2af9ca472c0'
' b7fd9326363ff7cc34a5e5f171d8311504c723c31896059a071280e2eb27b275'
'09832c1a1b6e5aa0523bd6b329e32f8453d100ed20fcb15b6acbbe394a4c58cf'
'd0efaafb434d338545f9027f503c9fa851a3408f929d38f5bcb6da2110fff3d2'
'cd0c13ec5f974417c4a77e3d645d197360814fdc222a908846eeb814de5e0bdb'
'e0323a0a4906245cc2d3ac629195e479e7c8376d8dd54ea96c56f4ea657aae08'
'ba78252e1ca6b4c6e8dd741f4bbd8b8a703eb5664803f60e613557b986c11d9e'
'e1f8981169d98e949b1e87e9ce5528df8ca1890dbfe6426841992d0fb054bb16')
_INV = bytearray(256)
for _i, _v in enumerate(_S): _INV[_v] = _i
_INV = bytes(_INV)
_RCON = (0x00,0x01,0x02,0x04,0x08,0x10,0x20,0x40,0x80,0x1b,0x36)

def _mul(a,b):
    out=0
    while b:
        if b&1: out ^= a
        a <<= 1
        if a & 0x100: a ^= 0x11b
        b >>= 1
    return out

def _expand(key):
    if len(key)!=16: raise ValueError('AES fallback supports 128-bit keys only')
    w=[list(key[i:i+4]) for i in range(0,16,4)]
    for i in range(4,44):
        t=w[i-1][:]
        if i%4==0:
            t=t[1:]+t[:1]; t=[_S[x] for x in t]; t[0]^=_RCON[i//4]
        w.append([w[i-4][j]^t[j] for j in range(4)])
    return [sum(w[i:i+4],[]) for i in range(0,44,4)]

def _add(s,k): return [a^b for a,b in zip(s,k)]
def _sub(s,box): return [box[x] for x in s]
def _shift(s, inverse=False):
    out=[0]*16
    for r in range(4):
        for c in range(4):
            out[4*c+r]=s[4*((c-r if inverse else c+r)%4)+r]
    return out

def _mix(s, inverse=False):
    out=s[:]
    for c in range(4):
        i=4*c; a,b,d,e=s[i:i+4]
        if not inverse:
            out[i]  = _mul(a,2)^_mul(b,3)^d^e
            out[i+1]= a^_mul(b,2)^_mul(d,3)^e
            out[i+2]= a^b^_mul(d,2)^_mul(e,3)
            out[i+3]= _mul(a,3)^b^d^_mul(e,2)
        else:
            out[i]  = _mul(a,14)^_mul(b,11)^_mul(d,13)^_mul(e,9)
            out[i+1]= _mul(a,9)^_mul(b,14)^_mul(d,11)^_mul(e,13)
            out[i+2]= _mul(a,13)^_mul(b,9)^_mul(d,14)^_mul(e,11)
            out[i+3]= _mul(a,11)^_mul(b,13)^_mul(d,9)^_mul(e,14)
    return out

class _CBC:
    def __init__(self,key,iv):
        self.rks=_expand(bytes(key)); self.iv=bytes(iv)
        if len(self.iv)!=16: raise ValueError('AES IV must be 16 bytes')
    def encrypt(self,data):
        data=bytes(data)
        if len(data)%16: raise ValueError('AES CBC input must be a multiple of 16 bytes')
        chain=self.iv; out=bytearray()
        for off in range(0,len(data),16):
            s=_add(list(bytes(a^b for a,b in zip(data[off:off+16],chain))),self.rks[0])
            for r in range(1,10): s=_add(_mix(_shift(_sub(s,_S)),False),self.rks[r])
            s=_add(_shift(_sub(s,_S)),self.rks[10]); block=bytes(s); out.extend(block); chain=block
        return bytes(out)
    def decrypt(self,data):
        data=bytes(data)
        if len(data)%16: raise ValueError('AES CBC input must be a multiple of 16 bytes')
        chain=self.iv; out=bytearray()
        for off in range(0,len(data),16):
            block=data[off:off+16]; s=_add(list(block),self.rks[10])
            for r in range(9,0,-1): s=_mix(_add(_sub(_shift(s,True),_INV),self.rks[r]),True)
            s=_add(_sub(_shift(s,True),_INV),self.rks[0])
            out.extend(a^b for a,b in zip(s,chain)); chain=block
        return bytes(out)

class AES:
    MODE_CBC=MODE_CBC
    @staticmethod
    def new(key, mode, iv):
        if mode != MODE_CBC: raise NotImplementedError('Only AES CBC supported in fallback')
        return _CBC(key,iv)
