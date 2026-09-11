"""Deterministic seamless marble maps and a static reference KINO display.
Creates texture assets only. Surface materials and lighting are authored by Unity.
"""
from pathlib import Path
import numpy as np
from PIL import Image, ImageDraw, ImageFont

ROOT=Path(__file__).resolve().parents[2]
OUT=ROOT/'Assets/KinoRotunda/Textures'
OUT.mkdir(parents=True,exist_ok=True)
S=2048
Y,X=np.mgrid[0:S,0:S].astype(np.float32)/S
TAU=np.float32(2*np.pi)

def wave_noise(seed,octaves=5):
    rng=np.random.default_rng(seed); result=np.zeros_like(X)
    for o in range(octaves):
        for j in range(3):
            kx=int(rng.integers(1,4))*2**o;ky=int(rng.integers(-3,4))*2**o
            result+=np.sin(TAU*(kx*X+ky*Y)+rng.uniform(0,TAU)).astype(np.float32)*(.5**o)/3
    return result

def marble(name,seed,dark):
    rng=np.random.default_rng(seed)
    cloud=wave_noise(seed,6)
    u=(X*7+wave_noise(seed+1,5)*.52)%7
    v=(Y*7+wave_noise(seed+2,5)*.52)%7
    ix=np.floor(u).astype(int);iy=np.floor(v).astype(int)
    jitter=rng.uniform(.15,.85,(7,7,2)).astype(np.float32)
    d1=np.full_like(X,100);d2=np.full_like(X,100)
    for oy in (-1,0,1):
        for ox in (-1,0,1):
            cx=ix+ox;cy=iy+oy;j=jitter[cy%7,cx%7]
            d=(u-cx-j[:,:,0])**2+(v-cy-j[:,:,1])**2
            d2=np.minimum(d2,np.maximum(d1,d));d1=np.minimum(d1,d)
    gap=np.sqrt(d2)-np.sqrt(d1)
    vein=np.exp(-((gap/(.010+.010*(cloud+.8).clip(.15,1.6)))**2))
    halo=np.exp(-((gap/.058)**2))
    fine=np.exp(-(np.sin((X*9+Y*13)*TAU+wave_noise(seed+3,6)*9)/.045)**2)
    if dark:
        base=np.array([17,21,31],np.float32)
        rgb=base[None,None,:]+cloud[:,:,None]*np.array([6,7,10])
        rgb+=halo[:,:,None]*np.array([13,10,7])
        rgb+=vein[:,:,None]*np.array([80,57,30])*(.62+cloud[:,:,None]*.20)
        rgb+=fine[:,:,None]*np.array([6,5,5])
    else:
        base=np.array([190,184,172],np.float32)
        rgb=base[None,None,:]+cloud[:,:,None]*np.array([16,15,14])
        rgb-=halo[:,:,None]*np.array([10,11,11])
        rgb-=vein[:,:,None]*np.array([53,51,46])
        rgb-=fine[:,:,None]*np.array([8,8,7])
    Image.fromarray(rgb.clip(0,255).astype('uint8')).save(OUT/f'{name}_BaseColor.png')
    height=cloud*.04-vein*.015-fine*.003
    dx=(np.roll(height,-1,1)-np.roll(height,1,1))*.9
    dy=(np.roll(height,-1,0)-np.roll(height,1,0))*.9
    normal=np.dstack((-dx,-dy,np.ones_like(dx)))
    normal/=np.linalg.norm(normal,axis=2)[:,:,None]
    Image.fromarray(((normal*.5+.5)*255).clip(0,255).astype('uint8')).save(OUT/f'{name}_Normal.png')
    del rgb,normal,d1,d2
    print('Created',name,flush=True)

marble('NeroMarble',32,True)
marble('IvoryMarble',75,False)

# No screenshot is pasted into the model; this is a clean original graphic.
W,H=2048,1320
im=Image.new('RGB',(W,H),(7,15,22));d=ImageDraw.Draw(im)
fonts=Path('C:/Windows/Fonts')
def font(size,bold=False):return ImageFont.truetype(str(fonts/('arialbd.ttf' if bold else 'arial.ttf')),size)
def centered(text,x,y,f,fill):d.text((x,y),text,font=f,fill=fill,anchor='mm')
gold=(255,192,27);muted=(125,148,158)
for y in range(H):
    t=y/H; d.line((0,y,W,y),fill=(int(7+4*(1-t)),int(15+5*(1-t)),int(22+5*(1-t))))
centered('Kino',W/2,130,font(164,True),gold)
for i in range(6):
    x=W/2+(i-2.5)*23;d.ellipse((x-5,228,x+5,238),fill=(213,153,28))
d.line((98,284,W-98,284),fill=(91,74,39),width=2)
selected={2,6,7,10,15,17,21,27,37,39,42,47,52,55,57,62,67}
for row in range(8):
    if row%2==0:d.rounded_rectangle((88,308+row*109,W-88,407+row*109),radius=10,fill=(12,24,31))
    for col in range(10):
        n=row*10+col+1;x=158+col*(W-316)/9;y=359+row*109
        if n in selected:
            d.ellipse((x-40,y-40,x+40,y+40),fill=gold)
            centered(str(n),x,y+2,font(43,True),(27,31,29))
        elif n==73:
            d.ellipse((x-40,y-40,x+40,y+40),fill=(188,34,36))
            centered(str(n),x,y+2,font(43,True),(255,230,209))
        else:centered(str(n),x,y,font(41),muted)
d.line((98,1220,W-98,1220),fill=(91,74,39),width=2)
im.save(OUT/'KinoDisplay.png')
print('Created KINO display',flush=True)
