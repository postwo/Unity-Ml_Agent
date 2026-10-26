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
    public Transform tip;
    public ArticulationBody closingFinger;

    [Header("관절 회전 방향 (처음 한 번 자동 확인 후 저장)")]
    public bool signsCalibrated = false;
    public float[] jointSigns = new float[0];

    [Header("자세")]
    [Tooltip("그리퍼가 항상 수직 아래를 보도록")]
    public bool pointDown = true;
    [Tooltip("자세 오차 1 rad를 위치 오차 몇 m로 볼지")]
    public float orientationWeight = 0.2f;

    [Header("이동")]
    public float jointSpeed = 60f;
    public float minMoveTime = 0.3f;
    public float linearStep = 0.01f;
    public int correctionPasses = 3;
    public float positionTolerance = 0.001f;

    [Header("테스트: IKTarget을 끌면 따라가기 (IKTarget의 Rotation Y = 손목 각도)")]
    public Transform testTarget;
    public bool followTestTarget = false;

    public bool Ready { get; private set; }
    public bool IsMoving { get; private set; }
    public bool LastSolveOk { get; private set; } = true;
    public float PositionError { get; private set; }
    public float AngleError { get; private set; }
    public float YawError { get; private set; }

    #endregion

    #region 계산용 모델 변수

    private int n;
    private Transform baseT;
    private Quaternion[] relRot0;
    private Vector3[] pivotInParent,
                      anchorPos,
                      axisLocal;
    private float[] theta0,
                    lower, upper;
    private bool[] limited;
    private Vector3 tipLocal,
                    dirLocal,
                    closeLocal;

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
        baseT = joints[0].transform.parent;
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
        dirLocal = (tipLocal - anchorPos[n - 1]).normalized;
        closeLocal = handInv * ActualCloseAxis();
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
        IsMoving = true;
        activeYaw = yawDeg;

        float[] current = CurrentTargets();
        float[] solution = new float[n];

        LastSolveOk = Solve(goal, activeYaw, current, solution);
        if (!LastSolveOk) Debug.LogWarning($"[ArmIK] {goal} (손목 {yawDeg}도) 에 정확히 닿는 자세가 없습니다. 가장 가까운 자세로 이동합니다.");

        yield return Drive(solution, MoveTime(current, solution), true);
        yield return Settle();

        yield return Correct(goal);

        IsMoving = false;
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

    static float[] SolveLinear(float[,] matrix, float[] rhs, int size)
    {
        float[,] mat = (float[,])matrix.Clone();
        float[] vec = (float[])rhs.Clone();

        for (int col = 0; col < size; col++)
        {
            int bestRow = col;
            for (int row = col + 1; row < size; row++)
            {
                if (Mathf.Abs(mat[row, col]) > Mathf.Abs(mat[bestRow, col])) bestRow = row;
            }
            if (Mathf.Abs(mat[bestRow, col]) < 1e-9f) return null;

            if (bestRow != col)
            {
                for (int k = 0; k < size; k++)
                {
                    float temp = mat[col, k]; mat[col, k] = mat[bestRow, k]; mat[bestRow, k] = temp;
                }
                float tempVec = vec[col]; vec[col] = vec[bestRow]; vec[bestRow] = tempVec;
            }

            for (int row = col + 1; row < size; row++)
            {
                float factor = mat[row, col] / mat[col, col];
                for (int k = col; k < size; k++) mat[row, k] -= factor * mat[col, k];
                vec[row] -= factor * vec[col];
            }
        }

        float[] answer = new float[size];
        for (int row = size - 1; row >= 0; row--)
        {
            float sum = vec[row];
            for (int k = row + 1; k < size; k++) sum -= mat[row, k] * answer[k];
            answer[row] = sum / mat[row, row];
        }
        return answer;
    }

    #endregion

    #region 4. 실제로 움직이기 - xDrive target 바꾸고 기다리기

    float MoveTime(float[] fromAngles, float[] toAngles)
    {
        float maxTurn = 0f;
        for (int j = 0; j < n; j++)
        {
            maxTurn = Mathf.Max(maxTurn, Mathf.Abs(toAngles[j] - fromAngles[j]));
        }
        return Mathf.Max(minMoveTime, maxTurn / jointSpeed);
    }

    IEnumerator Drive(float[] goalAngles, float duration, bool smooth)
    {
        float[] startAngles = CurrentTargets();
        float progress = 0f;
        while (progress < 1f)
        {
            progress += Time.fixedDeltaTime / duration;

            float blend = smooth ? Mathf.SmoothStep(0f, 1f, progress) : Mathf.Clamp01(progress);

            for (int j = 0; j < n; j++)
            {
                SetTarget(j, Mathf.Lerp(startAngles[j], goalAngles[j], blend));
            }
            yield return new WaitForFixedUpdate();
        }
    }

    IEnumerator Settle()
    {
        float waited = 0f;
        while (waited < 1f)
        {
            bool allArrived = true;
            for (int j = 0; j < n; j++)
            {
                float actualDeg = joints[j].jointPosition[0] * Mathf.Rad2Deg;
                if (Mathf.Abs(actualDeg - joints[j].xDrive.target) > 0.5f)
                {
                    allArrived = false;
                    break;
                }
            }
            if (allArrived) break;

            waited += Time.fixedDeltaTime;
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
