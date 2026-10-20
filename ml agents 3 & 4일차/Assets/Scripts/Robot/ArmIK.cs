using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class ArmIK : MonoBehaviour
{
    #region Inspector 

    [Header("관절 (비워두면 이름으로 자동 검색)")]
    public ArticulationBody[] joints = new ArticulationBody[0];
    static readonly string[] JointNames =
        { "shoulder_link", "arm_link", "elbow_link", "forearm_link", "wrist_link", "hand_link" };
    public Transform tip;                       // AttachPoint
    public ArticulationBody closingFinger;      // right_gripper

    [Header("관절 회전 방향 (처음 한 번 자동 확인 후 저장)")]
    public bool signsCalibrated = false;
    public float[] jointSigns = new float[0];

    [Header("자세")]
    [Tooltip("그리퍼가 항상 수직 아래를 보도록")]
    public bool pointDown = true;
    [Tooltip("자세 오차 1 rad를 위치 오차 몇 m로 볼지")]
    public float orientationWeight = 0.2f;

    [Header("이동")]
    public float jointSpeed = 60f;         // 관절 이동 속도 (도/초)
    public float minMoveTime = 0.3f;
    public float linearStep = 0.01f;        // 직선 경로를 몇 m 간격으로 나눌지
    public int correctionPasses = 3;        // 도착 후 처짐 보정 횟수
    public float positionTolerance = 0.001f;

    [Header("테스트: IKTarget을 끌면 따라가기 (IKTarget의 Rotation Y = 손목 각도)")]
    public Transform testTarget;
    public bool followTestTarget = false;

    public bool Ready { get; private set; }
    public bool IsMoving { get; private set; }
    public bool LastSolveOk { get; private set; } = true;
    public float PositionError { get; private set; }   // 마지막 이동 후 실제 위치 오차 (m)
    public float AngleError { get; private set; }      // 마지막 이동 후 수직 오차 (도)
    public float YawError { get; private set; }        // 마지막 이동 후 손목 각도 오차 (도)

    #endregion

    #region 계산용 모델 변수 

    private int n;
    private Transform baseT;
    private Quaternion[] relRot0;
    private Vector3[] pivotInParent, anchorPos, axisLocal;
    private float[] theta0, lower, upper;
    private bool[] limited;
    private Vector3 tipLocal, dirLocal, closeLocal;
    private float? activeYaw;
    private Vector3 lastFollowPos;
    private float lastFollowYaw;
    private bool firstMove = true;

    #endregion

    #region 1. 로봇 구조를 읽고, 계산용 모델 만들기

    IEnumerator Start()
    {
        AutoAssign();
        BuildModel();

        if (!signsCalibrated || jointSigns == null || jointSigns.Length != n)
            yield return CalibrateSigns();

        Ready = true;
    }

    void AutoAssign()
    {
        var bodies = GetComponentsInChildren<ArticulationBody>();
        if (joints == null || joints.Length == 0)
        {
            joints = new ArticulationBody[JointNames.Length];
            for (int i = 0; i < JointNames.Length; i++)
                foreach (var b in bodies)
                    if (b.name == JointNames[i]) joints[i] = b;
        }
        if (closingFinger == null)
            foreach (var b in bodies)
                if (b.name == "right_gripper") closingFinger = b;
        if (tip == null)
            foreach (var t in GetComponentsInChildren<Transform>())
                if (t.name == "AttachPoint") tip = t;

        for (int i = 0; i < joints.Length; i++)
            if (joints[i] == null) Debug.LogError($"[ArmIK] {i}번 관절을 찾지 못했습니다. Inspector에서 직접 넣어 주세요.");
        if (tip == null) Debug.LogError("[ArmIK] AttachPoint를 찾지 못했습니다.");
        if (closingFinger == null) Debug.LogError("[ArmIK] right_gripper를 찾지 못했습니다.");
    }

    void BuildModel()
    {
        n = joints.Length;
        baseT = joints[0].transform.parent;   // base_link
        relRot0 = new Quaternion[n];
        pivotInParent = new Vector3[n];
        anchorPos = new Vector3[n];
        axisLocal = new Vector3[n];
        theta0 = new float[n];
        lower = new float[n]; upper = new float[n]; limited = new bool[n];

        for (int j = 0; j < n; j++)
        {
            Transform c = joints[j].transform, p = c.parent;
            Quaternion inv = Quaternion.Inverse(p.rotation);
            relRot0[j] = inv * c.rotation;
            anchorPos[j] = joints[j].anchorPosition;
            axisLocal[j] = joints[j].anchorRotation * Vector3.right;
            pivotInParent[j] = inv * (c.position - p.position) + relRot0[j] * anchorPos[j];
            theta0[j] = joints[j].jointPosition[0] * Mathf.Rad2Deg;

            var d = joints[j].xDrive;
            limited[j] = joints[j].twistLock == ArticulationDofLock.LimitedMotion;
            lower[j] = d.lowerLimit; upper[j] = d.upperLimit;
        }

        Transform hand = joints[n - 1].transform;
        Quaternion handInv = Quaternion.Inverse(hand.rotation);
        tipLocal = handInv * (tip.position - hand.position);
        dirLocal = (tipLocal - anchorPos[n - 1]).normalized;   // 손목 회전 중심에서 그리퍼 끝을 향하는 방향
        closeLocal = handInv * ActualCloseAxis();              // 손가락이 닫히는 축 (손 기준)
    }

    IEnumerator CalibrateSigns()
    {
        jointSigns = new float[n];
        for (int j = 0; j < n; j++)
        {
            float mid = (lower[j] + upper[j]) * 0.5f;
            SetTarget(j, theta0[j] + (theta0[j] <= mid ? 4f : -4f));
        }
        yield return new WaitForSeconds(0.3f);

        for (int j = 0; j < n; j++)
        {
            Transform c = joints[j].transform;
            Quaternion actual = Quaternion.Inverse(c.parent.rotation) * c.rotation;
            float moved = joints[j].jointPosition[0] * Mathf.Rad2Deg - theta0[j];
            float ePlus = Quaternion.Angle(relRot0[j] * Quaternion.AngleAxis(moved, axisLocal[j]), actual);
            float eMinus = Quaternion.Angle(relRot0[j] * Quaternion.AngleAxis(-moved, axisLocal[j]), actual);
            jointSigns[j] = ePlus <= eMinus ? 1f : -1f;
            SetTarget(j, theta0[j]);
        }
        yield return new WaitForSeconds(0.3f);

        signsCalibrated = true;
        Debug.Log($"[ArmIK] 회전 방향 확인 완료: {string.Join(", ", jointSigns)}\n" +
                  "Play 중에 ArmIK 컴포넌트 메뉴(점 3개)에서 Copy Component, Play 끄고 Paste Component Values 하면 다음부터 움찔거림이 없습니다.");
    }

    #endregion

    #region 2. 목표 받기 - 위치 + 아래 보기 + 손목 각도

    void Update()
    {
        if (!Ready || !followTestTarget || testTarget == null || IsMoving) return;
        float yaw = testTarget.eulerAngles.y;
        if ((testTarget.position - lastFollowPos).sqrMagnitude > 0.002f * 0.002f ||
            Mathf.Abs(Mathf.DeltaAngle(yaw, lastFollowYaw)) > 1f)
        {
            lastFollowPos = testTarget.position;
            lastFollowYaw = yaw;
            StartCoroutine(MoveTo(testTarget.position, yaw));
        }
    }

    public IEnumerator MoveTo(Vector3 goal, float? yawDeg = null)
    {
        // [미션] 빈칸을 채워 MoveTo를 완성하세요

        // 1. 이동 중임을 표시하세요 (IsMoving)
        //여기에 입력하세요

        // 2. 목표 손목 각도(yawDeg)를 activeYaw에 기억하세요 (아래 Solve가 이 값을 씁니다)
        //여기에 입력하세요

        // 3.목표 각도 계산을 통해 실제로 움직이기
        float[] current = CurrentTargets();          // 지금 관절 각도 6개 = 출발 자세
        float[] solution = new float[n];             // 계산 결과를 담을 빈 칸 = 도착 자세

        LastSolveOk = Solve(goal, activeYaw, current, solution);
        if (!LastSolveOk) Debug.LogWarning($"[ArmIK] {goal} (손목 {yawDeg}도) 에 정확히 닿는 자세가 없습니다. 가장 가까운 자세로 이동합니다.");

        yield return Drive(solution, MoveTime(current, solution), true);   // 이동 시간 = 가장 많이 도는 관절 기준

        // 4. Settle을 호출해서 관절이 다 따라올 때까지 기다리세요 (코루틴 사용법 생각해보기) 
        //여기에 입력하세요

        // 5. Correct로 목표 위치(goal)에 맞게 보정하세요  (코루틴 사용법 생각해보기) 
        //여기에 입력하세요

        // 6. 이동이 끝났음을 표시하세요 (IsMoving)
        //여기에 입력하세요
    }
    public IEnumerator MoveLinear(Vector3 goal, float speed, float? yawDeg = null)
    {
        IsMoving = true;
        activeYaw = yawDeg;

        float[] startAngles = CurrentTargets();
        Forward(startAngles, out Vector3 startPos, out _, out _, null, null);

        float distance = Vector3.Distance(startPos, goal);
        int pointCount = Mathf.Max(1, Mathf.CeilToInt(distance / linearStep));
        float pointTime = Mathf.Max(Time.fixedDeltaTime, distance / Mathf.Max(speed, 0.001f) / pointCount);

        List<float[]> pathAngles = new List<float[]>();
        float[] prevAngles = startAngles;
        bool allOk = true;
        for (int i = 1; i <= pointCount; i++)
        {
            Vector3 point = Vector3.Lerp(startPos, goal, (float)i / pointCount);
            float[] pointAngles = new float[n];
            allOk &= Solve(point, activeYaw, prevAngles, pointAngles);
            pathAngles.Add(pointAngles);
            prevAngles = pointAngles;
        }
        LastSolveOk = allOk;
        if (!allOk) Debug.LogWarning($"[ArmIK] 직선 경로 일부가 닿을 수 없는 범위입니다. (목표 {goal}, 손목 {yawDeg}도)");

        foreach (float[] pointAngles in pathAngles)
        {
            yield return Drive(pointAngles, pointTime, false);
        }
        yield return Settle();
        yield return Correct(goal);

        IsMoving = false;
    }

    #endregion

    #region 3. 머릿속 계산 - FK로 예측하고 고치기

    // FK(순기구학) 관절 각도를 넣으면 그리퍼 끝 위치, 그리퍼 방향, 손가락 닫히는 축을 계산한다
    void Forward(float[] angles, out Vector3 tipPos, out Vector3 tipDir, out Vector3 closeAxis, Vector3[] jointAxes, Vector3[] jointPivots)
    {
        Vector3 linkPos = baseT.position;
        Quaternion linkRot = baseT.rotation;

        for (int j = 0; j < n; j++)
        {
            Vector3 pivotWorld = linkPos + linkRot * pivotInParent[j];

            float turn = jointSigns[j] * (angles[j] - theta0[j]);
            Quaternion childRot = linkRot * relRot0[j] * Quaternion.AngleAxis(turn, axisLocal[j]);

            if (jointAxes != null)
            {
                jointAxes[j] = jointSigns[j] * (childRot * axisLocal[j]);
                jointPivots[j] = pivotWorld;
            }

            linkPos = pivotWorld - childRot * anchorPos[j];
            linkRot = childRot;
        }

        tipPos = linkPos + linkRot * tipLocal;
        tipDir = linkRot * dirLocal;
        closeAxis = linkRot * closeLocal;
    }

    static Vector3 YawErrorVector(Vector3 closeAxis, float yawDeg, out float errorDeg)
    {
        Vector3 targetDir = Quaternion.AngleAxis(yawDeg, Vector3.up) * Vector3.forward;
        Vector3 currentDir = Vector3.ProjectOnPlane(closeAxis, Vector3.up).normalized;
        if (Vector3.Dot(currentDir, targetDir) < 0f) targetDir = -targetDir;
        errorDeg = Vector3.Angle(currentDir, targetDir);
        return Vector3.Cross(currentDir, targetDir);
    }

    // IK  목표와의 오차 확인해 각도를 조금 고치기를 최대 300번 반복한다
    bool Solve(Vector3 goal, float? yaw, float[] startAngles, float[] result)
    {
        float[] angles = (float[])startAngles.Clone();
        Vector3[] jointAxes = new Vector3[n];
        Vector3[] jointPivots = new Vector3[n];

        bool useYaw = pointDown && yaw.HasValue;
        int errorCount = 3 + (pointDown ? 2 : 0) + (useYaw ? 1 : 0);

        float rotWeight = orientationWeight;

        float[,] jacobian = new float[errorCount, n];
        float[] errors = new float[errorCount];
        float[,] matrixA = new float[errorCount, errorCount];
        const float damping = 0.02f;
        float yawErrorDeg = 0f;

        for (int iter = 0; iter < 300; iter++)
        {
            Forward(angles, out Vector3 tipPos, out Vector3 tipDir, out Vector3 closeAxis, jointAxes, jointPivots);

            Vector3 posError = goal - tipPos;
            Vector3 downError = Vector3.Cross(tipDir, Vector3.down);
            Vector3 yawError = useYaw ? YawErrorVector(closeAxis, yaw.Value, out yawErrorDeg) : Vector3.zero;

            if (posError.magnitude < 0.0005f &&
                (!pointDown || Vector3.Angle(tipDir, Vector3.down) < 0.5f) &&
                (!useYaw || yawErrorDeg < 0.5f)) break;

            errors[0] = posError.x; errors[1] = posError.y; errors[2] = posError.z;
            if (pointDown) { errors[3] = rotWeight * downError.x; errors[4] = rotWeight * downError.z; }
            if (useYaw) errors[5] = rotWeight * yawError.y;

            for (int j = 0; j < n; j++)
            {
                Vector3 tipMove = Vector3.Cross(jointAxes[j], tipPos - jointPivots[j]);
                jacobian[0, j] = tipMove.x; jacobian[1, j] = tipMove.y; jacobian[2, j] = tipMove.z;
                if (pointDown) { jacobian[3, j] = rotWeight * jointAxes[j].x; jacobian[4, j] = rotWeight * jointAxes[j].z; }
                if (useYaw) jacobian[5, j] = rotWeight * jointAxes[j].y;
            }

            for (int row = 0; row < errorCount; row++)
            {
                for (int col = 0; col < errorCount; col++)
                {
                    float sum = 0f;
                    for (int k = 0; k < n; k++) sum += jacobian[row, k] * jacobian[col, k];
                    matrixA[row, col] = sum + (row == col ? damping * damping : 0f);
                }
            }
            float[] solved = SolveLinear(matrixA, errors, errorCount);
            if (solved == null) break;

            for (int j = 0; j < n; j++)
            {
                float deltaRad = 0f;
                for (int row = 0; row < errorCount; row++) deltaRad += jacobian[row, j] * solved[row];
                angles[j] += Mathf.Clamp(deltaRad * Mathf.Rad2Deg, -10f, 10f);
                if (limited[j]) angles[j] = Mathf.Clamp(angles[j], lower[j], upper[j]);
            }
        }

        System.Array.Copy(angles, result, n);

        Forward(angles, out Vector3 finalPos, out Vector3 finalDir, out Vector3 finalClose, null, null);
        float finalYawDeg = 0f;
        if (useYaw) YawErrorVector(finalClose, yaw.Value, out finalYawDeg);
        return (goal - finalPos).magnitude < 0.002f &&
               (!pointDown || Vector3.Angle(finalDir, Vector3.down) < 3f) &&
               (!useYaw || finalYawDeg < 3f);
    }

    // 가우스 소거법으로 연립방정식 A x = b 를 푼다 (Solve에서 각도 고칠 양을 구할 때 사용)
    // 풀 수 없으면 null을 돌려준다
    static float[] SolveLinear(float[,] A, float[] b, int n)
    {
        var M = (float[,])A.Clone();
        var v = (float[])b.Clone();
        for (int col = 0; col < n; col++)
        {
            int piv = col;
            for (int r = col + 1; r < n; r++)
                if (Mathf.Abs(M[r, col]) > Mathf.Abs(M[piv, col])) piv = r;
            if (Mathf.Abs(M[piv, col]) < 1e-9f) return null;
            if (piv != col)
            {
                for (int c = 0; c < n; c++) { float t = M[col, c]; M[col, c] = M[piv, c]; M[piv, c] = t; }
                float tv = v[col]; v[col] = v[piv]; v[piv] = tv;
            }
            for (int r = col + 1; r < n; r++)
            {
                float f = M[r, col] / M[col, col];
                for (int c = col; c < n; c++) M[r, c] -= f * M[col, c];
                v[r] -= f * v[col];
            }
        }
        var x = new float[n];
        for (int r = n - 1; r >= 0; r--)
        {
            float s = v[r];
            for (int c = r + 1; c < n; c++) s -= M[r, c] * x[c];
            x[r] = s / M[r, r];
        }
        return x;
    }

    #endregion

    #region 4. 실제로 움직이기 - xDrive target 바꾸고 기다리기

    float MoveTime(float[] a, float[] b)
    {
        float max = 0f;
        for (int j = 0; j < n; j++) max = Mathf.Max(max, Mathf.Abs(b[j] - a[j]));
        return Mathf.Max(minMoveTime, max / jointSpeed);
    }

    IEnumerator Drive(float[] goal, float duration, bool smooth)
    {
        float[] start = CurrentTargets();
        float t = 0f;
        while (t < 1f)
        {
            t += Time.fixedDeltaTime / duration;
            float s = smooth ? Mathf.SmoothStep(0f, 1f, t) : Mathf.Clamp01(t);
            for (int j = 0; j < n; j++) SetTarget(j, Mathf.Lerp(start[j], goal[j], s));
            yield return new WaitForFixedUpdate();
        }
    }

    IEnumerator Settle()
    {
        float wait = 0f;
        while (wait < 1f)
        {
            bool done = true;
            for (int j = 0; j < n; j++)
                if (Mathf.Abs(joints[j].jointPosition[0] * Mathf.Rad2Deg - joints[j].xDrive.target) > 0.5f)
                { done = false; break; }
            if (done) break;
            wait += Time.fixedDeltaTime;
            yield return new WaitForFixedUpdate();
        }
    }

    float[] CurrentTargets()
    {
        float[] targets = new float[n];
        for (int j = 0; j < n; j++)
        {
            targets[j] = joints[j].xDrive.target;
        }
        return targets;
    }

    void SetTarget(int j, float deg)
    {
        var d = joints[j].xDrive;
        d.target = deg;
        joints[j].xDrive = d;
    }

    #endregion

    #region 5. 확인과 보정 - 처진 만큼 다시 보정
    IEnumerator Correct(Vector3 goal)
    {
        Vector3 aimPos = goal;

        for (int pass = 0; pass <= correctionPasses; pass++)
        {
            Vector3 posError = goal - tip.position;

            if (pass == 0 && firstMove)
            {
                firstMove = false;
                if (posError.magnitude > 0.02f)
                    Debug.LogWarning("[ArmIK] 모델 예측과 실제 위치 차이가 큽니다. Joint Signs 값이 틀렸을 수 있습니다. Signs Calibrated 체크를 끄고 다시 확인하세요.");
            }

            if (posError.magnitude < positionTolerance || pass == correctionPasses) break;

            aimPos += posError;

            float[] currentAngles = CurrentTargets();
            float[] solution = new float[n];
            Solve(aimPos, activeYaw, currentAngles, solution);
            yield return Drive(solution, 0.2f, true);
            yield return Settle();
        }

        PositionError = (goal - tip.position).magnitude;
        AngleError = Vector3.Angle(ActualDir(), Vector3.down);
        YawError = 0f;
        if (activeYaw.HasValue)
        {
            YawErrorVector(ActualCloseAxis(), activeYaw.Value, out float yawErrorDeg);
            YawError = yawErrorDeg;
        }
    }

    Vector3 ActualDir()
    {
        Vector3 wristPivot = joints[n - 1].transform.TransformPoint(anchorPos[n - 1]);
        return (tip.position - wristPivot).normalized;
    }

    Vector3 ActualCloseAxis()
    {
        Vector3 slideAxisLocal = closingFinger.anchorRotation * Vector3.right;
        return (closingFinger.transform.rotation * slideAxisLocal).normalized;
    }
    #endregion

    #region 화면 표시

    // 화면 위쪽에 IK 상태, 마지막 오차, 계산 성공 여부를 보여준다
    void OnGUI()
    {
        GUILayout.BeginArea(new Rect(300, 10, 290, 95), GUI.skin.box);
        GUILayout.Label(!Ready ? "IK: 회전 방향 확인 중..." : IsMoving ? "IK: 이동 중" : "IK: 대기");
        GUILayout.Label($"마지막 오차: {PositionError * 100f:F2} cm / 수직 {AngleError:F1}도 / 손목 {YawError:F1}도");
        GUILayout.Label($"경로 계산: {(LastSolveOk ? "성공" : "닿을 수 없음")}");
        GUILayout.EndArea();
    }

    #endregion
}