using System;
using UnityEngine;

namespace StickHandle.Scripts
{
    [Serializable]
    public class HsvPreset
    {
        public const int HUE_MAX              = 179;
        public const int SATURATION_VALUE_MAX = 255;

        [SerializeField] private int m_HMin, m_HMax, m_SMin, m_SMax, m_VMin, m_VMax;

        public int hMin { get => m_HMin; set => m_HMin = Mathf.Clamp(value, 0, HUE_MAX); }
        public int hMax { get => m_HMax; set => m_HMax = Mathf.Clamp(value, 0, HUE_MAX); }
        public int sMin { get => m_SMin; set => m_SMin = Mathf.Clamp(value, 0, SATURATION_VALUE_MAX); }
        public int sMax { get => m_SMax; set => m_SMax = Mathf.Clamp(value, 0, SATURATION_VALUE_MAX); }
        public int vMin { get => m_VMin; set => m_VMin = Mathf.Clamp(value, 0, SATURATION_VALUE_MAX); }
        public int vMax { get => m_VMax; set => m_VMax = Mathf.Clamp(value, 0, SATURATION_VALUE_MAX); }
    }
}
