using UnityEngine;

public class FingerContact : MonoBehaviour
{
    [Tooltip("마지막 접촉 후 이 시간(초)까지는 '닿아 있음'으로 인정 (접촉 신호가 스텝마다 끊기는 것 대비)")]
    public float holdTime = 0.1f;

    Rigidbody touching;
    float lastTouchTime = -1f;

    public Rigidbody TouchingPart
    {
        get
        {
            if (touching == null || Time.fixedTime - lastTouchTime > holdTime)
                return null;
            return touching;
        }
    }

    void OnCollisionEnter(Collision collision) => Record(collision);
    void OnCollisionStay(Collision collision) => Record(collision);

    void Record(Collision collision)
    {
        Rigidbody rb = collision.collider.attachedRigidbody;

        if (rb != null && rb.CompareTag("Part"))
        {
            touching = rb;
            lastTouchTime = Time.fixedTime;
        }
    }

    public void Clear()
    {
        touching = null;
        lastTouchTime = -1f;
    }
}
