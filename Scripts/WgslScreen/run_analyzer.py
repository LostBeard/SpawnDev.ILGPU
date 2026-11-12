# Runs wgsl_inline.py (imported, unmodified) over every .wgsl in a folder.
# Done in-process because the file list for a few hundred shaders would exceed the Windows command-line length.
# Loaded by path with importlib because `python -I` leaves the script folder off sys.path.
# usage: python -I run_analyzer.py <folder> <outfile>
import sys, os, glob, importlib.util, contextlib

# Without this the import writes __pycache__ next to the script, i.e. into the repo.
sys.dont_write_bytecode = True

ANALYZER = os.path.join(os.path.dirname(os.path.abspath(__file__)), "wgsl_inline.py")
folder, outfile = sys.argv[1], sys.argv[2]
spec = importlib.util.spec_from_file_location("wgsl_inline", ANALYZER)
mod = importlib.util.module_from_spec(spec)
spec.loader.exec_module(mod)
files = sorted(glob.glob(os.path.join(folder, "*.wgsl")))
if not files:
    sys.exit("analyzer: no .wgsl files in " + folder)
sys.argv = ["wgsl_inline.py", "--detail", "0"] + files
with open(outfile, "w", encoding="utf-8") as fh, contextlib.redirect_stdout(fh):
    mod.main()
print("analyzer: %d files -> %s" % (len(files), outfile))
