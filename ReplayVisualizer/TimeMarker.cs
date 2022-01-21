using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ReplayVisualizer
{
    interface ITimeMarker
    {
        double time { get; set; }
    }
    class TimeMarker<T> : ITimeMarker
    {
        public T value { get; set; }
        public double time { get; set; }
        public TimeMarker()
        {

        }
        public TimeMarker(T value, double time)
        {
            this.value = value;
            this.time = time;
        }
    }
    class TimeMarkerComparer : IComparer<ITimeMarker>
    {
        public int Compare(ITimeMarker x, ITimeMarker y)
        {
            if (y == null) return 1;
            if (x == null) return 1;

            return x.time.CompareTo(y.time);
        }
    }
}
