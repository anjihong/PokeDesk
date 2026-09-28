"""Reference-matched UI geometry drawn pixel by pixel from empty RGBA canvases.

Reference coordinates are measured in design/reference-ui-target.png. This
generator never reads that image into any exported UI component. Text and
runtime Pokemon artwork are used only in the separate demonstration preview.
"""
from pathlib import Path
import json
import math
import os
import random
from PIL import Image, ImageDraw, ImageFont

ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT / 'Assets' / 'PixelUI'
DESIGN = ROOT / 'design'
N = '#0c1d36'
N2 = '#19374c'
CREAM = '#f3f0e1'
CYAN = '#359ab2'
assets = {}
layers = []


def rgb(color):
    if isinstance(color, tuple):
        return color[:3]
    return tuple(int(color[i:i+2], 16) for i in (1, 3, 5))


class Sprite:
    """All geometry is authored on a half-resolution integer pixel lattice."""
    def __init__(self, width, height):
        self.im = Image.new('RGBA', (width // 2, height // 2), (0, 0, 0, 0))
        self.d = ImageDraw.Draw(self.im)
        self.w, self.h = self.im.size

    def rect(self, x, y, w, h, color):
        self.d.rectangle((x, y, x+w-1, y+h-1), fill=color)

    def poly(self, points, color):
        self.d.polygon(points, fill=color)

    def material(self, points, base, kind='plain', seed=1):
        """Plot a finite-color material, with hard pixels and no blur/resampling."""
        mask = Image.new('1', self.im.size)
        ImageDraw.Draw(mask).polygon(points, fill=1)
        bounds = mask.getbbox()
        if not bounds:
            return
        l, t, r, b = bounds
        color = rgb(base)
        rng = random.Random(seed)
        tiles = {(x, y): rng.choice([-2, -1, 0, 0, 0, 1, 2])
                 for y in range(self.h // 3 + 1) for x in range(self.w // 3 + 1)}
        for y in range(t, b):
            v = (y-t)/max(1, b-t-1)
            for x in range(l, r):
                if not mask.getpixel((x, y)):
                    continue
                u = (x-l)/max(1, r-l-1)
                grain = tiles[(x//3, y//3)]
                if kind == 'cream':
                    shift = 2.5*math.cos(u*math.pi)+1.5*math.sin(v*math.pi)+grain*.35
                elif kind == 'cell':
                    shift = 6*math.sin(u*math.pi)*math.sin(v*math.pi)-4*u+grain*.65
                elif kind == 'red':
                    shift = 3*math.sin(u*math.pi)-5*v+grain*.3
                elif kind == 'gold':
                    shift = 4*math.sin(u*math.pi)-3*v+grain*.45
                elif kind == 'grid':
                    shift = (((x//5+y//4)%2)*2-1)*3+grain*1.5
                else:
                    shift = grain*.35
                # Quantized material shades still retain exactly aligned 2 px squares.
                shift = round(shift/2)*2
                self.im.putpixel((x, y), tuple(max(0, min(255, c+shift)) for c in color)+(255,))

    def image(self):
        return self.im.resize((self.w*2, self.h*2), Image.Resampling.NEAREST)

    def save(self, name, pos=None, role='panel'):
        self.image().save(OUT / f'{name}.png')
        assets[name] = {'size': [self.w*2, self.h*2], 'position': pos, 'role': role}
        if pos:
            layers.append((name, pos))


def cutbox(x, y, w, h, c=2):
    return [(x+c,y),(x+w-c-1,y),(x+w-c-1,y+c),(x+w-1,y+c),
            (x+w-1,y+h-c-1),(x+w-c-1,y+h-c-1),(x+w-c-1,y+h-1),
            (x+c,y+h-1),(x+c,y+h-c-1),(x,y+h-c-1),(x,y+c),(x+c,y+c)]


def frame():
    p = Sprite(704, 718)
    p.poly([(0,0),(351,0),(351,348),(348,348),(348,352),(344,352),
            (344,355),(339,355),(339,358),(10,358),(10,355),(6,355),
            (6,352),(3,352),(3,348),(0,348)], N)
    p.poly([(3,2),(348,2),(348,347),(345,347),(345,351),(341,351),
            (341,353),(10,353),(10,351),(7,351),(7,348),(3,348)], '#244b60')
    p.poly([(5,2),(347,2),(347,346),(344,346),(344,350),(9,350),
            (9,347),(5,347)], '#bbc9c9')
    p.material([(7,2),(345,2),(345,345),(342,345),(342,348),(11,348),
                (11,345),(7,345)], CREAM, 'cream')
    p.rect(8,2,336,3,'#fffdef')
    p.rect(11,347,331,2,'#fdfae9')
    p.rect(14,350,326,2,'#6d8e9d')
    p.save('drawer_frame',(255,498))


def rail():
    p = Sprite(704, 84)
    p.poly([(0,15),(4,15),(4,11),(8,11),(8,7),(12,7),(12,4),
            (339,4),(339,7),(344,7),(344,10),(348,10),(348,14),
            (351,14),(351,41),(0,41)], N)
    p.rect(6,14,339,26,'#e42c3c')
    p.rect(7,11,333,4,'#eb4b5a')
    p.rect(0,40,352,2,N)
    p.rect(5,17,2,23,'#f15b62')
    p.rect(10,10,3,30,N2)
    p.save('tab_rail',(255,418))


def tab(name, width, gold, pos):
    p = Sprite(width, 82)
    w,h=p.w,p.h
    # Flat bottom; only the two upper shoulders are stepped.
    outer=[(0,12),(2,12),(2,8),(5,8),(5,5),(8,5),(8,2),(w-9,2),
           (w-9,4),(w-6,4),(w-6,7),(w-3,7),(w-3,11),(w-1,11),(w-1,h-1),(0,h-1)]
    p.poly(outer,N)
    face=[(3,14),(5,14),(5,10),(8,10),(8,7),(11,7),(11,5),(w-11,5),
          (w-11,7),(w-8,7),(w-8,10),(w-5,10),(w-5,13),(w-3,13),(w-3,h-3),(3,h-3)]
    p.material(face,'#fdcc30' if gold else '#e32935','gold' if gold else 'red')
    bright='#ffe16a' if gold else '#f86a76'
    p.rect(11,5,w-22,2,bright)
    for x,y in [(8,7),(5,10)]:
        p.rect(x,y,3,2,bright)
        p.rect(w-x-3,y,3,2,bright)
    p.rect(3,14,1,h-17,'#ffdc55' if gold else '#ef485a')
    p.rect(w-4,14,1,h-17,'#b59827' if gold else '#bd293c')
    p.rect(3,h-4,w-6,1,'#e4b431' if gold else '#ca2234')
    p.save(name,pos)


def toolbar():
    p=Sprite(676,78)
    p.material([(0,0),(337,0),(337,38),(0,38)],CREAM,'cream')
    p.rect(0,37,338,2,'#d5d8c8')
    p.save('toolbar_bg',(270,500))
    p=Sprite(234,62)
    p.poly(cutbox(0,0,117,30,3),N)
    p.poly(cutbox(2,2,113,26,2),'#aabac0')
    p.material(cutbox(4,3,109,24,1),CREAM,'cream')
    p.rect(4,3,18,24,'#c6d0cf')
    p.rect(92,3,21,24,'#c4cccb')
    p.rect(22,3,1,24,'#e1e3d6')
    p.rect(91,3,1,24,'#e1e3d6')
    p.rect(4,3,109,1,'#f8f8ec')
    p.rect(3,28,111,1,'#395369')
    p.save('generation_selector',(280,508))


def arrows():
    for name,left in [('arrow_left',True),('arrow_right',False)]:
        p=Sprite(24,34)
        pts=[(1,8),(3,8),(3,6),(5,6),(5,4),(7,4),(7,2),(9,2),
             (9,15),(7,15),(7,13),(5,13),(5,11),(3,11),(3,10),(1,10)]
        if not left:
            pts=[(11-x,y) for x,y in pts]
        p.poly(pts,N)
        p.save(name,None,'icon')


def checkbox():
    for checked in (False,True):
        p=Sprite(38,38)
        p.poly(cutbox(0,0,19,19,1),N)
        p.rect(2,2,15,15,'#bdc5c0')
        p.material([(3,3),(15,3),(15,15),(3,15)],CREAM,'cream')
        if checked:
            p.poly([(4,8),(6,8),(6,10),(8,10),(8,8),(10,8),(10,6),(12,6),
                    (12,4),(14,4),(14,8),(12,8),(12,10),(10,10),(10,12),(6,12),(6,10),(4,10)],N)
        p.save('checkbox_on' if checked else 'checkbox_off',None,'control')


def grid():
    p=Sprite(672,450)
    p.rect(0,0,336,225,'#1a6d89')
    p.rect(0,0,336,2,'#b5d1d0')
    p.rect(0,2,336,2,'#104e68')
    p.material([(2,4),(333,4),(333,224),(2,224)],'#147392','grid')
    p.rect(0,6,2,219,'#288ca3')
    p.rect(334,3,2,222,'#499bad')
    p.save('box_grid_bg',(272,576))


def slot(name,state):
    p=Sprite(104,104)
    edge=cutbox(0,0,52,52,4)
    p.poly(edge,'#105771')
    p.poly(cutbox(1,1,50,50,3),'#216c84')
    p.poly(cutbox(2,2,48,48,2),'#55afc1')
    p.material(cutbox(3,3,46,46,2),'#183d55' if state=='unknown' else
               ('#237b94' if state=='selected' else '#369eb7'),'cell',seed=4)
    p.rect(6,2,40,1,'#51b1c4')
    p.rect(2,6,1,40,'#48a7bc')
    p.rect(49,6,1,40,'#338a9e')
    p.rect(6,49,40,1,'#5fb2c0')
    p.rect(6,50,40,1,'#225d75')
    if state=='selected':
        p.poly(cutbox(1,1,50,50,3),'#c5a435')
        p.poly(cutbox(2,2,48,48,2),'#ffd53e')
        p.poly(cutbox(4,4,44,44,1),'#ffe775')
        p.material(cutbox(5,5,42,42,1),'#257a8f','cell',4)
        p.rect(7,2,38,1,'#ffe56b')
        p.rect(2,7,1,38,'#ffe56b')
    elif state=='hover':
        p.rect(6,3,40,2,'#98d4da')
        p.rect(3,6,2,40,'#98d4da')
    p.save(name,None,'slot')


def detail():
    p=Sprite(672,180)
    p.material([(2,0),(333,0),(333,89),(2,89)],CREAM,'cream')
    p.rect(0,3,2,82,'#b3c5c6')
    p.rect(334,3,2,82,'#b3c5c6')
    p.save('detail_base',(272,1022))
    p=Sprite(650,158)
    p.poly(cutbox(0,0,325,79,5),N)
    p.poly(cutbox(3,3,319,73,3),'#fffdef')
    p.material(cutbox(5,5,315,69,2),CREAM,'cream')
    p.rect(4,6,1,64,'#b7c5c5')
    p.rect(320,6,1,64,'#c1ceca')
    p.rect(79,30,235,2,'#bdc7c2')
    p.save('detail_panel',(282,1030))
    p=Sprite(130,132)
    p.poly(cutbox(0,0,65,66,2),'#c6cdc5')
    p.poly(cutbox(2,2,61,62,1),'#fffdef')
    p.material([(4,4),(60,4),(60,61),(4,61)],CREAM,'cream')
    p.rect(63,4,2,57,'#b9c5c0')
    p.save('portrait_frame',(295,1043))
    p=Sprite(96,40)
    p.material(cutbox(0,0,48,20,3),'#e84631','red')
    p.rect(5,0,38,1,'#ee6450')
    p.save('type_badge',(551,1047))
    p=Sprite(136,42)
    p.poly(cutbox(0,0,68,21,2),'#94a6ad')
    p.poly(cutbox(2,2,64,17,1),'#ccd4cf')
    p.material([(4,3),(63,3),(63,17),(4,17)],'#d4dcd5','cream')
    p.rect(5,2,57,1,'#f5f7e9')
    p.save('number_badge',(781,1042))


def status():
    p=Sprite(196,56)
    p.poly(cutbox(0,0,98,28,3),N)
    p.poly(cutbox(2,2,94,24,1),'#b3bfbf')
    p.material([(4,4),(93,4),(93,23),(4,23)],CREAM,'cream')
    p.rect(4,3,89,1,'#fffdef')
    p.rect(3,25,92,1,N)
    p.save('egg_timer_plate',(710,341))
    p=Sprite(284,38)
    p.poly(cutbox(0,0,142,19,2),N)
    p.rect(2,2,138,14,'#a1b4c1')
    p.rect(3,3,136,12,'#94a9ba')
    p.rect(3,3,136,1,'#b5c4ca')
    for x in range(4,139,18):
        p.rect(x,3,2,12,'#29465e')
    p.rect(2,16,138,1,'#2d485b')
    p.save('exp_track',(372,361))
    p=Sprite(76,44)
    p.poly(cutbox(0,0,38,22,3),N)
    p.poly(cutbox(2,2,34,18,2),'#b4a042')
    p.material(cutbox(3,3,32,16,1),'#ffd13b','gold')
    p.rect(5,3,28,1,'#fff087')
    p.save('exp_label_plate',(302,355))
    p=Sprite(32,22)
    p.rect(0,0,16,11,'#a69b55')
    p.material([(1,1),(15,1),(15,10),(1,10)],'#ffce39','gold')
    p.rect(1,1,15,1,'#ffe775')
    p.save('exp_fill_segment',None,'fill')


def icons():
    p=Sprite(44,48)
    p.rect(3,2,16,20,N)
    p.rect(1,20,18,3,N)
    p.rect(5,4,12,15,'#fa6a67')
    p.rect(7,5,10,14,'#dd303c')
    p.rect(5,5,2,14,'#fc8080')
    p.rect(7,5,1,14,'#ef4648')
    p.rect(4,18,14,4,'#7996a0')
    p.rect(6,18,11,2,'#f8f3dc')
    p.rect(6,21,11,1,'#d23743')
    p.poly([(10,6),(15,6),(15,12),(10,12),(10,10),(13,10),(13,8),(10,8)],'#fff8e2')
    p.rect(2,22,16,1,'#c9b16b')
    p.save('icon_dex',None,'icon')
    p=Sprite(48,48)
    p.poly([(12,1),(22,6),(22,18),(12,23),(2,18),(2,6)],N)
    p.rect(10,1,4,2,N)
    p.poly([(12,3),(19,6),(12,10),(5,6)],'#fff9de')
    p.poly([(4,9),(10,12),(10,20),(4,17)],'#f4f2de')
    p.poly([(13,12),(20,9),(20,17),(13,20)],'#dae2d9')
    p.rect(13,13,1,6,'#afc1c2')
    p.save('icon_box',None,'icon')
    p=Sprite(48,48)
    p.poly([(8,1),(15,1),(15,4),(18,4),(18,3),(21,6),(20,8),
            (23,8),(23,15),(20,15),(21,18),(18,21),(16,20),(16,23),
            (8,23),(8,20),(6,21),(3,18),(4,15),(1,15),(1,8),(4,8),
            (3,6),(6,3),(8,4)],N)
    p.poly([(10,3),(14,3),(14,6),(17,6),(18,5),(19,7),(17,9),
            (21,10),(21,13),(18,13),(18,16),(19,17),(17,19),(15,17),
            (14,21),(10,21),(10,18),(7,17),(6,18),(5,16),(7,14),
            (3,13),(3,10),(6,10),(7,7),(6,6),(8,5),(10,7)],'#f2f1de')
    p.poly(cutbox(8,8,8,8,2),N)
    p.rect(10,10,4,4,'#94aeb9')
    p.save('icon_settings',None,'icon')
    p=Sprite(40,48)
    p.poly([(5,2),(15,2),(15,4),(18,4),(18,11),(15,11),(15,14),
            (12,14),(12,17),(7,17),(7,12),(11,12),(11,9),(13,9),
            (13,7),(6,7),(6,10),(2,10),(2,5),(5,5)],'#428ba3')
    p.rect(7,20,5,4,'#428ba3')
    p.save('icon_unknown',None,'icon')


def shadow(name,w,h):
    p=Sprite(w,h)
    p.poly([(9,2),(p.w-9,2),(p.w-9,5),(p.w-2,5),(p.w-2,8),
            (p.w-1,8),(p.w-1,p.h-5),(p.w-8,p.h-5),(p.w-8,p.h-2),
            (10,p.h-2),(10,p.h-4),(2,p.h-4),(2,5),(9,5)],'#8e9ea5')
    p.rect(18,1,p.w-36,4,'#adb9b9')
    p.save(name,None,'shadow')


def composite(background, name, pos):
    background.alpha_composite(Image.open(OUT/f'{name}.png'),pos)


def text_at(canvas,text,pos,size,color=N,bold=True):
    font=ImageFont.truetype(str(ROOT/'Assets'/'Fonts'/('Galmuri11-Bold.ttf' if bold else 'Galmuri11.ttf')),size//2)
    temp=Image.new('RGBA',(500,80))
    # Embedded pixel glyph masks at the native font scale.
    ImageDraw.Draw(temp).text((0,0),text,font=font,fill=color,stroke_width=0)
    bbox=temp.getbbox()
    if bbox:
        temp=temp.crop(bbox)
        canvas.alpha_composite(temp.resize((temp.width*2,temp.height*2),Image.Resampling.NEAREST),pos)


def cached_sprite(relative,key=None):
    cache=Path(os.environ['LOCALAPPDATA'])/'DeskPokemon'/'sprites'
    path=cache/relative
    data=json.loads(path.with_suffix('.json').read_text(encoding='utf-8-sig'))
    frames=data.get('textures',[data])[0].get('frames')
    if isinstance(frames,dict):
        frame=frames.get(str(key)+'.png',frames.get(str(key))) if key is not None else next(iter(frames.values()))
    else:
        frame=next((v for v in frames if Path(v['filename']).stem==str(key)),None) if key is not None else frames[0]
    if frame is None:
        return None
    box=frame['frame']
    im=Image.open(path.with_suffix('.png')).convert('RGBA').crop((box['x'],box['y'],box['x']+box['w'],box['y']+box['h']))
    return im.crop(im.getbbox())


def fit_sprite(canvas,im,box,silhouette=False):
    if im is None:
        return
    x,y,w,h=box
    ratio=min(w/im.width,h/im.height)
    # Preview runtime sprites at an integer scale, like the app renderer.
    ratio=max(1,int(ratio))
    im=im.resize((im.width*ratio,im.height*ratio),Image.Resampling.NEAREST)
    if silhouette:
        solid=Image.new('RGBA',im.size,'#123d53')
        solid.putalpha(im.getchannel('A'))
        im=solid
    canvas.alpha_composite(im,(x+(w-im.width)//2,y+h-im.height))


def checker(im):
    base=Image.new('RGBA',im.size,'#303a43')
    d=ImageDraw.Draw(base)
    for y in range(0,im.height,24):
        for x in range(0,im.width,24):
            if (x//24+y//24)%2:
                d.rectangle((x,y,x+23,y+23),fill='#394550')
    return Image.alpha_composite(base,im).convert('RGB')


def preview():
    blank=Image.new('RGBA',(1246,1263))
    for name,pos in layers:
        composite(blank,name,tuple(pos))
    xs=[288,398,504,610,716,822]
    ys=[590,696,802,908]
    for row,y in enumerate(ys):
        for col,x in enumerate(xs):
            composite(blank,'slot_selected' if (row,col)==(0,0) else
                      'slot_unknown' if row==3 else 'slot_normal',(x,y))
    # Empty panel composition: no icon, text, sprite or checkbox baked in.
    blank.save(DESIGN/'pixel-ui-redrawn-transparent.png')
    checker(blank).save(DESIGN/'pixel-ui-redrawn-checker.png')
    demo=blank.copy()
    for name,pos in [('icon_dex',(315,441)),('icon_box',(547,441)),('icon_settings',(775,441)),
                     ('arrow_left',(293,522)),('arrow_right',(473,522)),('checkbox_off',(545,524)),
                     ('pet_shadow',(375,292)),('egg_shadow',(729,305))]:
        composite(demo,name,pos)
    for i in range(4):
        composite(demo,'exp_fill_segment',(388+36*i,367))
    for text,pos,size,col in [
        ('도감',(378,451),32,N),('박스',(609,451),32,'#fff9e7'),('설정',(839,451),32,'#fff9e7'),
        ('1세대',(349,526),32,N),('보유만',(597,529),30,N),('보유 1/1025',(763,530),28,N),
        ('파이리',(440,1052),32,N),('불꽃',(569,1056),26,'#fff9e7'),('No. 0004',(789,1053),24,N),
        ('꼬리의 불꽃은',(440,1103),28,N),('기분이 좋아지면 더 세차게 탄다.',(440,1140),28,N),
        ('파이리',(305,324),32,N),('Lv. 1',(565,326),30,N),('EXP',(311,367),28,N),('30:00',(768,354),32,N)]:
        text_at(demo,text,pos,size,col)
    try:
        pet=cached_sprite('4')
        fit_sprite(demo,pet,(395,158,166,157))
        fit_sprite(demo,pet,(305,1055,104,109))
        egg=cached_sprite('egg/egg','egg')
        if egg is None:
            egg=cached_sprite('egg/egg')
        fit_sprite(demo,egg,(744,206,112,121))
        ids=[4,1,7,25,133,39,94,143,95,151,150,35,6,9,130,68,3,132]
        for i,dex in enumerate(ids):
            fit_sprite(demo,cached_sprite('pokemon_icons_1',dex),
                       (xs[i%6]+10,ys[i//6]+8,84,86),i>=11)
        for x in xs:
            composite(demo,'icon_unknown',(x+32,933))
    except (OSError,KeyError,StopIteration) as exc:
        print('Optional cached artwork unavailable:',exc)
    demo.save(DESIGN/'pixel-ui-reference-demo.png')
    checker(demo).save(DESIGN/'pixel-ui-reference-demo-checker.png')
    sheet=Image.new('RGB',(1180,1320),'#202b36')
    d=ImageDraw.Draw(sheet)
    font=ImageFont.truetype(str(ROOT/'Assets'/'Fonts'/'Galmuri11.ttf'),16)
    for i,(name,info) in enumerate(assets.items()):
        x=20+(i%4)*292
        y=20+(i//4)*155
        im=Image.open(OUT/f'{name}.png')
        factor=min(1,270/im.width,112/im.height)
        if factor<1:
            im=im.resize((int(im.width*factor),int(im.height*factor)),Image.Resampling.NEAREST)
        tile=checker(Image.new('RGBA',(272,116))).convert('RGBA')
        tile.alpha_composite(im,((272-im.width)//2,(116-im.height)//2))
        sheet.paste(tile.convert('RGB'),(x,y))
        d.text((x,y+120),name,font=font,fill='#e1edf0')
    sheet.save(DESIGN/'pixel-ui-assets-contact-sheet.png')


def main():
    OUT.mkdir(parents=True,exist_ok=True)
    frame(); rail()
    tab('tab_selected',218,True,(280,419))
    tab('tab_box',228,False,(494,419))
    tab('tab_settings',232,False,(718,419))
    toolbar(); grid(); detail(); status(); arrows(); checkbox()
    for state in ['normal','selected','empty','unknown','hover']:
        slot('slot_'+state,state)
    icons(); shadow('pet_shadow',192,34); shadow('egg_shadow',144,36)
    preview()
    (OUT/'manifest.json').write_text(json.dumps({
        'version':2,'reference_canvas':[1246,1263],'pixel_grid':2,
        'generator':'design/draw_reference_pixel_ui.py',
        'method':'Measured reference geometry, newly plotted pixels; source screenshot is never loaded into components.',
        'assets':assets,'font':'Assets/Fonts/Galmuri11-Bold.ttf',
        'slot_columns':[288,398,504,610,716,822],'slot_rows':[590,696,802,908],
        'preview_content':'Text and cached runtime Pokemon sprites appear only in design/pixel-ui-reference-demo.png'
    },ensure_ascii=False,indent=2),encoding='utf-8')
    print(f'Wrote {len(assets)} independent RGBA components')


if __name__=='__main__':
    main()
