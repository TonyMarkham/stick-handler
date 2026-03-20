using System;
using Newtonsoft.Json;
using UnityEngine;

namespace StickHandle.Scripts
{
    [Serializable]
    [JsonObject(MemberSerialization.Fields)]
    public class HsvPreset
    {
        public const int HUE_MAX              = 179;
        public const int SATURATION_VALUE_MAX = 255;

        [JsonProperty("hMin")] private int m_HMin;
        [JsonProperty("hMax")] private int m_HMax;
        [JsonProperty("sMin")] private int m_SMin;
        [JsonProperty("sMax")] private int m_SMax;
        [JsonProperty("vMin")] private int m_VMin;
        [JsonProperty("vMax")] private int m_VMax;

        public int hMin { get => m_HMin; set => m_HMin = Mathf.Clamp(value, 0,        m_HMax); }
        public int hMax { get => m_HMax; set => m_HMax = Mathf.Clamp(value, m_HMin,   HUE_MAX); }
        public int sMin { get => m_SMin; set => m_SMin = Mathf.Clamp(value, 0,        m_SMax); }
        public int sMax { get => m_SMax; set => m_SMax = Mathf.Clamp(value, m_SMin,   SATURATION_VALUE_MAX); }
        public int vMin { get => m_VMin; set => m_VMin = Mathf.Clamp(value, 0,        m_VMax); }
        public int vMax { get => m_VMax; set => m_VMax = Mathf.Clamp(value, m_VMin,   SATURATION_VALUE_MAX); }
    }
}
