using UnityEngine;

// Third-person follow: behind + above the target, looking at its chest.
public class CameraRig : MonoBehaviour
{
    public Transform Target;

    private void LateUpdate()
    {
        if (Target == null) return;
        var back = Target.rotation * Vector3.back;
        transform.position = Target.position + back * 7.5f + Vector3.up * 3.2f
                             - Vector3.up * 0.9f; // target pos is capsule center
        transform.LookAt(Target.position + Vector3.up * 0.7f);
    }
}
