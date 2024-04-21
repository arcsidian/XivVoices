using System.Collections.Generic;

namespace XivVoices.Engine
{
    public class DataMapper
    {
        private Dictionary<int, string> bodyMap = new Dictionary<int, string>()
        {
            {0, "Beastman"},
            {1, "Adult"},
            {3, "Elderly"},
            {4, "Child"},
        };

        private Dictionary<int, string> raceMap = new Dictionary<int, string>()
        {
            {1, "Hyur"},
            {2, "Elezen"},
            {3, "Lalafell"},
            {4, "Miqo'te"},
            {5, "Roegadyn"},
            {6, "Au Ra"},
            {7, "Hrothgar"},
            {8, "Viera"},
        };

        private Dictionary<int, string> tribeMap = new Dictionary<int, string>()
        {
            {1, "Midlander"},
            {2, "Highlander"},
            {3, "Wildwood"},
            {4, "Duskwight"},
            {5, "Plainsfolk"},
            {6, "Dunesfolk"},
            {7, "Seeker of the Sun"},
            {8, "Keeper of the Moon"},
            {9, "Sea Wolf"},
            {10, "Hellsguard"},
            {11, "Raen"},
            {12, "Xaela"},
            {13, "Helions"},
            {14, "The Lost"},
            {15, "Rava"},
            {16, "Veena"},
        };

        private Dictionary<int, string> eyesMap = new Dictionary<int, string>()
        {
            {0, "Option 1"},
            {1, "Option 2"},
            {2, "Option 3"},
            {3, "Option 4"},
            {4, "Option 5"},
            {5, "Option 6"},
            {128, "Option 1"},
            {129, "Option 2"},
            {130, "Option 3"},
            {131, "Option 4"},
            {132, "Option 5"},
            {133, "Option 6"},
        };

        private Dictionary<int, string> skeletonMap = new Dictionary<int, string>()
        {
            {11001, "Amalj'aa"},
            {11002, "Ixal"},
            {11003, "Kobold"},
            {11004, "Goblin"},
            {11005, "Sylph"},
            {11006, "Moogle"},
            {11007, "Sahagin"},
            {11028, "Kojin"}
        };

        public string GetBody(int id) => bodyMap.TryGetValue(id, out var name) ? name : "Adult";
        public string GetRace(int id) => raceMap.TryGetValue(id, out var name) ? name : "Unknown:" + id.ToString();
        public string GetTribe(int id) => tribeMap.TryGetValue(id, out var name) ? name : "Unknown:" + id.ToString();
        public string GetEyes(int id) => eyesMap.TryGetValue(id, out var name) ? name : "Unknown:" + id.ToString();
        public string GetSkeleton(int id) => skeletonMap.TryGetValue(id, out var name) ? name : "Unknown:" + id.ToString();
    }
}
