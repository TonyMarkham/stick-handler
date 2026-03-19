using System;
using UnityEngine;
using UnityEngine.UIElements;

[CreateAssetMenu(fileName = "UiElements", menuName = "Stick Handler/UI Elements" )]
[Serializable]
public class UiElements : ScriptableObject
{
    [SerializeField] private GameObject m_CalibrationGameObject;
    [SerializeField] private UIDocument m_CalibrationUiDocument;
}
