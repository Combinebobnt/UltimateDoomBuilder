
#region ================== Copyright (c) 2007 Pascal vd Heiden

/*
 * Copyright (c) 2007 Pascal vd Heiden, www.codeimp.com
 * This program is released under GNU General Public License
 * 
 * This program is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
 * GNU General Public License for more details.
 * 
 */

#endregion

#region ================== Namespaces

using System;
using CodeImp.DoomBuilder.Geometry;

#endregion

namespace CodeImp.DoomBuilder.Rendering
{
    internal unsafe sealed class Plotter : IDisposable
    {
        #region ================== Constants

        private const int DASH_INTERVAL = 16; //mxd

        #endregion

        #region ================== Variables

        // Memory
        private PixelColor[] pixels;
        private int width;
        private int height;
        private int visiblewidth;
        private int visibleheight;
        // GL
        public Texture Texture { get; private set; }

        #endregion

        #region ================== Properties

        public int VisibleWidth { get { return visiblewidth; } }
        public int VisibleHeight { get { return visibleheight; } }
        public int Width { get { return width; } }
        public int Height { get { return height; } }

        #endregion

        #region ================== Constructor / Disposer

        // Constructor
        public Plotter(int width, int height) : this(width, height, true) { }

        // Without a texture the plotter only fills its pixel buffer (tests)
        internal Plotter(int width, int height, bool createtexture)
        {
            // Initialize
            if (createtexture) Texture = new Texture(width, height, TextureFormat.Bgra8);
            this.pixels = new PixelColor[width*height];
            this.width = width;
            this.height = height;
            this.visiblewidth = width;
            this.visibleheight = height;
        }

        public void Dispose()
        {
            if (Texture != null)
            {
                Texture.Dispose();
                Texture = null;
            }
        }

        #endregion

        #region ================== Pixel Rendering

        private int TransformY(int y)
        {
            return height - y;
        }

        // Pixel at buffer coordinates (rows top-down, after TransformY)
        internal PixelColor GetPixel(int x, int y)
        {
            return pixels[y * width + x];
        }

        // This clears all pixels black
        public void Clear()
        {
            // Clear memory
            fixed(PixelColor* pixel = pixels)
            {
                uint* op = (uint*)pixel;
                for (int i = 0; i < pixels.Length; i++)
                {
                    *op = 0;
                    op++;
                }
            }
        }

        // This draws a pixel normally
        public void DrawPixelSolid(int x, int y, ref PixelColor c)
        {
            y = TransformY(y);

            // Draw pixel when within range
            if ((x >= 0) && (x < visiblewidth) && (y >= 0) && (y < visibleheight))
                pixels[y * width + x] = c;
        }

        // This draws a pixel normally
        public void DrawVertexSolid(int x, int y, int size, ref PixelColor c, ref PixelColor l, ref PixelColor d)
        {
            y = TransformY(y);

            int x1 = x - size;
            int x2 = x + size;
            int y1 = y - size;
            int y2 = y + size;

            // Do unchecked?
            if ((x1 >= 0) && (x2 < visiblewidth) && (y1 >= 0) && (y2 < visibleheight))
            {
                // Filled square
                for (int yp = y1; yp <= y2; yp++)
                    for (int xp = x1; xp <= x2; xp++)
                        pixels[yp * width + xp] = c;

                if (!General.Settings.FlatShadeVertices)
                {
                    // Vertical edges
                    for (int yp = y1 + 1; yp <= y2 - 1; yp++)
                    {
                        pixels[yp * width + x1] = l;
                        pixels[yp * width + x2] = d;
                    }

                    // Horizontal edges
                    for (int xp = x1 + 1; xp <= x2 - 1; xp++)
                    {
                        pixels[y1 * width + xp] = l;
                        pixels[y2 * width + xp] = d;
                    }

                    // Corners
                    pixels[y2 * width + x2] = d;
                    pixels[y1 * width + x1] = l;
                }
            }
            else
            {
                // Clipped: only the parts inside the visible area
                int cx1 = Math.Max(x1, 0);
                int cx2 = Math.Min(x2, visiblewidth - 1);
                int cy1 = Math.Max(y1, 0);
                int cy2 = Math.Min(y2, visibleheight - 1);
                if ((cx1 > cx2) || (cy1 > cy2)) return;

                for (int yp = cy1; yp <= cy2; yp++)
                    for (int xp = cx1; xp <= cx2; xp++)
                        pixels[yp * width + xp] = c;

                if (!General.Settings.FlatShadeVertices)
                {
                    int ey1 = Math.Max(y1 + 1, cy1);
                    int ey2 = Math.Min(y2 - 1, cy2);
                    int ex1 = Math.Max(x1 + 1, cx1);
                    int ex2 = Math.Min(x2 - 1, cx2);

                    // Vertical edges
                    for (int yp = ey1; yp <= ey2; yp++)
                    {
                        if (x1 == cx1) pixels[yp * width + x1] = l;
                        if (x2 == cx2) pixels[yp * width + x2] = d;
                    }

                    // Horizontal edges
                    for (int xp = ex1; xp <= ex2; xp++)
                    {
                        if (y1 == cy1) pixels[y1 * width + xp] = l;
                        if (y2 == cy2) pixels[y2 * width + xp] = d;
                    }

                    // Corners
                    if ((x2 == cx2) && (y2 == cy2)) pixels[y2 * width + x2] = d;
                    if ((x1 == cx1) && (y1 == cy1)) pixels[y1 * width + x1] = l;
                }
            }
        }

        // This draws a dotted grid line horizontally
        public void DrawGridLineH(int y, int x1, int x2, ref PixelColor c)
        {
            y = TransformY(y);

            int numpixels = visiblewidth >> 1;
            int offset = y & 0x01;
            int ywidth = y * width;
            x1 = General.Clamp(x1 >> 1, 0, numpixels - 1);
            x2 = General.Clamp(x2 >> 1, 0, numpixels - 1);

            if ((y >= 0) && (y < height))
            {
                // Draw all pixels on this line
                for (int i = x1; i < x2; i++) pixels[ywidth + ((i << 1) | offset)] = c;
            }
        }

        // This draws a dotted grid line vertically
        public void DrawGridLineV(int x, int y1, int y2, ref PixelColor c)
        {
            y1 = TransformY(y1);
            y2 = TransformY(y2);

            int numpixels = visibleheight >> 1;
            int offset = x & 0x01;
            y1 = General.Clamp(y1 >> 1, 0, numpixels - 1);
            y2 = General.Clamp(y2 >> 1, 0, numpixels - 1);

            if ((x >= 0) && (x < width))
            {
                // Draw all pixels on this line
                for (int i = y2; i < y1; i++) pixels[((i << 1) | offset) * width + x] = c;
            }
        }

        // This draws a pixel alpha blended
        public void DrawPixelAlpha(int x, int y, ref PixelColor c)
        {
            y = TransformY(y);

            fixed (PixelColor* pixels = this.pixels)
            {
                // Draw only when within range
                if ((x >= 0) && (x < visiblewidth) && (y >= 0) && (y < visibleheight))
                {
                    // Get the target pixel
                    PixelColor* p = pixels + (y * width + x);

                    // Not drawn on target yet?
                    if (*(int*)p == 0)
                    {
                        // Simply apply color to pixel
                        *p = c;
                    }
                    else
                    {
                        // Blend with pixel
                        float a = c.a * 0.003921568627450980392156862745098f;
                        if (p->a + c.a > 255) p->a = 255; else p->a += c.a;
                        p->r = (byte)(p->r * (1f - a) + c.r * a);
                        p->g = (byte)(p->g * (1f - a) + c.g * a);
                        p->b = (byte)(p->b * (1f - a) + c.b * a);
                    }
                }
            }
        }

        // This draws a line normally
        // See: http://en.wikipedia.org/wiki/Bresenham%27s_line_algorithm
        public void DrawLineSolid(int x1, int y1, int x2, int y2, ref PixelColor c, uint mask = 0xffffffff, int thickness = 1)
        {
            y1 = TransformY(y1);
            y2 = TransformY(y2);

            if (thickness > 1)
            {
                DrawLineThick(x1, y1, x2, y2, ref c, mask, thickness);
                return;
            }

            // Check if the line is outside the screen for sure.
            // This is quickly done by checking in which area both points are. When this
            // is above, below, right or left of the screen, then skip drawing the line.
            if (((x1 < 0) && (x2 < 0)) ||
               ((x1 > visiblewidth) && (x2 > visiblewidth)) ||
               ((y1 < 0) && (y2 < 0)) ||
               ((y1 > visibleheight) && (y2 > visibleheight))) return;

            // Distance of the line
            int dx = x2 - x1;
            int dy = y2 - y1;

            // Positive (absolute) distance
            int dxabs = Math.Abs(dx);
            int dyabs = Math.Abs(dy);

            // Half distance
            int x = dyabs >> 1;
            int y = dxabs >> 1;

            // Direction
            int sdx = Math.Sign(dx);
            int sdy = Math.Sign(dy);

            // Start position
            int px = x1;
            int py = y1;

            // When the line is completely inside screen,
            // then do an unchecked draw, because all of its pixels are
            // guaranteed to be within the memory range
            if ((x1 >= 0) && (x2 >= 0) && (x1 < visiblewidth) && (x2 < visiblewidth) &&
               (y1 >= 0) && (y2 >= 0) && (y1 < visibleheight) && (y2 < visibleheight))
            {
                // Draw first pixel
                pixels[py * width + px] = c;

                // Check if the line is more horizontal than vertical
                if (dxabs >= dyabs)
                {
                    for (int i = 0; i < dxabs; i++)
                    {
                        y += dyabs;
                        if (y >= dxabs)
                        {
                            y -= dxabs;
                            py += sdy;
                        }
                        px += sdx;

                        // Draw pixel
                        if ((mask & (1 << (i & 0x7))) != 0)
                        {
                            pixels[py * width + px] = c;
                        }
                    }
                }
                // Else the line is more vertical than horizontal
                else
                {
                    for (int i = 0; i < dyabs; i++)
                    {
                        x += dxabs;
                        if (x >= dyabs)
                        {
                            x -= dyabs;
                            px += sdx;
                        }
                        py += sdy;

                        // Draw pixel
                        if ((mask & (1 << (i & 0x7))) != 0)
                        {
                            pixels[py * width + px] = c;
                        }
                    }
                }
            }
            else
            {
                // Draw first pixel
                if ((px >= 0) && (px < visiblewidth) && (py >= 0) && (py < visibleheight))
                    pixels[py * width + px] = c;

                // Check if the line is more horizontal than vertical
                if (dxabs >= dyabs)
                {
                    for (int i = 0; i < dxabs; i++)
                    {
                        y += dyabs;
                        if (y >= dxabs)
                        {
                            y -= dxabs;
                            py += sdy;
                        }
                        px += sdx;

                        // Draw pixel
                        if ((mask & (1 << (i & 0x7))) != 0)
                        {
                            if ((px >= 0) && (px < visiblewidth) && (py >= 0) && (py < visibleheight))
                                pixels[py * width + px] = c;
                        }
                    }
                }
                // Else the line is more vertical than horizontal
                else
                {
                    for (int i = 0; i < dyabs; i++)
                    {
                        x += dxabs;
                        if (x >= dyabs)
                        {
                            x -= dyabs;
                            px += sdx;
                        }
                        py += sdy;

                        // Draw pixel
                        if ((mask & (1 << (i & 0x7))) != 0)
                        {
                            if ((px >= 0) && (px < visiblewidth) && (py >= 0) && (py < visibleheight))
                                pixels[py * width + px] = c;
                        }
                    }
                }
            }
        }

        // Bresenham along the major axis, filling a span across the minor axis at each step so the
        // stroke is about thickness px wide perpendicular to the line. Coordinates are already transformed.
        private void DrawLineThick(int x1, int y1, int x2, int y2, ref PixelColor c, uint mask, int thickness)
        {
            int dx = x2 - x1;
            int dy = y2 - y1;
            int dxabs = Math.Abs(dx);
            int dyabs = Math.Abs(dy);
            bool xmajor = (dxabs >= dyabs);
            int major = xmajor ? dxabs : dyabs;
            int minor = xmajor ? dyabs : dxabs;

            // Span across the minor axis; even spans put the extra pixel on the positive side (down or right)
            int span = (major == 0) ? thickness : Math.Max(1, (int)Math.Round(thickness * Math.Sqrt((double)dx * dx + (double)dy * dy) / major));
            int lo = -(span - 1) / 2;
            int hi = span / 2;

            // Outside the screen for sure, allowing for the span
            if (((x1 < -span) && (x2 < -span)) ||
               ((x1 > visiblewidth + span) && (x2 > visiblewidth + span)) ||
               ((y1 < -span) && (y2 < -span)) ||
               ((y1 > visibleheight + span) && (y2 > visibleheight + span))) return;

            int majstart = xmajor ? x1 : y1;
            int minstart = xmajor ? y1 : x1;
            int smaj = Math.Sign(xmajor ? dx : dy);
            int smin = Math.Sign(xmajor ? dy : dx);
            int majlimit = xmajor ? visiblewidth : visibleheight;
            int minlimit = xmajor ? visibleheight : visiblewidth;

            // Only walk the steps whose major coordinate is on screen (spans run across the minor axis)
            long kfirst, klast;
            if (smaj > 0) { kfirst = -(long)majstart; klast = (long)majlimit - 1 - majstart; }
            else if (smaj < 0) { kfirst = (long)majstart - (majlimit - 1); klast = majstart; }
            else { kfirst = 0; klast = 0; }
            kfirst = Math.Max(kfirst, 0);
            klast = Math.Min(klast, major);
            if (kfirst > klast) return;

            // Bresenham state after kfirst steps: the error term starts at major / 2 and gains minor per step
            int pmin = minstart;
            long err = 0;
            if (major > 0)
            {
                long acc = (major >> 1) + kfirst * (long)minor;
                pmin = minstart + smin * (int)(acc / major);
                err = acc % major;
            }

            for (long k = kfirst; k <= klast; k++)
            {
                // The first pixel always draws; step k uses mask bit (k - 1), as the thin line does
                if ((k == 0) || ((mask & (1u << (int)((k - 1) & 0x7))) != 0))
                {
                    int pmaj = majstart + smaj * (int)k;
                    if ((pmaj >= 0) && (pmaj < majlimit))
                    {
                        int m1 = Math.Max(pmin + lo, 0);
                        int m2 = Math.Min(pmin + hi, minlimit - 1);
                        if (xmajor)
                            for (int m = m1; m <= m2; m++) pixels[m * width + pmaj] = c;
                        else
                            for (int m = m1; m <= m2; m++) pixels[pmaj * width + m] = c;
                    }
                }

                err += minor;
                if (err >= major)
                {
                    err -= major;
                    pmin += smin;
                }
            }
        }

        //mxd
        public void DrawLine3DFloor(int x1, int y1, int x2, int y2, ref PixelColor c, PixelColor c2, int thickness = 1)
        {
			Line2D line = new Line2D(x1, y1, x2, y2);
			double length = line.GetLength();

            if (length < DASH_INTERVAL * 2)
            {
                DrawLineSolid(x1, y1, x2, y2, ref c2, 0xffffffff, thickness);
            }
            else
            {
				double d1 = DASH_INTERVAL / length;
				double d2 = 1.0f - d1;

				
				Vector2D p1 = line.GetCoordinatesAt(d1);
				Vector2D p2 = line.GetCoordinatesAt(d2);

                DrawLineSolid(x1, y1, (int)p1.x, (int)p1.y, ref c2, 0xffffffff, thickness);
                DrawLineSolid((int)p1.x, (int)p1.y, (int)p2.x, (int)p2.y, ref c, 0xffffffff, thickness);
                DrawLineSolid((int)p2.x, (int)p2.y, x2, y2, ref c2, 0xffffffff, thickness);
            }
        }

        #endregion

        #region ================== Drawing to rendertarget

        public void DrawContents(RenderDevice graphics)
        {
            // set pixels of texture
            // convert from pixelcolor to uint
            fixed (PixelColor* pixels = this.pixels)
            {
                uint* uintpixels = (uint*)pixels;
                uint* targetpixels = (uint*)graphics.MapPBO(Texture);
                for (int i = 0; i < this.pixels.Length; i++)
                    *targetpixels++ = *uintpixels++;
                graphics.UnmapPBO(Texture);
            }
        }

        #endregion
    }
}
