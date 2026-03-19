using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;

/// <summary>
/// XRGrabInteractable that clamps the grabbed object's Y position to a fixed floor height.
/// Bakes the Y constraint into the interactable — no separate script fighting the grab system each frame.
/// </summary>
public class FloorConstrainedGrabInteractable : UnityEngine.XR.Interaction.Toolkit.Interactables.XRGrabInteractable
{
    [SerializeField] private float _floorY = 0f;

    public override void ProcessInteractable(XRInteractionUpdateOrder.UpdatePhase updatePhase)
    {
        base.ProcessInteractable(updatePhase);

        if (isSelected)
        {
            Vector3 pos = transform.position;
            pos.y = _floorY;
            transform.position = pos;
        }
    }
}
