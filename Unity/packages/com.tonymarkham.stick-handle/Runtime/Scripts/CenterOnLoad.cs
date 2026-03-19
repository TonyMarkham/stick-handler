using System.Linq;
using Meta.XR.MRUtilityKit;
using UnityEngine;

namespace StickHandle.Scripts
{
    public class CenterOnLoad : MonoBehaviour
    {
        private void Start()
        {
            MRUK.Instance.SceneLoadedEvent.AddListener(OnSceneLoaded);
        }

        void OnSceneLoaded()
        {
            if (MRUK.Instance.GetCurrentRoom() is not { } room
                || room.FloorAnchors is not { } floorAnchors
                || floorAnchors.FirstOrDefault() is not { } floorAnchor)
            {
                return;
            }

            Vector3 pos = floorAnchor.GetAnchorCenter();
            Quaternion rot = floorAnchor.gameObject.transform.rotation;
            
            gameObject.transform.position = pos;
            gameObject.transform.rotation = rot;

            // Optional: create your own persistent spatial anchor here
            gameObject.AddComponent<OVRSpatialAnchor>();
        }
    }
}
