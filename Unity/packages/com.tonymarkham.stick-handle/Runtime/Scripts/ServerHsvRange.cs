using System;
using Newtonsoft.Json;

namespace StickHandle.Scripts
{
    [Serializable]
    internal class ServerHsvRange
    {
        [JsonProperty("h_min")] public int hMin;
        [JsonProperty("h_max")] public int hMax;
        [JsonProperty("s_min")] public int sMin;
        [JsonProperty("s_max")] public int sMax;
        [JsonProperty("v_min")] public int vMin;
        [JsonProperty("v_max")] public int vMax;
    }
}
