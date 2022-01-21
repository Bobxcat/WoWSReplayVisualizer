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

namespace ReplayVisualizer
{
    static class Render
    {
        const string minimapImagePath = "assets/minimaps/";

        static MetaData meta;
        static Ship[] ships;

        static Image background;

        static Image carrierIcon;
        static Image battleshipIcon;
        static Image cruiserIcon;
        static Image destroyerIcon;
        static Image submarineIcon;
        //static Form form; //Form is for live renderring, coming later
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

            {
                string minimapImagePathFull = minimapImagePath + Program.meta.mapName + "/";

                Image minimap = Image.FromFile(minimapImagePathFull + "minimap.png");
                Image minimapWater = Image.FromFile(minimapImagePathFull + "minimap_water.png");

                background = minimapWater;
                Graphics g = Graphics.FromImage(background);
                g.DrawImage(minimap, Point.Empty);
            }

            /*//Form Creation
            form = new Form
            {
                FormBorderStyle = FormBorderStyle.FixedSingle,
                BackColor = Color.FromArgb(0, 50, 150),
            };

            //Events
            
            //Show the form
            form.Show();*/
        }
        /// <summary>
        /// Draws a rectangle on the Graphics object with a rotation of angle around its center
        /// </summary>
        /// <param name="angle">The angle of rotation in degrees</param>
        private static void RotateRectangle(Graphics g, RectangleF r, float angle, Brush b)
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
        private static void RotateImage(Graphics g, Image i, float x, float y, float angle)
        {
            using (Matrix m = new Matrix())
            {
                m.RotateAt(angle, new PointF(x + (i.Width / 2),
                                          y + (i.Height / 2)));
                g.Transform = m;
                g.DrawImage(i, x, y);
                g.ResetTransform();
            }
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
            VideoFileWriter writer = new VideoFileWriter();
            writer.Open(fileName, width, width, new Accord.Math.Rational(framerate), codec);

            int totalFrames = (int)(meta.replayLength / timeScale * framerate);

            //Multiply to take a value from 0-100 and put it in the range 0-width
            double frameScale = width / 100.0;

            int shipIconWidth = (int)(3.5 * frameScale);
            float shipIconOffset = -(float)(shipIconWidth / 2.0);

            float healthBarWidth = shipIconWidth;
            float healthBarHeight = shipIconWidth * 0.15f;
            float healthBarOffsetX = -(float)(healthBarWidth / 2.0);
            float healthBarOffsetY = (-shipIconWidth * 0.25f) + shipIconOffset;

            for (int i = 0; i < totalFrames; i++)
            {
                double t = i / framerate * timeScale; 
                Bitmap frame = new Bitmap(width, width);
                Graphics g = Graphics.FromImage(frame);

                //Do per-frame graphical setup
                g.SmoothingMode = SmoothingMode.HighQuality;
                g.DrawImage(background, 0f, 0f, width, width);

                //Do actual drawing to the frame here
                foreach (Ship s in ships)
                {
                    //Don't draw anything about the ship if it hasn't been detected yet
                    if (s.firstSpotted > t)
                        continue;

                    //Get position and convert to screen position
                    Point2 pos = s.GetPosition(t);
                    pos *= width;
                    pos.y = width - pos.y;

                    float heading = s.GetHeading(t);
                    int health = s.GetHealth(t);
                    bool visibile = s.GetVisible(t);
                    //Ship Icon
                    {
                        Color brushColor;

                        if (health <= 0)
                            brushColor = Color.Black;
                        else if (visibile)
                        {
                            if (s.team == 0)
                            {
                                if (Program.meta.mainPlayerID == s.ID)
                                    brushColor = Color.White;
                                else
                                    brushColor = Color.Aqua;
                            }
                            else
                                brushColor = Color.Red;
                        }
                        else
                            brushColor = Color.LightPink;

                        RectangleF r = new RectangleF((float)pos.x + shipIconOffset, (float)pos.y + shipIconOffset, shipIconWidth, shipIconWidth);
                        //g.FillRectangle(new SolidBrush(brushColor), r);
                        RotateRectangle(g, r, heading, new SolidBrush(brushColor));
                    }
                    //Health bar
                    {
                        if (health > 0 && visibile)
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
                //g.FillRectangle(Brushes.Blue, Math.Min(i, 100), Math.Min(i, 200), width / 10f, width / 10f);
                //...
                //Add game time
                g.DrawString(Utils.SecondsToGameTime(t), new Font(SystemFonts.DefaultFont.FontFamily, width / 25f), Brushes.White, 0f, 0f);
                //...
                writer.WriteVideoFrame(frame);
            }

            writer.Close();
        }
    }
}
