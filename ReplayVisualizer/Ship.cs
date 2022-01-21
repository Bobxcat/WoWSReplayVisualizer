using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ReplayVisualizer
{
    struct Point2
    {
        public double x;
        public double y;
        public Point2(double x, double y)
        {
            this.x = x;
            this.y = y;
        }

        //Maybe non-static would be good too?
        public static Point2 Lerp(Point2 start, Point2 end, double l) => (1.0 - l) * start + l * end;

        public static Point2 operator +(Point2 a, Point2 b) => new Point2(a.x + b.x, a.y + b.y);
        public static Point2 operator -(Point2 a, Point2 b) => new Point2(a.x - b.x, a.y - b.y);
        //Here, I just reorder the operator parameters so multiplication can be done both ways
        public static Point2 operator *(Point2 a, double b) => new Point2(a.x * b, a.y * b);
        public static Point2 operator *(double b, Point2 a) => a * b;
        public static Point2 operator /(Point2 a, double b) => new Point2(a.x / b, a.y / b);
        public override string ToString() => $"({x}, {y})";
    }

    class Ship
    {
        //team 0 is friendly, team 1 is enemy
        public int team;
        /// <summary>
        /// 
        /// </summary>
        public Relation relation;
        public enum Relation
        {
            self,
            friend,
            enemy,
            division
        }
        /// <summary>
        /// ID of a player as defined by the "shipid" field in "OnArenaStateReceived" 
        /// </summary>
        public int ID;
        /// <summary>
        /// ID of in-game ship, for example there's a vehicleID for konigsberg shared by all players using that boat
        /// </summary>
        public int vehicleID;

        public int maxHealth;
        public string username;
        public string clan;

        public List<TimeMarker<Point2>> positions;
        public List<TimeMarker<float>> headings;
        public List<TimeMarker<int>> healths;
        public List<TimeMarker<int>> damages;
        public List<TimeMarker<int>> kills;
        public List<TimeMarker<bool>> visibilities;
        public double firstSpotted; //Only applies to enemy ships, it's the first time value when an enemy has visiblity == true

        public Ship()
        {
            positions = new List<TimeMarker<Point2>>();
            headings = new List<TimeMarker<float>>();

            healths = new List<TimeMarker<int>>();
            damages = new List<TimeMarker<int>>();
            kills = new List<TimeMarker<int>>();

            visibilities = new List<TimeMarker<bool>>();

            //Set default values
            headings.Add(new TimeMarker<float>(0f, -1.0));

            damages.Add(new TimeMarker<int>(0, -1.0));
            kills.Add(new TimeMarker<int>(0, -1.0));
        }

        /// <summary>
        /// Sorts all TimeMarker lists by time, using the TimeMarkerComparer class
        /// </summary>
        private void Sort()
        {
            TimeMarkerComparer comparer = new TimeMarkerComparer();
            positions.Sort(comparer);
            headings.Sort(comparer);
            healths.Sort(comparer);
            damages.Sort(comparer);
            kills.Sort(comparer);
            visibilities.Sort(comparer);
        }
        /// <summary>
        /// Call between processing all packets and rendering the video
        /// </summary>
        public void PreRenderSetup()
        {
            Sort();
            //Set firstSpotted value
            foreach (TimeMarker<bool> b in visibilities)
            {
                if (b.value)
                {
                    firstSpotted = b.time;
                    Console.WriteLine(b.time);
                    break;
                }
            }
        }

        //The difference between getting position and health is that the position of a ship is the weighted average between two recorded positions, and the health of a ship is whatever its last recorded health is
        /// <summary>
        /// Returns the position of this ship at a given time value. This does interpolate
        /// </summary>
        public Point2 GetPosition(double time)
        {
            if (positions.Count == 0)
                return new Point2();
            int index = positions.BinarySearch(new TimeMarker<Point2>() { time = time }, new TimeMarkerComparer());

            //If it's a perfect match, return the value. Else, get the index of the TimeMarker with the next highest time
            if (index >= 0)
            {
                return positions[index].value;
            }
            //indexAbove is the index of the TimeMarker with the next highest time than the given time value, indexBelow is the same (but below)
            int indexAbove = ~index;
            int indexBelow = indexAbove - 1;

            //If the given time is greater than all other times in the list, return the highest element and vice-versa
            if (indexAbove >= positions.Count)
            {
                return positions[positions.Count - 1].value;
            }
            //The index here is checked against 0 since only elements which are below zero will have the zero index as the nearest greater time value
            //In other words -- Since indexAbove is of the index of the next highest time value, if indexAbove is zero then the time value is below all elements in the list
            else if (indexAbove <= 0)
            {
                return positions[0].value;
            }

            //Now that we know the index is neither above or below the list (and is not any of the edge values either)...
            //We need to find the linear interpolation between the two!
            //Find the weight using the difference between the time values and use that for the lerp


            //this means calling GetVisible() twice for every ship in a rendered frame------------------
            if (!GetVisible(time))
                return positions[indexBelow].value;

            double deltaTime = positions[indexAbove].time - positions[indexBelow].time;
            double lerpAmount = (time - positions[indexBelow].time) / deltaTime;

            Point2 finalPos = Point2.Lerp(positions[indexBelow].value, positions[indexAbove].value, lerpAmount);

            return finalPos;
        }
        /// <summary>
        /// Returns the heading of this ship at a given time value. This does interpolate
        /// </summary>
        public float GetHeading(double time)
        {
            int index = headings.BinarySearch(new TimeMarker<float>() { time = time }, new TimeMarkerComparer());

            //If it's a perfect match, return the value. Else, get the index of the TimeMarker with the next highest time
            if (index >= 0)
            {
                return headings[index].value;
            }
            //indexAbove is the index of the TimeMarker with the next highest time than the given time value, indexBelow is the same (but below)
            int indexAbove = ~index;
            int indexBelow = indexAbove - 1;

            //If the given time is greater than all other times in the list, return the highest element and vice-versa
            if (indexAbove >= headings.Count)
            {
                return headings[headings.Count - 1].value;
            }
            //The index here is checked against 0 since only elements which are below zero will have the zero index as the nearest greater time value
            //In other words -- Since indexAbove is of the index of the next highest time value, if indexAbove is zero then the time value is below all elements in the list
            else if (indexAbove <= 0)
            {
                return headings[0].value;
            }

            //Now that we know the index is neither above or below the list (and is not any of the edge values either)...
            //We need to find the linear interpolation between the two!
            //Find the weight using the difference between the time values and use that for the lerp

            //testing
            return headings[indexBelow].value;
            //...

            double deltaTime = headings[indexAbove].time - headings[indexBelow].time;

            /*if (deltaTime > 20.0)
                return headings[indexBelow].value;

            double lerpAmount = (time - headings[indexBelow].time) / deltaTime;

            float finalHeading = Utils.Lerp();

            return finalPos;*/
        }
        /// <summary>
        /// Returns the health of this ship at a given time value. This does not interpolate
        /// </summary>
        public int GetHealth(double time)
        {
            if (healths.Count == 0)
                return -1;
            //This is the same as getting position except it does not do any interpolation
            int index = healths.BinarySearch(new TimeMarker<int>() { time = time }, new TimeMarkerComparer());

            if (index >= 0)
            {
                return healths[index].value;
            }
            //indexAbove is the index of the TimeMarker with the next highest time than the given time value, indexBelow is the same (but below)
            int indexAbove = ~index;
            int indexBelow = indexAbove - 1;

            //If the given time is greater than all other times in the list, return the highest element and vice-versa
            if (indexAbove >= healths.Count)
            {
                return healths[healths.Count - 1].value;
            }
            //The index here is checked against 0 since only elements which are below zero will have the zero index as the nearest greater time value
            //In other words -- Since the index is of the next highest time value, if the index is zero then the time value is below all elements in the list
            else if (indexAbove <= 0)
            {
                return healths[0].value;
            }

            //Since the time value is now known to be between two of the values in the list, just return the most recent health value
            return healths[indexBelow].value;
        }
        /// <summary>
        /// Returns the ship's damage dealt at a given time value. This does not interpolate
        /// </summary>
        public int GetDamage(double time)
        {
            if (damages.Count == 0)
                return -1;
            //This is the same as getting health except it does everything from the damages list
            int index = damages.BinarySearch(new TimeMarker<int>() { time = time }, new TimeMarkerComparer());

            if (index >= 0)
            {
                return damages[index].value;
            }
            //indexAbove is the index of the TimeMarker with the next highest time than the given time value, indexBelow is the same (but below)
            int indexAbove = ~index;
            int indexBelow = indexAbove - 1;

            //If the given time is greater than all other times in the list, return the highest element and vice-versa
            if (indexAbove >= damages.Count)
            {
                return damages[damages.Count - 1].value;
            }
            //The index here is checked against 0 since only elements which are below zero will have the zero index as the nearest greater time value
            //In other words -- Since the index is of the next highest time value, if the index is zero then the time value is below all elements in the list
            else if (indexAbove <= 0)
            {
                return damages[0].value;
            }

            //Since the time value is now known to be between two of the values in the list, just return the most recent health value
            return damages[indexBelow].value;
        }
        /// <summary>
        /// Returns the ship's visibility level at a given time value. This does not interpolate
        /// </summary>
        public bool GetVisible(double time)
        {
            //If it's friendly, it's always visible
            if (team == 0)
                return true;
            if (visibilities.Count == 0)
                return false;
            //This is the same as getting health except it does everything from the damages list
            int index = visibilities.BinarySearch(new TimeMarker<bool>() { time = time }, new TimeMarkerComparer());

            if (index >= 0)
            {
                return visibilities[index].value;
            }
            //indexAbove is the index of the TimeMarker with the next highest time than the given time value, indexBelow is the same (but below)
            int indexAbove = ~index;
            int indexBelow = indexAbove - 1;

            //If the given time is greater than all other times in the list, return the highest element and vice-versa
            if (indexAbove >= visibilities.Count)
            {
                return visibilities[visibilities.Count - 1].value;
            }
            //The index here is checked against 0 since only elements which are below zero will have the zero index as the nearest greater time value
            //In other words -- Since the index is of the next highest time value, if the index is zero then the time value is below all elements in the list
            else if (indexAbove <= 0)
            {
                return visibilities[0].value;
            }

            //Since the time value is now known to be between two of the values in the list, just return the most recent health value
            return visibilities[indexBelow].value;
        }
        /// <summary>
        /// Returns the ship's number of kills at a given time value. This does not interpolate
        /// </summary>
        public int GetKills(double time)
        {
            if (kills.Count == 0)
                return -1;
            //This is the same as getting health except it does everything from the damages list
            int index = kills.BinarySearch(new TimeMarker<int>() { time = time }, new TimeMarkerComparer());

            if (index >= 0)
            {
                return kills[index].value;
            }
            //indexAbove is the index of the TimeMarker with the next highest time than the given time value, indexBelow is the same (but below)
            int indexAbove = ~index;
            int indexBelow = indexAbove - 1;

            //If the given time is greater than all other times in the list, return the highest element and vice-versa
            if (indexAbove >= kills.Count)
            {
                return kills[kills.Count - 1].value;
            }
            //The index here is checked against 0 since only elements which are below zero will have the zero index as the nearest greater time value
            //In other words -- Since the index is of the next highest time value, if the index is zero then the time value is below all elements in the list
            else if (indexAbove <= 0)
            {
                return kills[0].value;
            }

            //Since the time value is now known to be between two of the values in the list, just return the most recent health value
            return kills[indexBelow].value;
        }
    
        public override string ToString() => $"ID: {ID}\nTeam: {team}\nVehicle ID: {vehicleID}\nDamages count: {damages.Count}\nPositions count: {positions.Count}\nVisibilites count: {visibilities.Count}";
    }
}
