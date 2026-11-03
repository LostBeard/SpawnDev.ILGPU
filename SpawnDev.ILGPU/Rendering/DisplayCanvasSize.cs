using SpawnDev.SpawnJS.JSObjects;

namespace SpawnDev.ILGPU.Rendering
{
    /// <summary>
    /// Keeps an attached display canvas's drawing-buffer size equal to the size of the frames presented to it.
    /// </summary>
    /// <remarks>
    /// A canvas's drawing buffer (<c>width</c> / <c>height</c> attributes) defaults to 300x150 and is independent of its CSS size.
    /// Every renderer draws the frame at its natural size at (0, 0), so a 960x889 frame on a default canvas showed only its
    /// top-left 300x150, stretched by CSS (Riker, AssistZ, 2026-11-02). The renderers now size the canvas to the frame; CSS still
    /// decides the displayed size. Setting a canvas's size clears it, so this runs only when the frame size changes, before drawing.
    /// The canvas handle is taken from the renderer's own 2D context (<c>ctx.canvas</c>) and owned here, because callers may dispose
    /// the <see cref="HTMLCanvasElement"/> wrapper they passed to <c>AttachCanvas</c> right after attaching (documented as fine).
    /// </remarks>
    internal struct DisplayCanvasSize
    {
        private HTMLCanvasElement? _canvas;
        private int _width, _height;

        /// <summary>Starts tracking the canvas of <paramref name="context"/>; the next <see cref="Ensure"/> sizes it.</summary>
        public void Attach(CanvasRenderingContext2D? context)
        {
            Detach();
            _canvas = context?.Canvas;
        }

        /// <summary>Stops tracking and releases this struct's own handle (the canvas element itself is untouched).</summary>
        public void Detach()
        {
            _canvas?.Dispose();
            _canvas = null;
            _width = 0;
            _height = 0;
        }

        /// <summary>Sets the canvas's drawing-buffer size to <paramref name="width"/> x <paramref name="height"/> if it changed.</summary>
        public void Ensure(int width, int height)
        {
            if (_canvas == null || (width == _width && height == _height)) return;
            if (_canvas.Width != width) _canvas.Width = width;
            if (_canvas.Height != height) _canvas.Height = height;
            _width = width;
            _height = height;
        }
    }
}
