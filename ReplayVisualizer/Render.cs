using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Forms;
//Maybe I shouldn't use System.Drawing (it's kind of windows dependent)
using System.Drawing;
using Accord.Video.FFMPEG;
using Accord.Video;
using System.Drawing.Drawing2D;
using AForge.Imaging.Filters;

//Mutithreading!
using System.Threading;

namespace ReplayVisualizer.Video
{
    static class Render
    {
        static Mutex writerMut = new Mutex();
        static VideoFileWriter writer;
        static Bitmap[] bitmapBuffer;
        static long bitmapBufferIndex;

        static MetaData meta;
        static Ship[] ships;
        static ShipIcons shipIcons;

        static List<IRenderPortion> renderPortions;
        //static Form form; //Form is for live rendering, coming later
        /// <summary>
        /// Sets the values of Render's fields and opens the render window. Call after Program.ProcessFile() finishes
        /// </summary>
        public static void Init()
        {
            meta = Program.meta;
            ships = Program.shipList.Values.ToArray();

            foreach (Ship s in ships)
            {
                Console.WriteLine(s + "\n");
            }

            shipIcons = new ShipIcons();
            shipIcons.Init();
        }
        /// <summary>
        /// Draws a rectangle on the Graphics object with a rotation of angle around its center
        /// </summary>
        /// <param name="angle">The angle of rotation in degrees</param>
        public static void RotateRectangle(Graphics g, RectangleF r, float angle, Brush b)
        {
            using (Matrix m = new Matrix())
            {
                m.RotateAt(angle, new PointF(r.Left + (r.Width / 2),
                                          r.Top + (r.Height / 2)));
                g.Transform = m;
                //g.DrawRectangle(Pens.Black, r);
                g.FillRectangle(b, r);
                g.ResetTransform();
            }
        }
        public static void RotateImage(Graphics g, Image i, RectangleF r, float angle)
        {
            using (Matrix m = new Matrix())
            {
                m.RotateAt(angle, new PointF(r.Left + (r.Width / 2),
                                          r.Top + (r.Height / 2)));
                g.Transform = m;
                g.DrawImage(i, r);
                g.ResetTransform();
            }
        }

        private static long previousPrintedPercent = long.MinValue;
        /// <summary>
        /// 
        /// </summary>
        /// <param name="b"></param>
        /// <param name="index"></param>
        /// <returns>The difference between the parameter index and bitmapBufferIndex. Expressed as index - bitmapBufferIndex, calculated after writing any frames to the stream writer</returns>
        private static long PushBitmapToVideoAsync(Bitmap b, long index)
        {
            writerMut.WaitOne();
            if (index >= bitmapBuffer.Length)
            {
                writerMut.ReleaseMutex();
                Console.WriteLine($"Attempted writing out of bounds of bitmap buffer size: attempted index {index} with buffer size {bitmapBuffer.Length}");
                return index - bitmapBufferIndex;
            }

            bitmapBuffer[index] = b;

            System.IO.TextWriter consoleWriter = Console.Out;
            Console.SetOut(System.IO.TextWriter.Null);

            //Write all valid frames from the front of the buffer
            while (bitmapBufferIndex < bitmapBuffer.Length && bitmapBuffer[bitmapBufferIndex] != null)
            {
                writer.WriteVideoFrame(bitmapBuffer[bitmapBufferIndex]);
                bitmapBuffer[bitmapBufferIndex].Dispose();
                bitmapBuffer[bitmapBufferIndex] = null;
                bitmapBufferIndex++;
            }
            Console.SetOut(consoleWriter);

            long percentProgress = bitmapBufferIndex * 100 / bitmapBuffer.Length;
            if (percentProgress >= previousPrintedPercent + 5)
            {
                Console.WriteLine($"{percentProgress}%");
                previousPrintedPercent = percentProgress;
            }

            //The distance between the current
            long indexGap = index - bitmapBufferIndex;

            writerMut.ReleaseMutex();

            return index - bitmapBufferIndex;
        }

        private static void RenderVideoPortion(MetaData meta, Ship[] ships, ShipIcons shipIcons, List<IRenderPortion> renderPortions, int width, double framerate, double timeScale, int frameOffset, int numThreads)
        {
            double frameScale = width / 100.0;

            //double segmentLength = end - start;
            //int totalFrames = (int)(segmentLength / timeScale * framerate);
            int totalFrames = (int)(meta.replayLength / timeScale * framerate);

            //int totalFramesOffset = (int)(start / timeScale * framerate);

            for (int i = frameOffset; i < totalFrames; i += numThreads)
            {
                //Console.WriteLine(i + totalFramesOffset);
                double t = i / framerate * timeScale;
                long currentFrame = i;

                Bitmap frame = new Bitmap(width, width);
                Graphics g = Graphics.FromImage(frame);

                foreach (IRenderPortion rp in renderPortions)
                {
                    //In reality, scaling is more complex. Will be fixed later along with the bin packing solution
                    Rectangle r = new Rectangle(0, 0, (int)(frameScale * rp.PortionSize.x), (int)(frameScale * rp.PortionSize.y));
                    rp.Draw(g, r, meta, ships, shipIcons, t);
                }

                //Write video frame
                //numAhead is the number of frames ahead of the video stream writing that this portion is at. Takes into numThreads so thread counts > 50 don't break this (not that they should be used)
                //This makes any threads which are far ahead of the slowest thread have to slow down so there isn't an increase in memory usage because of that distance
                long numAhead = PushBitmapToVideoAsync(frame, currentFrame) - numThreads;
                if (numAhead > 50)
                    Thread.Sleep((int)(numAhead / 10));
            }

            Console.WriteLine($"Thread index {frameOffset} finished");
        }

        /// <summary>
        /// Renders the game with a minimap view. Render threads compute render frames in a striped pattern --
        /// For example, if you had 10 frames rendered by three threads, the frames rendered by each thread would be as follows: T1:[0, 3, 6, 9] T2: [1, 4, 7] T3: [2, 5, 8]
        /// </summary>
        /// <param name="fileName">Path to final output video</param>
        /// <param name="codec">Codec for rendered video</param>
        /// <param name="width">The side length of the video frame. Always renders as a square with side lengths width x width</param>
        /// <param name="framerate">The output framerate in FPS</param>
        /// <param name="timeScale">Multiplier for game speed, a value of 6.0 would make a 10 minute (600 second) game create a video lasting 100 seconds (6000 frames at 60 FPS)</param>
        /// <param name="numThreads">Number of CPU threads to dedicate to the render</param>
        public static void RenderVideo(string fileName, VideoCodec codec, int width, double framerate, double timeScale, int numThreads)
        {
            long startTime = Utils.CurrentTimeMilliseconds();
            double frameScale = width / 100.0;

            //Create and add render portions
            {
                renderPortions = new List<IRenderPortion>
                {
                    new MinimapRenderPortion(frameScale)
                };
            }

            //8MB to bytes
            int videoSize = (int)((7.5 * 1000.0 * 1000.0) * 8.0);
            int bitrate = (int)(videoSize / (meta.replayLength / timeScale));

            //Open video stream
            writer = new VideoFileWriter();
            writer.Open(fileName, width, width, new Accord.Math.Rational(framerate), codec, bitrate);

            writerMut = new Mutex();

            double timePerThread = meta.replayLength / numThreads;

            int totalFrames = (int)(meta.replayLength / timeScale * framerate);

            bitmapBuffer = new Bitmap[totalFrames];

            Thread[] threads = new Thread[numThreads];
            Bitmap[][] threadReturns = new Bitmap[numThreads][];

            //Borrow mutex so console printing works until all threads have been created
            writerMut.WaitOne();

            for (int i = 0; i < numThreads; i++)
            {
                Console.WriteLine($"Thread init: index={i}");

                //Copy ships
                Ship[] shipsCopy = new Ship[ships.Length];
                ships.CopyTo(shipsCopy, 0);

                //Copy render portions
                List<IRenderPortion> renderPortionsCopy = new List<IRenderPortion>();

                foreach (IRenderPortion rp in renderPortions)
                {
                    renderPortionsCopy.Add(rp.Copy());
                }

                //For the moment, too lazy to implement much faster copy constructor so this is it for now:
                ShipIcons shipIconsCopy = new ShipIcons();
                shipIconsCopy.Init();

                //For help on this, see https://stackoverflow.com/questions/16843510/index-was-outside-the-bounds-of-the-array-while-trying-to-start-multiple-threads

                int index = i;

                int frameOffset = i;
                int w = width;

                threads[i] = new Thread(() => RenderVideoPortion(new MetaData(meta), shipsCopy, shipIconsCopy, renderPortionsCopy, w, framerate, timeScale, frameOffset, numThreads));
                threads[i].Start();
            }

            //Release mutex to allow execution to begin
            writerMut.ReleaseMutex();

            Console.WriteLine("All threads initiated");

            
            //Wait for all the threads
            for (int i = 0; i < numThreads; i++)
            {
                threads[i].Join();
            }

            writer.Close();

            int framesPerThread = totalFrames / numThreads;

            long endTime = Utils.CurrentTimeMilliseconds();
            long renderTime = endTime - startTime;
            double frameTime = renderTime / (double)totalFrames;
            long pixelsPerFrame = width * width;
            Console.WriteLine($"Render finished\nTime taken: {renderTime}ms\nTime taken per frame: {frameTime:N2}ms\nFrames rendered per second: {(1.0 / frameTime * 1000.0):N2}\n" +
                $"Time taken per second: {(frameTime * framerate):N2}ms\nTotal number of frames: {totalFrames}\nFrames per thread: {framesPerThread}\n\n" +
                $"Resolution: {width} x {width}\nPixels per frame: {pixelsPerFrame}\nTotal pixels: {pixelsPerFrame * totalFrames}");
        }

        /// <summary>
        /// Renders the game with a minimap view
        /// </summary>
        /// <param name="fileName">Path to final output video</param>
        /// <param name="codec">Codec for rendered video</param>
        /// <param name="width">The side length of the video frame. Always renders as a square with side lengths width x width</param>
        /// <param name="framerate">The output framerate in FPS</param>
        /// <param name="timeScale">Multiplier for game speed, a value of 6.0 would make a 10 minute (600 second) game create a video lasting 100 seconds (6000 frames at 60 FPS)</param>
        public static void RenderVideo(string fileName, VideoCodec codec, int width, double framerate, double timeScale)
        {
            long startTime = Utils.CurrentTimeMilliseconds();
            //Multiply to take a value from 0-100 and put it in the range 0-width
            double frameScale = width / 100.0;

            //Create and add render portions
            {
                renderPortions = new List<IRenderPortion>
                {
                    new MinimapRenderPortion(frameScale)
                };
            }

            //Solve the Bin Packing Problem in 2D for the render portions. Must take into account a given aspect ratio
            {
                //
            }
            //...

            //Open video stream
            VideoFileWriter writer = new VideoFileWriter();
            writer.Open(fileName, width, width, new Accord.Math.Rational(framerate), codec);

            int totalFrames = (int)(meta.replayLength / timeScale * framerate);


            for (int i = 0; i < totalFrames; i++)
            {
                double t = i / framerate * timeScale; 
                Bitmap frame = new Bitmap(width, width);
                Graphics g = Graphics.FromImage(frame);

                foreach (IRenderPortion rp in renderPortions)
                {
                    //In reality, scaling is more complex. Will be fixed later along with the bin packing solution
                    Rectangle r = new Rectangle(0, 0, (int)(frameScale * rp.PortionSize.x), (int)(frameScale * rp.PortionSize.y));
                    rp.Draw(g, r, meta, ships, shipIcons, t);
                }
                //The WriteVideoFrame call creates console output, so temporarily disable console writing before writing the frame
                System.IO.TextWriter consoleWriter = Console.Out;

                Console.SetOut(System.IO.TextWriter.Null);
                writer.WriteVideoFrame(frame);
                Console.SetOut(consoleWriter);
                //...
                if (i % (totalFrames / 20) == 0)
                    Console.WriteLine($"{(int)(i / (double)totalFrames * 100.0 + 0.9)}%");
            }

            writer.Close();

            long endTime = Utils.CurrentTimeMilliseconds();
            long renderTime = endTime - startTime;
            double frameTime = renderTime / (double)totalFrames;
            long pixelsPerFrame = width * width;
            Console.WriteLine($"Render finished\nTime taken: {renderTime}ms\nTime taken per frame: {frameTime:N2}ms\nFrames rendered per second: {(1.0 / frameTime * 1000.0):N2}\n" +
                $"Time taken per second: {(frameTime * framerate):N2}ms\nTotal number of frames: {totalFrames}\n\n" +
                $"Resolution: {width} x {width}\nPixels per frame: {pixelsPerFrame}\nTotal pixels: {pixelsPerFrame * totalFrames}");
        }
    }
    interface IRenderPortion
    {
        Point2 PortionSize { get; }
        /// <summary>
        /// Draws the portion onto the canvas with the given rectangle (which should be of size PortionSize)
        /// </summary>
        void Draw(Graphics g, Rectangle rect, MetaData meta, Ship[] ships, ShipIcons shipIcons, double t);
        IRenderPortion Copy();
    }
    class MinimapRenderPortion : IRenderPortion
    {
        const string minimapImagePath = "assets/minimaps/";
        /// <summary>
        /// The size, from 0 to 100, of the ship icon itself
        /// </summary>
        const double shipIconBaseWidth = 5.0;
        /// <summary>
        /// The default size (in pixels) of the rendered portion, not taking into account any scaling
        /// </summary>
        public Point2 PortionSize => new Point2(100.0, 100.0);

        Image background;

        /// <summary>
        /// A list of all shots currently being displayed. This always be
        /// </summary>
        List<Shot> activeShots;
        int nextShotIndex;

        int shipIconWidth;
        float shipIconOffset;
        float healthBarWidth;
        float healthBarHeight;
        float healthBarOffsetX;
        float healthBarOffsetY;

        public MinimapRenderPortion(MinimapRenderPortion m)
        {
            background = (Image)m.background.Clone();
            activeShots = new List<Shot>(m.activeShots);
            nextShotIndex = m.nextShotIndex;

            shipIconWidth = m.shipIconWidth;
            shipIconOffset = m.shipIconOffset;
            healthBarWidth = m.healthBarWidth;
            healthBarHeight = m.healthBarHeight;
            healthBarOffsetX = m.healthBarOffsetX;
            healthBarOffsetY = m.healthBarOffsetY;
        }

        public MinimapRenderPortion(double frameScale)
        {
            activeShots = new List<Shot>();
            nextShotIndex = 0;

            {
                string minimapImagePathFull = minimapImagePath + Program.meta.mapName + "/";

                //Background
                Image minimap = Image.FromFile(minimapImagePathFull + "minimap.png");
                Image minimapWater = Image.FromFile(minimapImagePathFull + "minimap_water.png");

                background = minimapWater;
                Graphics g = Graphics.FromImage(background);
                g.DrawImage(minimap, Point.Empty);
            }

            //Intialize render variables
            {
                shipIconWidth = (int)(shipIconBaseWidth * frameScale);
                shipIconOffset = -(float)(shipIconWidth / 2.0);

                healthBarWidth = (int)(3.5 * frameScale);
                healthBarHeight = healthBarWidth * 0.15f;
                healthBarOffsetX = -(float)(healthBarWidth / 2.0);
                healthBarOffsetY = (-shipIconWidth * 0.25f) + shipIconOffset;
            }
        }

        public void Draw(Graphics g, Rectangle rect, MetaData meta, Ship[] ships, ShipIcons shipIcons, double t)
        {
            //Do per-frame graphical setup
            g.SmoothingMode = SmoothingMode.HighQuality;
            g.DrawImage(background, rect);
            g.SetClip(rect);

            //Do actual drawing to the frame here
            foreach (Ship s in ships)
            {
                //Don't draw anything about the ship if it hasn't been detected yet
                if (s.firstSpotted > t)
                    continue;

                //Get position and convert to screen position
                Point2 pos = s.GetPosition(t);
                pos *= rect.Width;
                pos.x += rect.X;
                //pos.y = width - pos.y;
                pos.y = rect.Height - pos.y;
                pos.y += rect.Y;

                float heading = s.GetHeading(t);
                int health = s.GetHealth(t);
                bool visible = s.GetVisible(t);
                //Ship Icon
                {
                    
                    //Now, draw ship icon
                    RectangleF r = new RectangleF((float)pos.x + shipIconOffset, (float)pos.y + shipIconOffset, shipIconWidth, shipIconWidth);
                    Image shipIcon = shipIcons.GetImageFromShip(s, health, visible);

                    if (shipIcon == null)
                        Console.WriteLine($"Ship icon null: {s}");

                    Render.RotateImage(g, shipIcon, r, heading);
                }
                //Health bar
                {
                    if (health > 0 && visible)
                    {
                        //Full is the healthbar background (which is red), and left is the green portion of the healthbar which goes down with losing health
                        RectangleF full = new RectangleF((float)pos.x + healthBarOffsetX, (float)pos.y + healthBarOffsetY, healthBarWidth, healthBarHeight);
                        RectangleF left = full;
                        left.Width = (health / (float)s.maxHealth * left.Width);

                        g.FillRectangle(Brushes.Red, full);
                        g.FillRectangle(Brushes.Green, left);
                    }
                }
            }

            goto END;

            //Render shots here
            while (Program.shotList.Count > nextShotIndex && Program.shotList[nextShotIndex].fireTime <= t)
            {
                activeShots.Add(Program.shotList[nextShotIndex]);
                nextShotIndex++;
            }

            for (int i = activeShots.Count - 1; i >= 0; i--)
            {
                if (activeShots[i].endTime > t)
                    activeShots.RemoveAt(i);
            }
            
            {
                Pen p = new Pen(new SolidBrush(Color.Red), 2f);
                foreach (Shot s in activeShots)
                {
                    g.DrawLine(p, s.startPos.ToPointF(), s.endPos.ToPointF());
                }
            }

            END:
            //Add game timer
            g.DrawString(Utils.SecondsToGameTime(t), new Font(SystemFonts.DefaultFont.FontFamily, (float)rect.Width / 25f, FontStyle.Regular), Brushes.White, rect.X, rect.Y);
        }

        public IRenderPortion Copy() => new MinimapRenderPortion(this);
    }

    class ShipIcons
    {
        readonly Color mainPlayerIconColor = Color.White;
        readonly Color friendlyIconColor = Color.Aqua;
        readonly Color enemyIconColor = Color.Red;
        readonly Color disappearedIconColor = Color.Pink;
        readonly Color destroyedIconColor = Color.Black;

        Image carrierIconMainPlayer;
        Image battleshipIconMainPlayer;
        Image cruiserIconMainPlayer;
        Image destroyerIconMainPlayer;
        Image submarineIconMainPlayer;
        
        Image carrierIconFriend;
        Image battleshipIconFriend;
        Image cruiserIconFriend;
        Image destroyerIconFriend;
        Image submarineIconFriend;

        Image carrierIconEnemy;
        Image battleshipIconEnemy;
        Image cruiserIconEnemy;
        Image destroyerIconEnemy;
        Image submarineIconEnemy;

        Image carrierIconDisappeared;
        Image battleshipIconDisappeared;
        Image cruiserIconDisappeared;
        Image destroyerIconDisappeared;
        Image submarineIconDisappeared;

        Image carrierIconDestroyed;
        Image battleshipIconDestroyed;
        Image cruiserIconDestroyed;
        Image destroyerIconDestroyed;
        Image submarineIconDestroyed;


        public void Init()
        {
            const string iconImagePath = "assets/class_icons/";

            //Ship icons
            Image carrierIcon = Image.FromFile(iconImagePath + "AirCarrier.png");
            Image battleshipIcon = Image.FromFile(iconImagePath + "Battleship.png");
            Image cruiserIcon = Image.FromFile(iconImagePath + "Cruiser.png");
            Image destroyerIcon = Image.FromFile(iconImagePath + "Destroyer.png");
            Image submarineIcon = Image.FromFile(iconImagePath + "Destroyer.png");
            //Temporary submarine icon is just a destroyer icon with 'SS' in the middle
            Graphics.FromImage(submarineIcon).DrawString("SS", new Font(SystemFonts.DefaultFont.FontFamily, 7f), Brushes.Black, 32f - 8f, 32f - 2f);

            ColorFiltering shipIconFilter = new ColorFiltering();
            shipIconFilter.Red = new AForge.IntRange(-100, 100);
            shipIconFilter.Green = new AForge.IntRange(-100, 100);
            shipIconFilter.Blue = new AForge.IntRange(-100, 100);

            //Filter for main player
            shipIconFilter.FillColor = new AForge.Imaging.RGB(mainPlayerIconColor);

            submarineIconMainPlayer = shipIconFilter.Apply(new Bitmap(submarineIcon));
            destroyerIconMainPlayer = shipIconFilter.Apply(new Bitmap(destroyerIcon));
            cruiserIconMainPlayer = shipIconFilter.Apply(new Bitmap(cruiserIcon));
            battleshipIconMainPlayer = shipIconFilter.Apply(new Bitmap(battleshipIcon));
            carrierIconMainPlayer = shipIconFilter.Apply(new Bitmap(carrierIcon));

            //Filter for friendlies
            shipIconFilter.FillColor = new AForge.Imaging.RGB(friendlyIconColor);

            submarineIconFriend = shipIconFilter.Apply(new Bitmap(submarineIcon));
            destroyerIconFriend = shipIconFilter.Apply(new Bitmap(destroyerIcon));
            cruiserIconFriend = shipIconFilter.Apply(new Bitmap(cruiserIcon));
            battleshipIconFriend = shipIconFilter.Apply(new Bitmap(battleshipIcon));
            carrierIconFriend = shipIconFilter.Apply(new Bitmap(carrierIcon));

            //Filter for hostiles
            shipIconFilter.FillColor = new AForge.Imaging.RGB(enemyIconColor);

            submarineIconEnemy = shipIconFilter.Apply(new Bitmap(submarineIcon));
            destroyerIconEnemy = shipIconFilter.Apply(new Bitmap(destroyerIcon));
            cruiserIconEnemy = shipIconFilter.Apply(new Bitmap(cruiserIcon));
            battleshipIconEnemy = shipIconFilter.Apply(new Bitmap(battleshipIcon));
            carrierIconEnemy = shipIconFilter.Apply(new Bitmap(carrierIcon));

            //Filter for invisible
            shipIconFilter.FillColor = new AForge.Imaging.RGB(disappearedIconColor);

            submarineIconDisappeared = shipIconFilter.Apply(new Bitmap(submarineIcon));
            destroyerIconDisappeared = shipIconFilter.Apply(new Bitmap(destroyerIcon));
            cruiserIconDisappeared = shipIconFilter.Apply(new Bitmap(cruiserIcon));
            battleshipIconDisappeared = shipIconFilter.Apply(new Bitmap(battleshipIcon));
            carrierIconDisappeared = shipIconFilter.Apply(new Bitmap(carrierIcon));

            //Filter for destroyed
            shipIconFilter.FillColor = new AForge.Imaging.RGB(destroyedIconColor);

            submarineIconDestroyed = shipIconFilter.Apply(new Bitmap(submarineIcon));
            destroyerIconDestroyed = shipIconFilter.Apply(new Bitmap(destroyerIcon));
            cruiserIconDestroyed = shipIconFilter.Apply(new Bitmap(cruiserIcon));
            battleshipIconDestroyed = shipIconFilter.Apply(new Bitmap(battleshipIcon));
            carrierIconDestroyed = shipIconFilter.Apply(new Bitmap(carrierIcon));
        }
    
        public Image GetImageFromShip(Ship s, int health, bool visible)
        {
            if (health <= 0)
                return GetDestroyedIcon(s.shipType);

            if (visible)
            {
                if (s.team == Program.meta.mainPlayer.team)
                {
                    if (Program.meta.mainPlayerID == s.ID)
                        return GetMainPlayerIcon(s.shipType);
                    else
                        return GetFriendIcon(s.shipType);
                }
                else
                    return GetEnemyIcon(s.shipType);
            }
            else
                return GetDisappearedIcon(s.shipType);
        }

        private Image GetMainPlayerIcon(Ship.ShipType type)
        {
            switch (type)
            {
                case Ship.ShipType.submarine:
                    return submarineIconMainPlayer;
                case Ship.ShipType.destroyer:
                    return destroyerIconMainPlayer;
                case Ship.ShipType.cruiser:
                    return cruiserIconMainPlayer;
                case Ship.ShipType.battleship:
                    return battleshipIconMainPlayer;
                case Ship.ShipType.carrier:
                    return carrierIconMainPlayer;
                default:
                    return null;
            }
        }

        private Image GetFriendIcon(Ship.ShipType type)
        {
            switch (type)
            {
                case Ship.ShipType.submarine:
                    return submarineIconFriend;
                case Ship.ShipType.destroyer:
                    return destroyerIconFriend;
                case Ship.ShipType.cruiser:
                    return cruiserIconFriend;
                case Ship.ShipType.battleship:
                    return battleshipIconFriend;
                case Ship.ShipType.carrier:
                    return carrierIconFriend;
                default:
                    return null;
            }
        }

        private Image GetEnemyIcon(Ship.ShipType type)
        {
            switch (type)
            {
                case Ship.ShipType.submarine:
                    return submarineIconEnemy;
                case Ship.ShipType.destroyer:
                    return destroyerIconEnemy;
                case Ship.ShipType.cruiser:
                    return cruiserIconEnemy;
                case Ship.ShipType.battleship:
                    return battleshipIconEnemy;
                case Ship.ShipType.carrier:
                    return carrierIconEnemy;
                default:
                    return null;
            }
        }

        private Image GetDisappearedIcon(Ship.ShipType type)
        {
            switch (type)
            {
                case Ship.ShipType.submarine:
                    return submarineIconDisappeared;
                case Ship.ShipType.destroyer:
                    return destroyerIconDisappeared;
                case Ship.ShipType.cruiser:
                    return cruiserIconDisappeared;
                case Ship.ShipType.battleship:
                    return battleshipIconDisappeared;
                case Ship.ShipType.carrier:
                    return carrierIconDisappeared;
                default:
                    return null;
            }
        }

        private Image GetDestroyedIcon(Ship.ShipType type)
        {
            switch (type)
            {
                case Ship.ShipType.submarine:
                    return submarineIconDestroyed;
                case Ship.ShipType.destroyer:
                    return destroyerIconDestroyed;
                case Ship.ShipType.cruiser:
                    return cruiserIconDestroyed;
                case Ship.ShipType.battleship:
                    return battleshipIconDestroyed;
                case Ship.ShipType.carrier:
                    return carrierIconDestroyed;
                default:
                    return null;
            }
        }
    }
}
