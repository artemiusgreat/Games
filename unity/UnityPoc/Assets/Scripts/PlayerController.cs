using System;
using UnityEngine;

// WASD relative to facing, Shift = run, gravity + terrain following.
// Height sampling on purpose (Unity's CharacterController would need a terrain
// collider; sampling is less code and matches the Stride/Flax versions).
public class PlayerController : MonoBehaviour
{
    private float _yaw;
    private float _vy;
    private bool _walkTest;
    private float _testTime;
    private Vector3 _startPos;

    private void Start()
    {
        foreach (var arg in Environment.GetCommandLineArgs())
            if (arg == "--walk-test") _walkTest = true;
        _startPos = transform.position;
        if (_walkTest)
            Debug.Log($"SMOKE start=({_startPos.x:F2}, {_startPos.y:F2}, {_startPos.z:F2})");
    }

    private void Update()
    {
        float iv, ih;
        if (_walkTest)
        {
            _testTime += Time.deltaTime;
            iv = 1; ih = 0;
            if (_testTime >= 3.5f)
            {
                var p = transform.position;
                Debug.Log($"SMOKE end=({p.x:F2}, {p.y:F2}, {p.z:F2}) moved={Vector3.Distance(_startPos, p):F2}");
                Application.Quit();
                return;
            }
        }
        else
        {
            _yaw += Input.GetAxis("Mouse X") * 3.0f;
            iv = Input.GetAxis("Vertical");
            ih = Input.GetAxis("Horizontal");
        }

        bool run = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
        float speed = run ? 9.5f : 5.0f;

        var wish = Quaternion.Euler(0, _yaw, 0) * new Vector3(ih, 0, iv);
        if (wish.magnitude > 1) wish.Normalize();

        var pos = transform.position + wish * (speed * Time.deltaTime);
        pos.x = Mathf.Clamp(pos.x, -48, 48);
        pos.z = Mathf.Clamp(pos.z, -48, 48);
        _vy -= 22f * Time.deltaTime;
        pos.y += _vy * Time.deltaTime;
        float ground = WorldBuilder.HeightAt(pos.x, pos.z) + 0.9f; // capsule center = feet + 0.9
        if (pos.y <= ground) { pos.y = ground; _vy = 0; }
        transform.position = pos;
        transform.rotation = Quaternion.Euler(0, _yaw, 0);
    }
}
