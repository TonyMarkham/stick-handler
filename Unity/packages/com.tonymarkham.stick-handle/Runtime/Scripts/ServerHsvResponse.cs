using System;
using Newtonsoft.Json;

namespace StickHandle.Scripts
{
    [Serializable]
    internal class ServerHsvResponse
    {
        [JsonProperty("orange")] public ServerHsvRange orange;
        [JsonProperty("green")]  public ServerHsvRange green;
    }
}
