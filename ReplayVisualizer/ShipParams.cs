using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace ReplayVisualizer
{
    class ShipParam
    {
        public long ID;
        public string name;

        public Ship.ShipType shipType;

        public ShipParam(string fileDir)
        {
            Parse(File.ReadAllText(fileDir));
        }

        public void Parse(string fileStr)
        {
            JObject jo = JsonConvert.DeserializeObject<JObject>(fileStr);
            ID = jo.Value<long>("id");
            name = jo.Value<string>("name");

            string prefix = jo.Value<string>("index") + "_";
            name = name.Substring(prefix.Length);

            JObject typeInfo = jo.Value<JObject>("typeinfo");
            {
                string shipTypeStr = typeInfo.Value<string>("species");
                switch (shipTypeStr)
                {
                    case "Submarine":
                        shipType = Ship.ShipType.submarine;
                        break;
                    case "Destroyer":
                        shipType = Ship.ShipType.destroyer;
                        break;
                    case "Cruiser":
                        shipType = Ship.ShipType.cruiser;
                        break;
                    case "Battleship":
                        shipType = Ship.ShipType.battleship;
                        break;
                    case "AirCarrier":
                        shipType = Ship.ShipType.carrier;
                        break;
                    default:
                        break;
                }
            }
        }

        public override string ToString()
        {
            return $"Name: {name} Type: {shipType} ID: {ID}";
        }
    }
    static class ShipParams
    {
        const string folderPath = "ShipParams";
        public static SortedList<long, ShipParam> shipParams;
        public static void Init()
        {
            shipParams = new SortedList<long, ShipParam>();

            string root = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, folderPath);
            string[] fileDirectories = Directory.GetFiles(root);

            foreach (string dir in fileDirectories)
            {
                ShipParam sp = new ShipParam(dir);
                shipParams.Add(sp.ID, sp);
            }
        }
    }
}
