using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class GripperController : MonoBehaviour
{
    [Header("손가락 (Prismatic ArticulationBody)")]
    public ArticulationBody rightFinger;
    public ArticulationBody leftFinger;
    public FingerContact rightContact;
    public FingerContact leftContact;

    [Header("열림/닫힘 값 (m)")]
    public float rightOpen = -0.005f;
    public float rightClose = 0.005f;
    public float leftOpen = 0.005f;
    public float leftClose = -0.005f;
    public float moveTime = 0.5f;
    [Tooltip("목표가 닫힘에 도달한 뒤에도 손가락이 늦게 닿는 것을 대비해 기다리는 최대 시간(초)")]
    public float extraCloseTime = 0.5f;

    [Header("부품을 붙일 위치 (손가락 사이 정중앙)")]
    public Transform attachPoint;

    [Header("파지 판정 (마찰 원뿔)")]
    public bool useGraspCheck = true;
    [Tooltip("손가락과 부품 사이 마찰계수. 허용 각도 = arctan(μ). 0.36 → 약 20°")]
    public float frictionCoefficient = 0.36f;
    [Tooltip("손가락 패드 절반 높이 (m) - 광선 격자의 위아래 범위")]
    public float padHalfHeight = 0.006f;
    [Tooltip("손가락 패드 절반 폭 (m) - 광선 격자의 좌우 범위")]
    public float padHalfWidth = 0.006f;
    [Tooltip("판정 실패 시 손가락을 살짝 벌려 '미끄러진' 상태로 만드는 거리 (m)")]
    public float slipBackoff = 0.001f;

    [Header("놓은 뒤 충돌을 다시 켜기까지 대기(초)")]
    public float restoreCollisionDelay = 0.3f;

    [Header("테스트")]
    public bool showTestButtons = true;

    public Rigidbody HeldPart { get; private set; }
    public bool IsBusy { get; private set; }

    public bool LastGraspOk { get; private set; }
    public float LastAngleRight { get; private set; }
    public float LastAngleLeft { get; private set; }
    public string LastGraspInfo { get; private set; } = "-";
    public float MaxAngle => Mathf.Atan(frictionCoefficient) * Mathf.Rad2Deg;

    Collider[] robotColliders;
    readonly List<Collider> ignoredPartColliders = new List<Collider>();

    Vector3 dbgHitA, dbgHitB, dbgNormA, dbgNormB;
    bool dbgValid;

    void Start()
    {
        robotColliders = transform.root.GetComponentsInChildren<Collider>(true);
        SetTargets(rightOpen, leftOpen);
    }

    public IEnumerator Close()
    {
        if (IsBusy) yield break;
        IsBusy = true;
        LastGraspOk = false;

        bool predictedOk = PredictGrasp(out Rigidbody predictedPart);

        string pred = LastGraspInfo;

        Vector3 partPos0 = predictedPart != null ? predictedPart.position : Vector3.zero;
        Quaternion partRot0 = predictedPart != null ? predictedPart.rotation : Quaternion.identity;
        float elapsed = 0f;
        float rightFirst = -1f;
        float leftFirst = -1f;

        float startR = rightFinger.jointPosition[0];
        float startL = leftFinger.jointPosition[0];
        float t = 0f;
        bool touched = false;

        float extra = 0f;

        while (t < 1f || extra < extraCloseTime)
        {
            if (t < 1f)
            {
                t += Time.fixedDeltaTime / moveTime;
                SetTargets(Mathf.Lerp(startR, rightClose, t),
                           Mathf.Lerp(startL, leftClose, t));
            }
            else
            {
                extra += Time.fixedDeltaTime;
            }

            elapsed += Time.fixedDeltaTime;

            if (rightFirst < 0f && rightContact.TouchingPart != null) rightFirst = elapsed;
            if (leftFirst < 0f && leftContact.TouchingPart != null) leftFirst = elapsed;

            Rigidbody r = rightContact.TouchingPart;
            if (r != null && r == leftContact.TouchingPart)
            {
                touched = true;
                float posR = rightFinger.jointPosition[0];
                float posL = leftFinger.jointPosition[0];

                bool ok = !useGraspCheck || (predictedOk && r == predictedPart);
                if (ok)
                {
                    SetTargets(posR, posL);
                    Attach(r);
                    LastGraspOk = true;
                }
                else
                {
                    SetTargets(posR + Mathf.Sign(rightOpen - rightClose) * slipBackoff,
                               posL + Mathf.Sign(leftOpen - leftClose) * slipBackoff);
                }
                break;
            }
            yield return new WaitForFixedUpdate();
        }

        string result;
        if (LastGraspOk) result = "성공";
        else if (touched) result = "실패: 미끄러짐 (판정 각도 초과)";
        else result = "실패: 양쪽 손가락이 동시에 닿지 않음";
        LastGraspInfo = $"{result}\n  닫기 전 판정: {pred}";

        string rightTime = "안 닿음";
        if (rightFirst >= 0f) rightTime = $"{rightFirst:F2}초";

        string leftTime = "안 닿음";
        if (leftFirst >= 0f) leftTime = $"{leftFirst:F2}초";

        float movedMm = 0f;
        float turnedDeg = 0f;
        if (predictedPart != null)
        {
            movedMm = Vector3.Distance(predictedPart.position, partPos0) * 1000f;
            turnedDeg = Quaternion.Angle(predictedPart.rotation, partRot0);
        }

        float rightPos = rightFinger.jointPosition[0];
        float leftPos = leftFinger.jointPosition[0];
        float rightTarget = rightFinger.xDrive.target;
        float leftTarget = leftFinger.xDrive.target;

        Debug.Log($"[Grasp] {result} | 닫기 전 판정: {pred} | " +
                  $"처음 닿은 시간 R {rightTime}, L {leftTime} | " +
                  $"손가락 위치 {rightPos:F4} / {leftPos:F4} (목표 {rightTarget:F4} / {leftTarget:F4}) | " +
                  $"부품 이동 {movedMm:F1}mm, 회전 {turnedDeg:F1}도");

        IsBusy = false;
    }

    public IEnumerator Open()
    {
        if (IsBusy) yield break;
        IsBusy = true;

        Detach();
        float startR = rightFinger.jointPosition[0];
        float startL = leftFinger.jointPosition[0];
        float t = 0f;

        while (t < 1f)
        {
            t += Time.fixedDeltaTime / moveTime;
            SetTargets(Mathf.Lerp(startR, rightOpen, t),
                       Mathf.Lerp(startL, leftOpen, t));
            yield return new WaitForFixedUpdate();
        }

        yield return new WaitForSeconds(restoreCollisionDelay);
        SetIgnoreCollision(false);
        IsBusy = false;
    }

    bool PredictGrasp(out Rigidbody part)
    {
        part = null;
        dbgValid = false;

        Vector3 axis = (rightFinger.transform.rotation * (rightFinger.anchorRotation * Vector3.right)).normalized;

        Vector3 up = Vector3.ProjectOnPlane(Vector3.up, axis).normalized;

        Vector3 side = Vector3.Cross(axis, up).normalized;
        Vector3 center = attachPoint.position;

        bool okA = FirstContact(center, axis, up, side, +1f, out RaycastHit a, out Rigidbody partA);
        bool okB = FirstContact(center, axis, up, side, -1f, out RaycastHit b, out Rigidbody partB);

        if (!okA || !okB || partA != partB)
        {
            LastGraspInfo = "실패: 손가락 사이에 부품이 없음";
            return false;
        }
        part = partA;

        LastAngleRight = Vector3.Angle(a.normal, axis);
        LastAngleLeft = Vector3.Angle(b.normal, -axis);

        dbgHitA = a.point;
        dbgNormA = a.normal;
        dbgHitB = b.point;
        dbgNormB = b.normal;
        dbgValid = true;

        bool ok = LastAngleRight <= MaxAngle && LastAngleLeft <= MaxAngle;
        LastGraspInfo = $"{(ok ? "성공" : "실패: 미끄러짐")} ({LastAngleRight:F1}°, {LastAngleLeft:F1}° / 한계 {MaxAngle:F1}°)";
        return ok;
    }

    bool FirstContact(Vector3 center, Vector3 axis, Vector3 up, Vector3 side, float sideSign,
                      out RaycastHit best, out Rigidbody bestPart)
    {
        best = default;
        bestPart = null;
        float bestDist = float.MaxValue;

        Vector3 dir = -sideSign * axis;

        for (int iu = -1; iu <= 1; iu++)
            for (int iv = -1; iv <= 1; iv++)
            {
                Vector3 origin = center + up * (iu * padHalfHeight) + side * (iv * padHalfWidth)
                                 + sideSign * axis * 0.1f;

                foreach (var h in Physics.RaycastAll(origin, dir, 0.2f, ~0, QueryTriggerInteraction.Ignore))
                {
                    Rigidbody rb = h.collider.attachedRigidbody;
                    if (rb == null || !rb.CompareTag("Part") || rb == HeldPart) continue;

                    if (h.distance < bestDist)
                    {
                        bestDist = h.distance;
                        best = h;
                        bestPart = rb;
                    }
                }
            }
        return bestPart != null;
    }

    void Attach(Rigidbody part)
    {
        HeldPart = part;
        part.isKinematic = true;
        part.transform.SetParent(attachPoint, true);

        ignoredPartColliders.Clear();
        ignoredPartColliders.AddRange(part.GetComponentsInChildren<Collider>());
        SetIgnoreCollision(true);
    }

    void Detach()
    {
        if (HeldPart == null) return;
        HeldPart.transform.SetParent(null, true);
        HeldPart.isKinematic = false;
        HeldPart = null;
        rightContact.Clear();
        leftContact.Clear();
    }

    void SetIgnoreCollision(bool ignore)
    {
        foreach (var partCollider in ignoredPartColliders)
        {
            if (partCollider == null) continue;
            foreach (var robotCollider in robotColliders)
                if (robotCollider != null && robotCollider != partCollider)
                    Physics.IgnoreCollision(partCollider, robotCollider, ignore);
        }
        if (!ignore) ignoredPartColliders.Clear();
    }

    void SetTargets(float right, float left)
    {
        var d = rightFinger.xDrive;
        d.target = right;
        rightFinger.xDrive = d;

        d = leftFinger.xDrive;
        d.target = left;
        leftFinger.xDrive = d;
    }

    void OnDrawGizmos()
    {
        if (!dbgValid) return;
        Gizmos.color = Color.yellow;
        Gizmos.DrawLine(dbgHitA, dbgHitA + dbgNormA * 0.03f);
        Gizmos.DrawLine(dbgHitB, dbgHitB + dbgNormB * 0.03f);
        Gizmos.color = Color.white;
        Gizmos.DrawLine(dbgHitA, dbgHitB);
        Gizmos.DrawSphere(dbgHitA, 0.0015f);
        Gizmos.DrawSphere(dbgHitB, 0.0015f);
    }

    static string Name(Rigidbody rb) => rb != null ? rb.name : "-";

    void OnGUI()
    {
        if (!showTestButtons) return;

        GUILayout.BeginArea(new Rect(10, 10, 280, 200), GUI.skin.box);
        if (GUILayout.Button("Open")) StartCoroutine(Open());
        if (GUILayout.Button("Close")) StartCoroutine(Close());
        GUILayout.Label($"Right / Left 위치: {rightFinger.jointPosition[0]:F4} / {leftFinger.jointPosition[0]:F4}");
        GUILayout.Label($"닿음: {Name(rightContact.TouchingPart)} / {Name(leftContact.TouchingPart)}");
        GUILayout.Label($"잡은 부품: {Name(HeldPart)}");
        GUILayout.Label($"판정: {LastGraspInfo}");
        GUILayout.EndArea();
    }
}
