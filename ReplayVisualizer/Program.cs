using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using ReplayVisualizer.Video;

namespace ReplayVisualizer
{

    /// <summary>
    /// A class representing a single line from the parsed replay file
    /// </summary>
    class ReplayLine
    {
        /// <summary>
        /// The time at which the payload was recorded, in seconds
        /// </summary>
        public double clock;
        /// <summary>
        /// The first item of this tuple is the type of payload. The second item is the value of the payload, as a JSON object
        /// </summary>
        //public (string, Newtonsoft.Json.Linq.JObject) payload;
        public (string, dynamic) payload;//Item2 (the actual data) will normally be of the type Newtonsoft.Json.Linq.JObject, but may sometimes be a string or other
        /// <summary>
        /// A shortcut for payload.Item2, because I guess I'm just that lazy
        /// </summary>
        //private Newtonsoft.Json.Linq.JObject Pld { get => payload.Item2; }
        public bool IsNull { get => payload.Item1 == null; }
        /// <summary>
        /// This class exists to be able to automatically write the payload from json deserialisation and then have it be turned into a more useful tuple in the ReplayLine class. This approach is not great and should be improved (perhaps through a constructor of payload in the main class -- make it writable as a Dictionary)
        /// </summary>
        private class ReplayLineFromJSON
        {
            public double clock;
            public Dictionary<string, dynamic> payload;
        }
        private ReplayLine(ReplayLineFromJSON rl)
        {
            if (rl != null && rl.payload != null && rl.payload.Keys.Count > 0)
            {
                clock = rl.clock;
                payload.Item1 = rl.payload.Keys.ElementAt(0);

                payload.Item2 = rl.payload.Values.ElementAt(0);

            }
            else
            {
                payload = (null, null);
            }
        }
        public ReplayLine(string lineStr)
        {
            ReplayLine rl = Parse(lineStr);

            if (rl != null)
            {
                clock = rl.clock;
                payload = rl.payload;
            }
            else
            {
                payload = (null, null);
            }
        }
        private static ReplayLine Parse(string line)
        {
            ReplayLineFromJSON deserialized;

            try
            {
                deserialized = JsonConvert.DeserializeObject<ReplayLineFromJSON>(line);
            }
            catch (Exception)
            {
                return null;
            }

            return new ReplayLine(deserialized);
        }
        /// <summary>
        /// Processes the values of the pre-created ReplayLine and turns any needed data into a form used by this program, otherwise ignoring it
        /// </summary>
        public void Process()
        {
            //Some notes about the JSON structure:

            //-shipid in OnArenaStateReceived is always avatarid + 1, and avatarid is always even
            //--calls to MinimapUpdate always use the shipid

            //-PlayerOrientation calls are strange
            //--calls come in pairs, with the main player (that being the player who recorded the replay) always being the target ID
            //--the first of the pair uses the player's avatarid, with the second using their shipid
            //---However, the first call (which uses the avatarid) has position and rotation values of 0 and a parentid equal to shipid
            //---the second call has parentid = 0 with 3d position and rotation values. The x and z components of position are on the horizontal plane and are likely in the range 0.0 to 500.0

            //-Position calls are similar to PlayerOrientation calls, except that they handle everyone except the main player
            //--the pid of a position call is their shipid


            //Chain of if-statements because I could spend wayyyy too much time on premature optimization (just to avoid the problem of scope in switch statements with the variable pld)
            if ("MinimapUpdate" == payload.Item1)
            {
                //The clock to game time offset is always set to the time of the first instance of MinimapUpdate
                if (Program.meta.clockTimeToGameTimeOffset > 0.0 && clock > 0.0)
                    Program.meta.clockTimeToGameTimeOffset = -clock;

                JObject pld = (JObject)payload.Item2;
                JToken[] updates = pld.Value<JArray>("updates").ToArray();

                for (int i = 0; i < updates.Length; i++)
                {
                    JToken p = updates[i];

                    int id = p.Value<int>("entity_id");
                    bool disappearing = p.Value<bool>("disappearing");
                    float heading = p.Value<float>("heading");
                    Point2 position = new Point2(p.Value<double>("x"), p.Value<double>("y"));
                    bool omitPos = disappearing;

                    if (Program.shipList.TryGetValue(id, out Ship s))
                    {
                        //Scale position from [-.1, 1.1] to [0, 1]
                        //[-.1, 1.1] -> [-.6, .6] -> [-.5, .5] -> [0, 1]
                        position.x = (position.x - 0.5) * (1.0 / 1.1) + 0.5;
                        position.y = (position.y - 0.5) * (1.0 / 1.1) + 0.5;
                        //...

                        s.visibilities.Add(new TimeMarker<bool>(!disappearing, clock));
                        if (!omitPos)
                        {
                            s.positions.Add(new TimeMarker<Point2>(position, clock));
                            s.headings.Add(new TimeMarker<float>(heading, clock));
                        }
                    }
                }
            }
            else if ("EntityMethod" == payload.Item1)
            {
                JObject pld = (JObject)payload.Item2;
                string method = pld.Value<string>("method");
                if ("receiveArtilleryShots" == method)
                {
                    //Data structure: "args":[[{"shots":[{...}, {...}, {...}...]}]]
                    JToken[] args = pld.Value<JArray>("args").ToArray()[0].Value<JArray>().ToArray();
                    JToken[] shots = args[0].Value<JArray>("shots").ToArray();
                    //JToken[] shots = pld.Value<JArray>("shots").ToArray();
                    for (int i = 0; i < shots.Length; i++)
                    {
                        Point2 startPos;
                        Point2 endPos;
                        {
                            //double[] startPosRaw = shots[i].Value<double[]>("pos");
                            //double[] endPosRaw = shots[i].Value<double[]>("tarPos");
                            double[] startPosRaw = shots[i].Value<JArray>("pos").ToArray().Values<double>().ToArray();
                            double[] endPosRaw = shots[i].Value<JArray>("tarPos").ToArray().Values<double>().ToArray();

                            startPos = new Point2(startPosRaw[0], startPosRaw[2]);
                            endPos = new Point2(endPosRaw[0], endPosRaw[2]);
                            //JToken[] startPosRaw = shots[i].Value<JArray>("pos").ToArray();
                            //JToken[] endPosRaw = shots[i].Value<JArray>("tarPos").ToArray();
                        }
                        Shot s = new Shot(startPos, endPos, shots[i].Value<double>("speed"), shots[i].Value<double>("hitDistance"), clock);
                        //Console.WriteLine(s);
                        Program.shotList.Add(s);
                    }
                }
            }
            else if ("DamageReceived" == payload.Item1)
            {
                JObject pld = (JObject)payload.Item2;
                int victimID = pld.Value<int>("victim");
                if (Program.shipList.TryGetValue(victimID, out Ship victim))
                {
                    JToken[] aggressors = pld.Value<JArray>("aggressors").ToArray();
                    foreach (JToken a in aggressors)
                    {
                        int aggressorID = a.Value<int>("aggressor");
                        int damage = a.Value<int>("damage");

                        int oldHealth = victim.GetHealth(clock);
                        //A ship can only be counted dead because of a call to either ShipDestroyed or EntityProperty:Health
                        //victim.healths.Add(new TimeMarker<int>(Math.Max(oldHealth - damage, 1), clock));

                        if (Program.shipList.TryGetValue(aggressorID, out Ship aggressor))
                        {
                            int oldDamage = aggressor.GetDamage(clock);

                            aggressor.damages.Add(new TimeMarker<int>(oldDamage + damage, clock));
                        }
                    }
                }
            }
            else if ("EntityProperty" == payload.Item1)
            {
                JObject pld = (JObject)payload.Item2;
                string property = pld.Value<string>("property");
                int id = pld.Value<int>("entity_id");
                dynamic value = pld.Value<dynamic>("value");

                if (Program.shipList.TryGetValue(id, out Ship s))
                {
                    switch (property)
                    {
                        case "health":
                            s.healths.Add(new TimeMarker<int>((int)value, clock));
                            break;
                    }
                }
            }
            else if ("ShipDestroyed" == payload.Item1)
            {
                JObject pld = (JObject)payload.Item2;
                int killer = pld.Value<int>("killer");
                int victim = pld.Value<int>("victim");
                string cause = pld.Value<string>("cause");

                if (Program.shipList.TryGetValue(killer, out Ship k))
                {
                    k.kills.Add(new TimeMarker<int>(k.GetKills(clock), clock));
                }
                if (Program.shipList.TryGetValue(victim, out Ship v))
                {
                    v.healths.Add(new TimeMarker<int>(0, clock));
                }
            }
            else if ("OnArenaStateReceived" == payload.Item1)
            {
                JObject pld = (JObject)payload.Item2;
                JToken[] players = pld.Value<JArray>("players").ToArray();
                for (int i = 0; i < players.Length; i++)
                {
                    Ship s = new Ship()
                    {
                        username = players[i].Value<string>("username"),
                        clan = players[i].Value<string>("clan"),
                        ID = players[i].Value<int>("shipid"),
                        //vehicleID = players[i].Value<int>("shipid"),
                        team = players[i].Value<int>("teamid"),
                        maxHealth = players[i].Value<int>("health")
                    };
                    s.healths.Add(new TimeMarker<int>(s.maxHealth, -1.0));

                    Program.shipList.Add(s.ID, s);
                }
            }
            else if ("Version" == payload.Item1)
            {
                string pld = (string)payload.Item2;//This pld is just the version string
            }
        }
    }
    class MetaData
    {
        public double replayLength;
        public double clockTimeToGameTimeOffset;
        public int mainPlayerID;
        public Ship mainPlayer;
        public string mapName;

        public MetaData()
        {
            replayLength = -1.0;
            clockTimeToGameTimeOffset = 1.0; //Should be negative, so default to positive
        }
        /// <summary>
        /// Call after processing every other line in the replay
        /// </summary>
        /// <param name="line"></param>
        public void SetFromFirstLine(string line)
        {
            JObject data = JsonConvert.DeserializeObject<JObject>(line);
            mapName = data.Value<string>("mapDisplayName");

            JToken[] ships = data.Value<JArray>("vehicles").ToArray();

            for (int i = 0; i < ships.Length; i++)
            {
                //Get the ship with relation=0, which will be the main player
                string name = ships[i].Value<string>("name");
                Ship.Relation relation = (Ship.Relation)ships[i].Value<int>("relation");

                Ship s = null;
                //Find the ID of the ship from its username
                foreach (Ship ship in Program.shipList.Values)
                {
                    if (ship.username == name)
                    {
                        s = ship;
                        break;
                    }
                }
                if (s == null)
                {
                    Console.WriteLine($"{name} not found in metadata");
                    continue;
                }

                s.relation = relation;
                s.vehicleID = ships[i].Value<long>("shipId");

                //Populate Ship from matching ShipParam
                if (ShipParams.shipParams.TryGetValue(s.vehicleID, out ShipParam sp))
                {
                    s.shipType = sp.shipType;
                }

                //Find the main player and set mainPlayerID
                if (relation == Ship.Relation.self)
                {
                    mainPlayerID = s.ID;
                    mainPlayer = s;
                    Console.WriteLine($"Main player username: {s.username}");
                }
            }
        }
    }
    class Program
    {
        /// <summary>
        /// A list of all ships. Key value is the ship's ID
        /// </summary>
        public static SortedList<int, Ship> shipList;
        /// <summary>
        /// A list of all shots made in the game
        /// </summary>
        public static List<Shot> shotList;
        public static MetaData meta;

        /// <summary>
        /// Takes a file path for a parsed .wowsreplay file and creates a video from it (in theory)
        /// </summary>
        /// <param name="filePath">The file path of the parsed file, relative to the program executable</param>
        static void ProcessFile(string filePath)
        {
            //First, initialize the shipParams
            ShipParams.Init();

            //Read file text
            string fileStr = System.IO.File.ReadAllText(System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, filePath));

            //And get all the lines
            string[] fileLines = fileStr.Split(Environment.NewLine.ToCharArray());

            //Now, turn all the fileLine strings into ReplayLine objects and populate the replayLines array
            //The first line is for metadata and the last line is empty, so reduce count by 2
            ReplayLine[] replayLines = new ReplayLine[fileLines.Length - 2];

            meta = new MetaData();


            //Start at index 1 because first line is just metadata
            for (int i = 1; i < replayLines.Length; i++)
            {
                //Console.WriteLine("\nLine " + (i + 1));
                ReplayLine rl = new ReplayLine(fileLines[i]);
                if (rl.IsNull)
                {
                    Console.WriteLine("Failure: " + fileLines[i] + $" ({i})");
                }
                else
                {
                    replayLines[i] = rl;
                }
                rl.Process();
            }

            shotList.Sort(new ShotComparer());

            //Second to last line, the final line is strange and has clock=0.0
            meta.replayLength = replayLines[replayLines.Length - 2].clock;

            meta.SetFromFirstLine(fileLines[0]);
        }

        static void Main(string[] args)
        {
            shipList = new SortedList<int, Ship>();
            shotList = new List<Shot>();

            string filePath = args.Length > 0 ? args[0]: "in.jl";
            ProcessFile(filePath);

            foreach (Ship s in shipList.Values)
            {
                s.PreRenderSetup();
            }

            //shipList.Values.Log();

            Render.Init();
            Render.RenderVideo("out.mkv", Accord.Video.FFMPEG.VideoCodec.FFV1, 900, 60.0, 10.0);
        }
    }

    static class Utils
    {
        /// <summary>
        /// Writes an array to the console, with a linebreak between each value
        /// </summary>
        public static void Log<T>(this IList<T> list)
        {
            for (int i = 0; i < list.Count; i++)
            {
                Console.WriteLine(list[i]);
            }
        }

        public static double Modulo(double a, double b) => a - b * Math.Floor(a / b);

        public static string SecondsToGameTime(double t)
        {
            //Subtract a certain amount from the input time because of starting area wait before game, replay starts at (very) roughly 20 seconds before the match
            t += Program.meta.clockTimeToGameTimeOffset;
            int minutes = (int)(20.0 - (t / 60.0));
            int seconds = (int)(60.0 - Modulo(t, 60.0));
            return $"{minutes}:{seconds:00}";
        }

        public static double Clamp(double n, double min, double max)
        {
            if (n > max)
                return max;
            if (n < min)
                return min;
            return n;
        }

        public static bool IsEven(uint n) => (n & 1u) == 0;

        public static long CurrentTimeMilliseconds() => DateTime.Now.Ticks / TimeSpan.TicksPerMillisecond;
    }
}