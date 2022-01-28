using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ReplayVisualizer
{
    class Shot
    {
        public Point2 startPos;
        public Point2 endPos;
        public double speed;
        public double distanceTravelled;
        public double fireTime;

        public double endTime;

        public Shot(Point2 startPos, Point2 endPos, double speed, double distanceTravelled, double fireTime)
        {
            this.startPos = startPos;
            this.endPos = endPos;
            this.speed = speed;
            this.distanceTravelled = distanceTravelled;
            this.fireTime = fireTime;

            endTime = distanceTravelled / speed + fireTime;
        }

        public override string ToString() => $"Shot Fired at: {Utils.SecondsToGameTime(fireTime)} Start position: {startPos} End position: {endPos} Speed: {speed}";
    }

    class ShotComparer : IComparer<Shot>
    {
        public int Compare(Shot x, Shot y)
        {
            if (y == null) return 1;
            if (x == null) return 1;

            return x.fireTime.CompareTo(y.fireTime);
        }
    }
}
