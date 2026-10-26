"""One sheet of the Cycles previews: rend/start/*.png -> rend/start/forest_start_A.jpg"""
import os, sys
from PIL import Image, ImageDraw, ImageFont
HERE = os.path.dirname(os.path.abspath(__file__))
R = os.path.join(HERE, 'rend', 'start')
F = ImageFont.truetype('/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf', 17)
FB = ImageFont.truetype('/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf', 22)
CELLS = [('booth', 'StartBooth — будка поста, табличка, лампа над дверью'),
         ('booth_in', 'Внутри: столик под окном (Slot_Table_1..3), полка (Slot_Shelf); IX_Door открыта 100°'),
         ('car', 'StartCar — универсал 80-х, IX_CarDoor (водительская) открыта'),
         ('car_rear', 'Сзади справа спущенное колесо, хвоя и грязь'),
         ('props', 'StartLamp (тренога + аккумулятор), StartTrash, StartTyres'),
         ('night', 'Ночь: LIGHT_Booth и LIGHT_Flood включены (раскладка только для превью)')]
W, H, CAP, PAD = 1100, 700, 34, 10
sheet = Image.new('RGB', (2 * W + 3 * PAD, 3 * (H + CAP) + 4 * PAD + 44), (24, 24, 26))
d = ImageDraw.Draw(sheet)
d.text((PAD, 12), 'forest_start 1.0 — брошенный пост у ворот SPERRGEBIET', font=FB, fill=(235, 235, 235))
for i, (nm, cap) in enumerate(CELLS):
    x = PAD + (i % 2) * (W + PAD)
    y = 44 + PAD + (i // 2) * (H + CAP + PAD)
    sheet.paste(Image.open(os.path.join(R, nm + '.png')).convert('RGB'), (x, y))
    d.text((x + 4, y + H + 7), cap, font=F, fill=(220, 220, 220))
out = os.path.join(R, 'forest_start_A.jpg')
sheet.save(out, quality=90)
print(out, sheet.size)
