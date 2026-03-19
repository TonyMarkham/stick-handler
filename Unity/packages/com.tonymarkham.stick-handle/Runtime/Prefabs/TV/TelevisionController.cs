using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace StickHandle.Prefabs.TV
{
    public class TelevisionController : MonoBehaviour
    {
        [SerializeField] private GameObject m_BodyGameobject;
        public GameObject BodyGameobject => m_BodyGameobject;
        
        [SerializeField] private GameObject m_ButtonGameobject;
        
        [SerializeField] private GameObject m_ScreenGameobject;
        public GameObject ScreenGameobject => m_ScreenGameobject;
        
        [SerializeField] private Material m_OnMaterial;
        [SerializeField] private Material m_OffMaterial;
        
        private MeshRenderer m_PowerButtonMeshRenderer;
        private MeshRenderer PowerButtonMeshRenderer
        {
            get
            {
                if (m_PowerButtonMeshRenderer)
                    return m_PowerButtonMeshRenderer;

                m_PowerButtonMeshRenderer = m_ButtonGameobject.GetComponent<MeshRenderer>();
                return m_PowerButtonMeshRenderer;
            }
        }
        
        private MeshRenderer m_ScreenMeshRenderer;
        public MeshRenderer ScreenMeshRenderer
        {
            get
            {
                if (m_ScreenMeshRenderer)
                    return m_ScreenMeshRenderer;

                m_ScreenMeshRenderer = m_ScreenGameobject.GetComponent<MeshRenderer>();
                return m_ScreenMeshRenderer;
            }
        }

        [SerializeField] private float m_Scale;
        public float Scale => m_Scale;
        
        [SerializeField] private bool m_IsOn;
        public bool IsOn => m_IsOn;
        
        private List<Collider> m_Colliders;
        public List<Collider> Colliders
        {
            get
            {
                if(m_Colliders is not null && m_Colliders.Count > 0)
                    return m_Colliders;

                m_Colliders = GetComponents<Collider>().ToList();
                return m_Colliders;
            }
        }

        public void Switch()
        {
            m_IsOn = !m_IsOn;

            SwitchInternal();
        }

        public bool Switch(bool isOn)
        {
            m_IsOn = isOn;
            
            SwitchInternal();
            return isOn;
        }
        
        protected void SwitchInternal()
        {
            if (m_IsOn)
            {
                PowerButtonMeshRenderer.sharedMaterial = m_OnMaterial;
                return;
            }
            
            PowerButtonMeshRenderer.sharedMaterial = m_OffMaterial;
        }
        
        [ContextMenu(itemName:"Switch")]
        public void SwitchOnOff()
        {
            Switch();
        }
    }
}
