"""Run one of the Snow style-library scripts inside Blender (headless).

blender -b --factory-startup --python tools/snow_style/run_bpy.py -- <script.py> [args...]

The script is looked up in tools/snow_style/lib (or given as a path). lib/ goes first on sys.path (the scripts import each
other by name, as in the Blender chat's sandbox), pydeps/ (scipy, Pillow for Blender's Python 3.13) goes last so Blender's
own numpy stays in charge. sys.argv is set to [script, args...] as when the chat ran `python3 -I script args`."""
import os, sys, runpy

HERE = os.path.dirname(os.path.abspath(__file__))
LIB = os.path.join(HERE, 'lib')
argv = sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else []
if not argv:
    raise SystemExit('usage: blender -b --python run_bpy.py -- <script.py> [args]')
script = argv[0] if os.path.isfile(argv[0]) else os.path.join(LIB, argv[0])
sys.path.insert(0, os.path.dirname(os.path.abspath(script)))
if LIB not in sys.path:
    sys.path.insert(1, LIB)
sys.path.append(os.path.join(HERE, 'pydeps'))
sys.argv = [script] + argv[1:]
os.chdir(os.path.dirname(os.path.abspath(script)))
runpy.run_path(script, run_name='__main__')
