using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public enum DefectType { None, Offset, WrongPart, Missing }

public enum InspectClass { OK, Missing, Defect }

public class AssemblySequence : MonoBehaviour
{
    #region [2편까지] 조립 동작 (1~9)
        #region 1. 준비와 대기 위치
        [Header("연결")]
        public ArmIK ik;   // 비워두면 같은 오브젝트에서 찾음
        public GripperController gripper;   // gripper_base
        public Transform partStopPoint;   // 부품 벨트 위 멈추는 자리 (벨트 윗면 높이)
        public Transform basePoint;   // 베이스 벨트 위 조립 자리 (벨트 윗면 높이)

        [Header("대기 위치")]
        public Vector3 homePosition = new Vector3(0.22f, 0.20f, 0f);
        [Tooltip("대기 위치로 돌아갈 때 관절 속도 (도/초). 매번 같은 관절 자세로 돌아가 손목 꼬임을 풂")]
        public float homeJointSpeed = 90f;

        public bool IsBusy { get; private set; }   // 동작 중인지 (동작 중에는 새 명령을 받지 않음)
        public string Status { get; private set; } = "대기";   // 지금 단계 (화면 표시용)

        BaseInfo currentBase;   // 지금 조립 대상 베이스
        float[] homeJoints;   // 처음 대기 위치의 관절 각도 6개 (매 사이클 여기로 복귀)

        ArticulationBody Wrist => ik.joints[ik.joints.Length - 1];   // 손목 관절
        float WristAngle => Wrist.jointPosition[0] * Mathf.Rad2Deg;   // 지금 손목 관절 각도 (도)

        string WristInfo() =>
            $"{WristAngle:F0}도 (한계 {Wrist.xDrive.lowerLimit:F0}~{Wrist.xDrive.upperLimit:F0}도)";

        void Awake()
        {
            if (ik == null)
            {
                ik = GetComponent<ArmIK>();
            }
        }

        IEnumerator Start()
        {
            IsBusy = true;

            if (basePoint != null)
            {
                foreach (BaseInfo sceneBase in FindObjectsByType<BaseInfo>())
                {
                    if ((sceneBase.transform.position - basePoint.position).sqrMagnitude < 0.0004f)
                    {
                        currentBase = sceneBase;
                        break;
                    }
                }
            }

            yield return new WaitUntil(() => ik.Ready);
            Status = "대기 위치로";
            yield return ik.MoveTo(homePosition, 0f);

            homeJoints = new float[ik.joints.Length];
            for (int jointIndex = 0; jointIndex < homeJoints.Length; jointIndex++)
            {
                homeJoints[jointIndex] = ik.joints[jointIndex].xDrive.target;
            }
            Debug.Log($"[Assembly] 대기 자세 저장 | 손목 관절 {WristInfo()}");
            Status = "대기";
            IsBusy = false;
        }

        IEnumerator GoHome()
        {
            if (homeJoints == null)
            {
                yield return ik.MoveTo(homePosition, 0f);
                yield break;
            }

            ArticulationBody[] joints = ik.joints;
            float[] startAngles = new float[joints.Length];
            float maxTurn = 0f;   // 가장 많이 돌아야 하는 관절의 각도
            for (int jointIndex = 0; jointIndex < joints.Length; jointIndex++)
            {
                startAngles[jointIndex] = joints[jointIndex].xDrive.target;
                maxTurn = Mathf.Max(maxTurn, Mathf.Abs(homeJoints[jointIndex] - startAngles[jointIndex]));
            }

            float duration = Mathf.Max(0.5f, maxTurn / Mathf.Max(1f, homeJointSpeed));

            for (float progress = 0f; progress < 1f;)
            {
                progress += Time.fixedDeltaTime / duration;
                float blend = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(progress));
                for (int jointIndex = 0; jointIndex < joints.Length; jointIndex++)
                {
                    ArticulationDrive drive = joints[jointIndex].xDrive;
                    drive.target = Mathf.Lerp(startAngles[jointIndex], homeJoints[jointIndex], blend);
                    joints[jointIndex].xDrive = drive;
                }
                yield return new WaitForFixedUpdate();
            }

            for (float waited = 0f; waited < 1.5f; waited += Time.fixedDeltaTime)
            {
                bool allArrived = true;
                for (int jointIndex = 0; jointIndex < joints.Length; jointIndex++)
                {
                    if (Mathf.Abs(joints[jointIndex].jointPosition[0] * Mathf.Rad2Deg - homeJoints[jointIndex]) > 0.5f)
                    {
                        allArrived = false;
                        break;
                    }
                }
                if (allArrived)
                {
                    break;
                }
                yield return new WaitForFixedUpdate();
            }
        }
        #endregion

        #region 2. 부품과 베이스 준비
        [Header("프리팹")]
        [Tooltip("PartInfo가 붙은 부품 프리팹 (CubePart, PrismPart)")]
        public PartInfo[] partPrefabs;
        [Tooltip("BaseInfo가 붙은 베이스 프리팹 (BaseSquare, BasePrism)")]
        public BaseInfo[] basePrefabs;

        [Header("베이스 배치 흔들기 (컨베이어로 들어올 때 어긋남 재현)")]
        [Tooltip("켜면 베이스를 놓을 때 위치,방향을 무작위로 (베이스 인식 학습과 조립 모두 적용)")]
        public bool randomizeBase = true;
        [Tooltip("BasePoint에서 X, Z 각각 +- 이 거리 안에서 무작위 (m)")]
        public float baseOffsetRange = 0.005f;
        [Tooltip("켜면 방향을 0/90/180/270도 중 무작위, 끄면 0도")]
        public bool baseRightAngles = true;

        [Header("부품 생성 범위 (PartStopPoint 기준, m)")]
        public float spawnRangeX = 0.01f;   // +-1cm (벨트가 멈추는 위치의 흔들림)
        public float spawnRangeZ = 0.03f;   // +-3cm (벨트 가장자리에 손가락이 닿지 않는 범위)
        public bool randomRotation = true;   // 끄면 회전 0도
        public float settleTime = 0.5f;   // 생성 후 안착 대기 (초)

        Rigidbody currentPart;   // 지금 조립 대상 부품 (물리)
        PartInfo currentPartInfo;   // 같은 부품의 정보 (종류, 크기, 대칭 등)

        public PartInfo CurrentPart => currentPartInfo;   // 지금 벨트 위(또는 조립된) 부품 (다른 스크립트가 읽음)
        public BaseInfo CurrentBase => currentBase;   // 지금 베이스

        public void ClearPart()
        {
            if (currentPart != null)
            {
                if (gripper.HeldPart == currentPart)
                {
                    return;
                }
                currentPart.gameObject.SetActive(false);
                Destroy(currentPart.gameObject);
            }
            currentPart = null;
            currentPartInfo = null;
        }

        public void SpawnPart(int index)
        {
            if (partPrefabs == null || index < 0 || index >= partPrefabs.Length)
            {
                return;
            }
            ClearPart();

            PartInfo prefab = partPrefabs[index];
            Vector3 stopPosition = partStopPoint.position;
            Vector3 spawnPosition = new Vector3(
                stopPosition.x + Random.Range(-spawnRangeX, spawnRangeX),
                stopPosition.y + prefab.halfHeight + 0.002f,
                stopPosition.z + Random.Range(-spawnRangeZ, spawnRangeZ));
            float spawnYaw = randomRotation ? Random.Range(0f, 180f) : 0f;

            PartInfo spawnedPart = Instantiate(prefab, spawnPosition, Quaternion.Euler(0f, spawnYaw, 0f));
            currentPartInfo = spawnedPart;
            currentPart = spawnedPart.GetComponent<Rigidbody>();
            Status = $"{prefab.name} 생성 (회전 {spawnYaw:F0}도)";
        }

        public void SpawnBase(int index)
        {
            if (basePrefabs == null || index < 0 || index >= basePrefabs.Length)
            {
                return;
            }
            if (currentBase != null)
            {
                currentBase.gameObject.SetActive(false);
                Destroy(currentBase.gameObject);
            }

            Vector3 spawnPosition = basePoint.position;
            float spawnYaw = 0f;
            if (randomizeBase)
            {
                spawnPosition.x += Random.Range(-baseOffsetRange, baseOffsetRange);
                spawnPosition.z += Random.Range(-baseOffsetRange, baseOffsetRange);
                if (baseRightAngles)
                {
                    spawnYaw = 90f * Random.Range(0, 4);
                }
            }
            currentBase = Instantiate(basePrefabs[index], spawnPosition, Quaternion.Euler(0f, spawnYaw, 0f));
            Status = $"{basePrefabs[index].name} 배치 (회전 {spawnYaw:F0}도)";
        }

        public int FindPartIndex(PartType type)
        {
            for (int i = 0; i < partPrefabs.Length; i++)
            {
                if (partPrefabs[i] != null && partPrefabs[i].partType == type)
                {
                    return i;
                }
            }
            return -1;
        }

        public int FindBaseIndex(PartType type)
        {
            for (int i = 0; i < basePrefabs.Length; i++)
            {
                if (basePrefabs[i] != null && basePrefabs[i].holeType == type)
                {
                    return i;
                }
            }
            return -1;
        }
        #endregion

        #region 3. 잡을 손목 각도
        [Header("손목 각도")]
        [Tooltip("잡을 때 고를 수 있는 손목 각도 (에이전트의 행동과 같음)")]
        public float yawStep = 15f;
        public int yawCount = 12;   // 0, 15, ... 165도

        public float HeuristicGraspYaw(PartInfo part)
        {
            float symmetry = part.symmetryDeg;   // 큐브 90, 삼각기둥 180
            float partYaw = part.transform.eulerAngles.y;
            float bestYaw = 0f;
            float bestError = float.MaxValue;
            for (int action = 0; action < yawCount; action++)
            {
                float yaw = action * yawStep;
                float error = Mathf.Abs(Mathf.Repeat(yaw - partYaw + symmetry * 0.5f, symmetry) - symmetry * 0.5f);
                if (error < bestError)
                {
                    bestError = error;
                    bestYaw = yaw;
                }
            }
            return bestYaw;
        }
        #endregion

        #region 4. 조립 계산 도구 (구멍 위치와 방향, 끼움 판정)
        [Header("끼움 판정")]
        public float seatHeightTolerance = 0.003f;   // 부품 바닥이 구멍 바닥에서 +-3mm
        public float seatCenterTolerance = 0.004f;   // 구멍 중심에서 4mm 이내
        public float seatTiltTolerance = 5f;   // 기울기 5도 이하

        bool useCellTarget;
        float cellHoleYaw;   // 셀 운전 중 쓸 구멍 방향 (베이스 인식 결과)

        Transform GripPoint => gripper.attachPoint != null ? gripper.attachPoint : ik.tip;

        static Vector3 FlatDelta(Vector3 a, Vector3 b) => new Vector3(a.x - b.x, 0f, a.z - b.z);

        Vector3 HoleCenterOf(BaseInfo baseInfo) => useCellTarget ? basePoint.position : baseInfo.HoleCenter;
        float TopYOf(BaseInfo baseInfo) => useCellTarget ? basePoint.position.y + baseInfo.topHeight : baseInfo.TopY;
        float HoleYawOf(BaseInfo baseInfo) => useCellTarget ? cellHoleYaw : baseInfo.HoleYaw;

        float PartHoleYawErr(PartInfo part, BaseInfo baseInfo)
        {
            float symmetry = part.symmetryDeg;
            return Mathf.Repeat(part.transform.eulerAngles.y - HoleYawOf(baseInfo) + symmetry * 0.5f, symmetry) - symmetry * 0.5f;
        }

        bool CheckSeated(PartInfo part, BaseInfo baseInfo, out string info)
        {
            Transform partTransform = part.transform;
            float bottom = partTransform.position.y - part.halfHeight;   // 부품 바닥 높이
            float heightError = bottom - baseInfo.FloorY;   // 구멍 바닥과의 차이
            Vector2 centerOffset = new Vector2(partTransform.position.x - baseInfo.HoleCenter.x,
                                               partTransform.position.z - baseInfo.HoleCenter.z);
            float tilt = Vector3.Angle(partTransform.up, Vector3.up);   // 기울기

            bool okHeight = Mathf.Abs(heightError) <= seatHeightTolerance;
            bool okCenter = centerOffset.magnitude <= seatCenterTolerance;
            bool okTilt = tilt <= seatTiltTolerance;
            bool ok = okHeight && okCenter && okTilt;

            info = $"{(ok ? "끼움 성공" : "끼움 실패")} | 높이 {heightError * 1000f:F1}mm, 중심 {centerOffset.magnitude * 1000f:F1}mm, 기울기 {tilt:F1}도";
            if (!baseInfo.Accepts(part))
            {
                info += " (틀린 조합)";
            }
            return ok;
        }
        #endregion

        #region 5. 손목 돌리기 (관절 한계 관리)
        [Header("손목 관절 한계 관리")]
        [Tooltip("손목(hand_link) 관절이 한계에서 이만큼은 떨어져 있도록 회전 방향을 고름 (도)")]
        public float wristLimitMargin = 10f;
        [Tooltip("손목을 크게 돌릴 때 한 번에 돌리는 최대 각도 (90도 미만이어야 원하는 방향으로 돎)")]
        public float wristStepDeg = 60f;

        bool WristLimited => Wrist.xDrive.lowerLimit < Wrist.xDrive.upperLimit;   // 손목에 각도 제한이 있는지

        float WristYawRate()
        {
            ArticulationBody wrist = Wrist;
            Vector3 axisWorld = wrist.transform.rotation * (wrist.anchorRotation * Vector3.right);   // 손목 회전축 (월드)
            float sign = (ik.jointSigns != null && ik.jointSigns.Length == ik.joints.Length) ? ik.jointSigns[ik.joints.Length - 1] : 1f;
            return sign * Mathf.Sign(Vector3.Dot(axisWorld, Vector3.up));
        }

        float WristOverLimit(float angle)
        {
            if (!WristLimited)
            {
                return 0f;
            }
            float lowest = Wrist.xDrive.lowerLimit + wristLimitMargin;
            float highest = Wrist.xDrive.upperLimit - wristLimitMargin;
            return Mathf.Max(0f, lowest - angle) + Mathf.Max(0f, angle - highest);
        }

        float ChooseRotation(float rotation, float symmetry, float avoidDirection = 0f)
        {
            float rate = WristYawRate();
            float nowAngle = WristAngle;
            float bestRotation = rotation;
            float bestScore = float.MaxValue;
            for (int k = -3; k <= 3; k++)
            {
                float candidate = rotation + k * symmetry;
                if (Mathf.Abs(candidate) > 200f)
                {
                    continue;
                }
                if (avoidDirection != 0f && Mathf.Sign(candidate) == avoidDirection && Mathf.Abs(candidate) > 1f)
                {
                    continue;
                }
                float score = WristOverLimit(nowAngle + candidate * rate) * 1000f + Mathf.Abs(candidate);
                if (score < bestScore)
                {
                    bestScore = score;
                    bestRotation = candidate;
                }
            }
            return bestRotation;
        }

        IEnumerator RotateWrist(Vector3 position, float fromYaw, float rotation)
        {
            int stepCount = Mathf.Max(1, Mathf.CeilToInt(Mathf.Abs(rotation) / Mathf.Max(10f, wristStepDeg)));
            for (int i = 1; i <= stepCount; i++)
            {
                yield return ik.MoveTo(position, fromYaw + rotation * i / stepCount);
            }
        }
        #endregion

        #region 6. 진단 로그 (조립 한 번의 기록)
        [Header("진단 로그")]
        [Tooltip("매 시도마다 [AssemblyDiag] 한 줄을 남김")]
        public bool logDiagnostics = true;

        System.Text.StringBuilder diagnosticLog = new System.Text.StringBuilder();

        void AddDiagnostic(string text)
        {
            if (diagnosticLog.Length > 0)
            {
                diagnosticLog.Append(" | ");
            }
            diagnosticLog.Append(text);
        }

        string IkInfo() =>
            $"IK {(ik.LastSolveOk ? "ok" : "닿지않음")} 위치 {ik.PositionError * 1000f:F1}mm 손목 {ik.YawError:F1}도";

        static string HeldOffsetInfo(Vector3 flatOffset, float wristYaw)
        {
            Vector3 closeAxis = Quaternion.Euler(0f, wristYaw, 0f) * Vector3.forward;
            Vector3 side = Vector3.Cross(Vector3.up, closeAxis);
            return $"닫힘축 {Vector3.Dot(flatOffset, closeAxis) * 1000f:F1}mm / 직각 {Vector3.Dot(flatOffset, side) * 1000f:F1}mm";
        }
        #endregion

        #region 7. 조립 동작 (이 스크립트의 핵심)
        [Header("높이 (월드 Y, m)")]
        public float pickAboveY = 0.16f;   // 부품 위 대기 높이
        public float placeAboveY = 0.17f;   // 베이스 위 대기 높이
        [Tooltip("그리퍼 기준점(AttachPoint)에서 손가락 끝까지 아래 방향 거리 (측정값 0.021)")]
        public float fingerTipDepth = 0.021f;
        [Tooltip("놓는 순간 손가락 끝과 베이스 윗면 사이 여유")]
        public float releaseClearance = 0.002f;

        [Header("속도 (m/s)")]
        public float travelSpeed = 0.15f;
        public float approachSpeed = 0.1f;
        public float insertSpeed = 0.02f;   // 베이스 위에서 놓는 높이까지 내려가는 속도
        public float releaseWait = 0.5f;   // 연 뒤 부품이 앉을 때까지 대기

        [Header("놓기 전 정렬 보정")]
        [Tooltip("손목을 돌린 뒤 부품 방향이 구멍과 이만큼 넘게 어긋나면 손목을 다시 맞춤 (도)")]
        public float yawAlignTolerance = 2f;
        [Tooltip("손 안에서 부품이 치우친 만큼 놓는 위치를 옮겨 부품 중심을 구멍 중심에 맞춤")]
        public bool compensateHeldOffset = true;
        [Tooltip("보정 후에도 이만큼 넘게 어긋나면 경고 (m, 도)")]
        public float warnDistance = 0.003f;
        public float warnYaw = 4f;

        public bool LastGraspOk { get; private set; }   // 마지막 조립에서 잡아 들어 올렸는지
        public bool LastSeated { get; private set; }   // 마지막 조립에서 끼워졌는지
        public string LastSeatInfo { get; private set; } = "-";   // 끼움 판정 문장

        int attemptCount;   // 조립 시도 수 (화면 표시)
        int successCount;   // 끼움 성공 수 (화면 표시)

        public IEnumerator Assemble(float? graspYaw = null)
        {
            if (IsBusy)
            {
                yield break;
            }
            IsBusy = true;
            LastGraspOk = false;
            LastSeated = false;
            LastSeatInfo = "-";
            diagnosticLog.Clear();

            if (currentPart == null || currentPartInfo == null)
            {
                Status = "부품 없음";
                IsBusy = false;
                yield break;
            }
            if (currentBase == null)
            {
                Status = "베이스 없음";
                IsBusy = false;
                yield break;
            }

            DefectType inject = suppressDefectOnce ? DefectType.None : RollDefect();
            suppressDefectOnce = false;
            LastInjected = inject;
            if (inject == DefectType.Missing)
            {
                Status = "[불량 주입] 부품 없음";
                yield return new WaitForSeconds(0.2f);
                Finish(false, "불량 주입: 부품 없음 (조립 안 함)", InspectClass.Missing);
                yield break;
            }
            if (inject == DefectType.WrongPart)
            {
                PartType otherType = currentPartInfo.partType == PartType.Cube ? PartType.Prism : PartType.Cube;
                int baseIndex = FindBaseIndex(otherType);
                if (baseIndex >= 0)
                {
                    SpawnBase(baseIndex);
                }
                yield return null;
            }

            PartInfo part = currentPartInfo;
            BaseInfo baseInfo = currentBase;
            float yaw = graspYaw ?? HeuristicGraspYaw(part);   // ?? = 왼쪽이 null이면 오른쪽 값 사용
            float symmetry = part.symmetryDeg;

            Vector3 partPosition = currentPart.position;
            Vector3 pickAbove = new Vector3(partPosition.x, pickAboveY, partPosition.z);   // 부품 위 대기 위치
            Vector3 pick = partPosition + Vector3.up * part.gripOffset;   // 잡는 위치 (부품마다 잡는 높이가 다름)
            AddDiagnostic($"잡기: 부품 {part.transform.eulerAngles.y:F1}도 손목 {yaw:F0}도");

            Status = $"1. 부품 위로 (손목 {yaw:F0}도)";
            yield return ik.MoveTo(pickAbove, yaw);
            AddDiagnostic($"부품 위 {IkInfo()}");

            Status = "2. 내려가기";
            yield return ik.MoveLinear(pick, approachSpeed, yaw);
            Vector3 pickOffset = FlatDelta(currentPart.position, GripPoint.position);
            AddDiagnostic($"내려간 뒤 {IkInfo()}, 손-부품 중심 {pickOffset.magnitude * 1000f:F1}mm ({HeldOffsetInfo(pickOffset, yaw)})");

            Status = "3. 잡기";
            yield return gripper.Close();

            if (gripper.HeldPart == null)
            {
                Status = $"잡기 실패 - {gripper.LastGraspInfo}";
                yield return ik.MoveLinear(pickAbove, approachSpeed, yaw);
                yield return gripper.Open();
                yield return GoHome();
                Finish(false, "잡기 실패", InspectClass.Missing);
                yield break;
            }

            Status = "4. 들어 올리기";
            yield return ik.MoveLinear(pickAbove, approachSpeed, yaw);
            if (gripper.HeldPart == null)
            {
                yield return GoHome();
                Finish(false, "들어 올리는 중 놓침", InspectClass.Missing);
                yield break;
            }
            LastGraspOk = true;
            float partYawLift = part.transform.eulerAngles.y;   // 들어 올린 직후 부품 방향
            Vector3 heldOffset = FlatDelta(part.transform.position, GripPoint.position);   // 손 안에서 부품이 치우친 정도
            AddDiagnostic($"들어올린 뒤 부품 {partYawLift:F1}도, 손 안 치우침 {heldOffset.magnitude * 1000f:F1}mm ({HeldOffsetInfo(heldOffset, yaw)}), 손목관절 {WristInfo()}");

            Vector3 hole = HoleCenterOf(baseInfo);
            Vector3 placeAbove = new Vector3(hole.x, placeAboveY, hole.z);
            float releaseY = TopYOf(baseInfo) + releaseClearance + fingerTipDepth;   // 손가락 끝이 베이스 윗면 2mm 위에 오는 높이

            Status = "5. 베이스 위로";
            yield return ik.MoveLinear(placeAbove, travelSpeed, null);

            System.Func<float> ActualWrist = () => yaw + Mathf.DeltaAngle(partYawLift, part.transform.eulerAngles.y);
            float placeYaw = ActualWrist();
            float yawError = PartHoleYawErr(part, baseInfo);   // 부품 방향과 구멍 방향의 차이
            AddDiagnostic($"베이스 위 손목 {placeYaw:F1}도, 손목관절 {WristInfo()}, 부품-구멍 각도 {yawError:F1}도");

            float avoidDirection = 0f;
            for (int attempt = 0; attempt < 2 && Mathf.Abs(yawError) > yawAlignTolerance; attempt++)
            {
                float rotation = ChooseRotation(-yawError, symmetry, avoidDirection);
                float predicted = WristAngle + rotation * WristYawRate();   // 예상 관절 각도 (로그용)
                Status = $"6. 손목 {rotation:F0}도 돌리기";
                yield return RotateWrist(placeAbove, placeYaw, rotation);
                avoidDirection = Mathf.Sign(rotation);
                placeYaw = ActualWrist();
                yawError = PartHoleYawErr(part, baseInfo);
                AddDiagnostic($"회전{attempt + 1} {rotation:F1}도 (예상 관절 {predicted:F0}도 -> 실제 {WristAngle:F0}도) -> {IkInfo()}, 각도 {yawError:F1}도");

                if (Mathf.Abs(yawError) <= yawAlignTolerance)
                {
                    break;
                }
                if (Mathf.Abs(yawError) < 15f)
                {
                    Status = "6-1. 손목 미세 보정";
                    yield return ik.MoveTo(placeAbove, placeYaw - yawError);
                    placeYaw = ActualWrist();
                    yawError = PartHoleYawErr(part, baseInfo);
                    AddDiagnostic($"미세 보정 -> {IkInfo()}, 각도 {yawError:F1}도");
                    break;
                }
            }

            Vector3 shift = Vector3.zero;
            Vector3 partToHole;
            if (compensateHeldOffset)
            {
                shift = FlatDelta(part.transform.position, GripPoint.position);
                Vector3 alignedAbove = placeAbove - shift;
                Status = "6-2. 부품 중심을 구멍 위로";
                yield return ik.MoveLinear(alignedAbove, approachSpeed, placeYaw);
                partToHole = FlatDelta(part.transform.position, hole);
                AddDiagnostic($"위치 보정 {shift.magnitude * 1000f:F1}mm -> {IkInfo()}, 거리 {partToHole.magnitude * 1000f:F1}mm");
            }
            Vector3 release = new Vector3(hole.x - shift.x, releaseY, hole.z - shift.z);   // 놓는 위치

            if (inject == DefectType.Offset)
            {
                float angle = Random.Range(0f, 360f) * Mathf.Deg2Rad;
                float distance = Random.Range(offsetMin, offsetMax);
                Vector3 offset = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * distance;
                release += offset;
                Status = $"[불량 주입] {distance * 1000f:F1}mm 비껴 놓기";
                yield return ik.MoveLinear(new Vector3(release.x, placeAboveY, release.z), approachSpeed, placeYaw);
                AddDiagnostic($"불량 주입: {distance * 1000f:F1}mm 비껴 놓기");
            }

            Status = "7. 놓는 높이까지 내려가기";
            yield return ik.MoveLinear(release, insertSpeed, placeYaw);
            yawError = PartHoleYawErr(part, baseInfo);
            partToHole = FlatDelta(part.transform.position, hole);
            bool stillHeld = gripper.HeldPart != null;
            bool alignedBeforeRelease = partToHole.magnitude <= warnDistance && Mathf.Abs(yawError) <= warnYaw;
            AddDiagnostic($"놓기 직전 {IkInfo()}, 각도 {yawError:F1}도 거리 {partToHole.magnitude * 1000f:F1}mm" +
                 (stillHeld ? "" : " (이미 놓침)") + (alignedBeforeRelease ? "" : " *정렬 경고"));

            Status = "8. 놓기";
            yield return gripper.Open();
            yield return new WaitForSeconds(releaseWait);

            Status = "9. 위로 빠지기";
            yield return ik.MoveLinear(new Vector3(release.x, placeAboveY, release.z), approachSpeed, placeYaw);

            bool seated = CheckSeated(part, baseInfo, out string info);

            Status = "10. 대기 위치로";
            yield return GoHome();

            Finish(seated, info, seated ? InspectClass.OK : InspectClass.Defect);
        }

        void Finish(bool seated, string info, InspectClass truth)
        {
            LastSeated = seated;
            LastSeatInfo = info;
            LastTruth = truth;
            attemptCount++;
            if (seated)
            {
                successCount++;
            }
            if (truth == InspectClass.OK)
            {
                truthOkCount++;
            }
            else if (truth == InspectClass.Missing)
            {
                truthMissingCount++;
            }
            else
            {
                truthDefectCount++;
            }
            Status = info;
            Debug.Log($"[Assembly] {info} | 부품 {(currentPartInfo != null ? currentPartInfo.partType.ToString() : "-")}, " +
                      $"베이스 {(currentBase != null ? currentBase.holeType.ToString() : "-")} | " +
                      $"주입 {DefectName[(int)LastInjected]} -> 실제 {TruthName[(int)truth]}");
            if (logDiagnostics && diagnosticLog.Length > 0)
            {
                string line = $"[AssemblyDiag] {(seated ? "성공" : "실패")} {(currentPartInfo != null ? currentPartInfo.partType.ToString() : "-")} | {diagnosticLog}";
                if (seated)
                {
                    Debug.Log(line);
                }
                else
                {
                    Debug.LogWarning(line);
                }
            }
            IsBusy = false;
        }
        #endregion

        #region 8. 학습용 잡기 테스트 (AssemblyGraspAgent가 사용)
        public IEnumerator PickTest(float graspYaw)
        {
            if (IsBusy)
            {
                yield break;
            }
            IsBusy = true;
            LastGraspOk = false;
            LastSeated = false;

            if (currentPart == null || currentPartInfo == null)
            {
                Status = "부품 없음";
                IsBusy = false;
                yield break;
            }

            Vector3 partPosition = currentPart.position;
            Vector3 pickAbove = new Vector3(partPosition.x, pickAboveY, partPosition.z);
            Vector3 pick = partPosition + Vector3.up * currentPartInfo.gripOffset;

            Status = $"[학습] 잡기 (손목 {graspYaw:F0}도)";
            yield return ik.MoveTo(pickAbove, graspYaw);
            yield return ik.MoveLinear(pick, approachSpeed, graspYaw);
            yield return gripper.Close();

            if (gripper.HeldPart != null)
            {
                yield return ik.MoveLinear(pickAbove, approachSpeed, graspYaw);
                LastGraspOk = gripper.HeldPart != null;
            }
            else
            {
                yield return ik.MoveLinear(pickAbove, approachSpeed, graspYaw);
            }

            yield return gripper.Open();
            yield return GoHome();
            Status = LastGraspOk ? "[학습] 잡기 성공" : "[학습] 잡기 실패";
            IsBusy = false;
        }
        #endregion

        #region 9. 테스트 화면
        [Header("테스트")]
        public bool showTestButtons = true;

        bool autoRunning;   // 자동 반복 중인지 (그동안 버튼 잠금)

        IEnumerator AutoRun(int count)
        {
            autoRunning = true;
            for (int i = 0; i < count; i++)
            {
                int partIndex = Random.Range(0, partPrefabs.Length);
                SpawnPart(partIndex);
                int baseIndex = FindBaseIndex(partPrefabs[partIndex].partType);
                if (baseIndex >= 0)
                {
                    SpawnBase(baseIndex);
                }
                yield return new WaitForSeconds(settleTime);
                yield return Assemble();
                yield return new WaitForSeconds(0.3f);
            }
            autoRunning = false;
            Status = $"자동 {count}회 끝: {successCount}/{attemptCount}";
        }

        void OnGUI()
        {
            if (!showTestButtons)
            {
                return;
            }

            GUILayout.BeginArea(new Rect(10, 215, 320, 380), GUI.skin.box);
            GUILayout.Label($"상태: {Status}");
            GUILayout.Label($"결과: {successCount}/{attemptCount} 성공");
            GUILayout.Label($"실제 상태: 양품 {truthOkCount} , 부품 없음 {truthMissingCount} , 불량 {truthDefectCount}");
            GUILayout.Label($"폐기: {rejectSuccessCount}/{rejectTryCount} 성공");
            GUILayout.BeginHorizontal();
            GUILayout.Label($"불량 주입 {defectRate * 100f:F0}%", GUILayout.Width(110));
            defectRate = GUILayout.HorizontalSlider(defectRate, 0f, 1f);
            GUILayout.EndHorizontal();

            GUI.enabled = !IsBusy && !autoRunning && ik != null && ik.Ready && !ik.IsMoving;

            GUILayout.BeginHorizontal();
            if (partPrefabs != null)
            {
                for (int i = 0; i < partPrefabs.Length; i++)
                {
                    if (partPrefabs[i] != null && GUILayout.Button($"{partPrefabs[i].name} 생성"))
                    {
                        SpawnPart(i);
                    }
                }
            }
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            if (basePrefabs != null)
            {
                for (int i = 0; i < basePrefabs.Length; i++)
                {
                    if (basePrefabs[i] != null && GUILayout.Button($"{basePrefabs[i].name} 배치"))
                    {
                        SpawnBase(i);
                    }
                }
            }
            GUILayout.EndHorizontal();

            randomRotation = GUILayout.Toggle(randomRotation, "부품 무작위 회전");

            if (GUILayout.Button("조립 실행"))
            {
                StartCoroutine(Assemble());
            }
            if (GUILayout.Button("자동 10회 (맞는 조합)"))
            {
                StartCoroutine(AutoRun(10));
            }
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("폐기 실행"))
            {
                StartCoroutine(Reject());
            }
            if (GUILayout.Button("재투입 실행"))
            {
                StartCoroutine(Refeed());
            }
            GUILayout.EndHorizontal();
            if (GUILayout.Button("결과 초기화"))
            {
                attemptCount = 0;
                successCount = 0;
                truthOkCount = 0;
                truthMissingCount = 0;
                truthDefectCount = 0;
                rejectTryCount = 0;
                rejectSuccessCount = 0;
            }

            GUI.enabled = true;
            GUILayout.EndArea();
        }
        #endregion
    #endregion

    #region [Part 2] 검사와 불량 처리 (10~12)
        #region 10. 불량 주입과 결과 판정
        [Header("불량 주입 (검사 학습,검증용)")]
        [Tooltip("조립 1회마다 이 확률로 일부러 불량을 만듦 (0 = 끔). 화면 슬라이더로도 조절")]
        [Range(0f, 1f)] public float defectRate = 0f;
        [Tooltip("불량 종류별 비중: 비껴 놓기(얹힘), 틀린 베이스(오조립), 부품 없음")]
        public float weightOffset = 1f;
        public float weightWrongPart = 1f;
        public float weightMissing = 1f;
        [Tooltip("비껴 놓기 거리 범위 (m). 구멍 가장자리까지 약 1.5~2cm라 이 정도면 얹힘")]
        public float offsetMin = 0.006f;
        public float offsetMax = 0.010f;

        public DefectType LastInjected { get; private set; } = DefectType.None;   // 마지막 조립에서 주입한 불량
        public InspectClass LastTruth { get; private set; } = InspectClass.OK;   // 마지막 조립의 실제 상태 (검사 정답)

        int truthOkCount;   // 실제 상태별 횟수 (화면 표시)
        int truthMissingCount;
        int truthDefectCount;
        bool suppressDefectOnce;   // 재투입 조립에는 불량을 주입하지 않음 (Refeed가 켬)

        static readonly string[] DefectName = { "없음", "비껴 놓기", "틀린 베이스", "부품 없음" };
        static readonly string[] TruthName = { "양품", "부품 없음", "불량" };

        DefectType RollDefect()
        {
            if (defectRate <= 0f || Random.value >= defectRate)
            {
                return DefectType.None;
            }
            float total = Mathf.Max(0f, weightOffset) + Mathf.Max(0f, weightWrongPart) + Mathf.Max(0f, weightMissing);
            if (total <= 0f)
            {
                return DefectType.None;
            }
            float remaining = Random.value * total;
            remaining -= Mathf.Max(0f, weightOffset);
            if (remaining < 0f)
            {
                return DefectType.Offset;
            }
            remaining -= Mathf.Max(0f, weightWrongPart);
            if (remaining < 0f)
            {
                return DefectType.WrongPart;
            }
            return DefectType.Missing;
        }

        public InspectClass EvaluateResult()
        {
            if (currentBase == null || currentPartInfo == null || !currentPartInfo.gameObject.activeInHierarchy)
            {
                return InspectClass.Missing;
            }
            Vector3 fromHole = currentPartInfo.transform.position - currentBase.HoleCenter;
            if (new Vector2(fromHole.x, fromHole.z).magnitude > 0.035f)
            {
                return InspectClass.Missing;
            }
            return CheckSeated(currentPartInfo, currentBase, out _) ? InspectClass.OK : InspectClass.Defect;
        }

        public PartInfo PlacePartAt(int index, Vector3 position, Quaternion rotation)
        {
            if (partPrefabs == null || index < 0 || index >= partPrefabs.Length)
            {
                return null;
            }
            ClearPart();
            PartInfo placedPart = Instantiate(partPrefabs[index], position, rotation);
            currentPartInfo = placedPart;
            currentPart = placedPart.GetComponent<Rigidbody>();
            return placedPart;
        }
        #endregion

        #region 11. 폐기와 재투입
        [Header("불량 처리 (폐기,재투입)")]
        [Tooltip("폐기함 RejectBin (기준점 = 폐기함 바닥 가운데)")]
        public Transform rejectBin;
        [Tooltip("폐기함 바닥에서 이 높이까지 내려가서 놓음 (m). 벽 높이 + 여유")]
        public float rejectDropHeight = 0.08f;
        [Tooltip("폐기 성공 = 폐기함 기준점에서 수평으로 이 거리 안 + 폐기함 바닥 아래로 빠지지 않음 (m). 폐기함 안쪽 절반 폭 정도.\n" +
                 "높이 위쪽 제한은 없음 (부품이 쌓여도 성공)")]
        public float rejectBinRadius = 0.05f;
        [Tooltip("폐기함에 남겨 둘 부품 수 (넘으면 오래된 것부터 지움)")]
        public int rejectBinKeep = 10;
        [Tooltip("폐기할 부품을 잡는 시도 횟수")]
        public int rejectGraspTries = 2;

        public bool LastRejectOk { get; private set; }
        public string LastRejectInfo { get; private set; } = "-";
        public GameObject LastDroppedPart { get; private set; }   // Reject(true)에서 폐기함 밖에 떨어져 남긴 부품

        int rejectTryCount;   // 폐기 시도 수 (화면 표시)
        int rejectSuccessCount;   // 폐기 성공 수 (화면 표시)
        readonly Queue<GameObject> rejectedParts = new Queue<GameObject>();   // 폐기함에 넣은 부품 (오래된 것부터 줄 서 있음)

        static float PartFlatYaw(PartInfo part)
        {
            Transform partTransform = part.transform;
            Vector3 forwardFlat = partTransform.forward;
            forwardFlat.y = 0f;
            if (forwardFlat.sqrMagnitude > 0.25f)
            {
                return Mathf.Atan2(forwardFlat.x, forwardFlat.z) * Mathf.Rad2Deg;
            }
            Vector3 rightFlat = partTransform.right;
            rightFlat.y = 0f;
            return Mathf.Atan2(rightFlat.x, rightFlat.z) * Mathf.Rad2Deg - 90f;
        }

        static float FlatGraspYaw(PartInfo part) => Mathf.Repeat(PartFlatYaw(part), part.symmetryDeg);

        static float PartTopY(PartInfo part)
        {
            float top = float.MinValue;
            foreach (Collider partCollider in part.GetComponentsInChildren<Collider>())
            {
                if (partCollider.enabled)
                {
                    top = Mathf.Max(top, partCollider.bounds.max.y);
                }
            }
            return top > float.MinValue ? top : part.transform.position.y + part.halfHeight;
        }

        public IEnumerator Reject(bool keepDropped = false)
        {
            if (IsBusy)
            {
                yield break;
            }
            IsBusy = true;
            LastRejectOk = false;
            LastDroppedPart = null;

            if (currentPart == null || currentPartInfo == null)
            {
                FinishReject(false, "폐기할 부품 없음");
                yield break;
            }
            if (rejectBin == null)
            {
                FinishReject(false, "Reject Bin이 비어 있음 (Inspector에서 연결)");
                yield break;
            }

            PartInfo part = currentPartInfo;
            Rigidbody partBody = currentPart;
            string who = $"부품 {part.partType}, 베이스 {(currentBase != null ? currentBase.holeType.ToString() : "-")}";

            float surfaceY = currentBase != null ? currentBase.TopY : partStopPoint.position.y;
            float minGripY = surfaceY + fingerTipDepth + 0.001f;

            Vector3 baseStartPosition = currentBase != null ? currentBase.transform.position : Vector3.zero;   // 진단용: 베이스가 밀렸는지
            bool held = false;

            for (int tryIndex = 0; tryIndex < Mathf.Max(1, rejectGraspTries) && !held; tryIndex++)
            {
                float yaw = FlatGraspYaw(part);
                Vector3 partCenter = partBody.position;
                float top = PartTopY(part);
                float pickY = Mathf.Max(top - (part.halfHeight - part.gripOffset), minGripY);
                Vector3 above = new Vector3(partCenter.x, pickAboveY, partCenter.z);

                Status = $"[폐기] 부품 위로 (손목 {yaw:F0}도, 시도 {tryIndex + 1})";
                yield return ik.MoveTo(above, yaw);
                Status = "[폐기] 내려가기";
                yield return ik.MoveLinear(new Vector3(partCenter.x, pickY, partCenter.z), approachSpeed, yaw);
                Status = "[폐기] 잡기";
                if (rejectDiagnostics)
                {
                    LogRejectDiagnostic($"잡기 직전 (시도 {tryIndex + 1}) | 손목 {yaw:F0}도, 부품 윗면 {(top - surfaceY) * 1000f:F1}mm (베이스 윗면 기준), " +
                          $"손 높이 {(GripPoint.position.y - surfaceY) * 1000f:F1}mm{(pickY <= minGripY + 0.0001f ? " (베이스 닿음 방지로 제한됨)" : "")}, " +
                          $"손-부품 중심 {FlatDelta(partBody.position, GripPoint.position).magnitude * 1000f:F1}mm, 부품 기울기 {PartTilt(part):F1}도");
                }
                yield return gripper.Close();
                yield return ik.MoveLinear(above, approachSpeed, yaw);
                held = gripper.HeldPart != null;
                if (held && rejectDiagnostics)
                {
                    Vector3 inHand = GripPoint.InverseTransformPoint(part.transform.position);   // 손 기준 부품 위치
                    float baseMoved = currentBase != null ? (currentBase.transform.position - baseStartPosition).magnitude * 1000f : 0f;
                    LogRejectDiagnostic($"들어올린 뒤 | 손 안 위치 (x {inHand.x * 1000f:F1}, y {inHand.y * 1000f:F1}, z {inHand.z * 1000f:F1})mm, " +
                          $"부품 기울기 {PartTilt(part):F1}도, 베이스 이동 {baseMoved:F1}mm");
                }
                if (!held)
                {
                    Debug.Log($"[Reject] 잡기 실패 (시도 {tryIndex + 1}) | {gripper.LastGraspInfo}");
                    yield return gripper.Open();
                    yield return new WaitForSeconds(0.3f);
                }
            }
            if (!held)
            {
                yield return GoHome();
                FinishReject(false, $"폐기 실패: 잡지 못함 | {who}");
                yield break;
            }

            Vector3 bin = rejectBin.position;
            Vector3 binAbove = new Vector3(bin.x, Mathf.Max(placeAboveY, bin.y + rejectDropHeight + 0.03f), bin.z);
            Status = "[폐기] 폐기함 위로";
            yield return ik.MoveLinear(binAbove, travelSpeed, null);
            if (rejectDiagnostics)
            {
                LogRejectDiagnostic($"폐기함 위 도착 | 잡고 있음 {(gripper.HeldPart == partBody ? "예" : "아니오 *옮기는 중 놓침")}, " +
                      $"부품-폐기함 중심 {FlatDelta(part.transform.position, bin).magnitude * 1000f:F0}mm, {IkInfo()}");
            }
            Status = "[폐기] 내려가기";
            yield return ik.MoveLinear(bin + Vector3.up * rejectDropHeight, approachSpeed, null);
            if (rejectDiagnostics)
            {
                LogRejectDiagnostic($"놓기 직전 | 잡고 있음 {(gripper.HeldPart == partBody ? "예" : "아니오 *내려가는 중 놓침")}, " +
                      $"부품 중심-폐기함 중심 {FlatDelta(part.transform.position, bin).magnitude * 1000f:F0}mm, " +
                      $"손-폐기함 중심 {FlatDelta(GripPoint.position, bin).magnitude * 1000f:F0}mm, " +
                      $"부품 바닥 높이 {(PartBottomY(part) - bin.y) * 1000f:F0}mm (폐기함 기준점 위), 부품 기울기 {PartTilt(part):F1}도, {IkInfo()}");
            }
            Status = "[폐기] 놓기";
            if (rejectDiagnostics)
            {
                StartCoroutine(TraceDrop(part, partBody, bin, 1.5f));
            }
            yield return gripper.Open();
            yield return new WaitForSeconds(releaseWait);
            yield return ik.MoveLinear(binAbove, approachSpeed, null);
            Status = "[폐기] 대기 위치로";
            yield return GoHome();

            float distance = FlatDelta(part.transform.position, bin).magnitude;
            float partHeight = part.transform.position.y - bin.y;
            bool inside = distance <= rejectBinRadius;
            bool fellThrough = partHeight < 0f;
            bool inBin = inside && !fellThrough;

            currentPart = null;
            currentPartInfo = null;
            if (inBin)
            {
                rejectedParts.Enqueue(part.gameObject);
                while (rejectedParts.Count > Mathf.Max(0, rejectBinKeep))
                {
                    GameObject oldestPart = rejectedParts.Dequeue();
                    if (oldestPart != null)
                    {
                        Destroy(oldestPart);
                    }
                }
                FinishReject(true, $"폐기 성공 | 폐기함 중심에서 {distance * 1000f:F0}mm, 높이 {partHeight * 1000f:F0}mm | {who}");
            }
            else
            {
                string why = fellThrough ? $"폐기함 바닥 아래로 빠짐 (높이 {partHeight * 1000f:F0}mm, 콜라이더,충돌 설정 확인)"
                                         : $"폐기함 밖에 떨어짐 (중심에서 {distance * 1000f:F0}mm)";
                if (keepDropped)
                {
                    LastDroppedPart = part.gameObject;
                    FinishReject(false, $"폐기 실패: {why}, 그 자리에 남김 | {who}");
                }
                else
                {
                    part.gameObject.SetActive(false);
                    Destroy(part.gameObject);
                    FinishReject(false, $"폐기 실패: {why}, 작업자가 치운 것으로 처리 | {who}");
                }
            }
        }

        void FinishReject(bool ok, string info)
        {
            LastRejectOk = ok;
            LastRejectInfo = info;
            rejectTryCount++;
            if (ok)
            {
                rejectSuccessCount++;
            }
            Status = info;
            if (ok)
            {
                Debug.Log($"[Reject] {info}");
            }
            else
            {
                Debug.LogWarning($"[Reject] {info}");
            }
            IsBusy = false;
        }

        public IEnumerator Refeed()
        {
            if (IsBusy)
            {
                yield break;
            }
            if (currentBase == null)
            {
                Status = "재투입: 베이스 없음";
                yield break;
            }

            bool onBelt = currentPartInfo != null && currentPartInfo.gameObject.activeInHierarchy
                          && currentPartInfo.partType == currentBase.holeType
                          && FlatDelta(currentPartInfo.transform.position, currentBase.HoleCenter).magnitude > 0.035f;
            if (!onBelt)
            {
                int partIndex = FindPartIndex(currentBase.holeType);
                if (partIndex < 0)
                {
                    Status = "재투입: 맞는 부품 프리팹 없음";
                    yield break;
                }
                SpawnPart(partIndex);
                yield return new WaitForSeconds(settleTime);
            }
            Debug.Log($"[Refeed] {(onBelt ? "벨트 위 부품" : "새 부품")}으로 다시 조립 | 베이스 {currentBase.holeType}");
            suppressDefectOnce = true;
            yield return Assemble();
        }
        #endregion

        #region 12. 폐기 진단 (선택: 폐기 문제를 찾을 때만 켬)
        [Tooltip("폐기 동작 진단 로그 [RejectDiag] (잡은 자세, 도착 상태, 놓은 뒤 부품 궤적)")]
        public bool rejectDiagnostics = true;

        void LogRejectDiagnostic(string text) => Debug.Log($"[RejectDiag] {text}");

        static float PartTilt(PartInfo part)
        {
            Transform partTransform = part.transform;
            float angle = Mathf.Min(Vector3.Angle(partTransform.up, Vector3.up), Vector3.Angle(-partTransform.up, Vector3.up));
            angle = Mathf.Min(angle, Mathf.Min(Vector3.Angle(partTransform.right, Vector3.up), Vector3.Angle(-partTransform.right, Vector3.up)));
            angle = Mathf.Min(angle, Mathf.Min(Vector3.Angle(partTransform.forward, Vector3.up), Vector3.Angle(-partTransform.forward, Vector3.up)));
            return angle;
        }

        static float PartBottomY(PartInfo part)
        {
            float bottom = float.MaxValue;
            foreach (Collider partCollider in part.GetComponentsInChildren<Collider>())
            {
                if (partCollider.enabled)
                {
                    bottom = Mathf.Min(bottom, partCollider.bounds.min.y);
                }
            }
            return bottom < float.MaxValue ? bottom : part.transform.position.y - part.halfHeight;
        }

        IEnumerator TraceDrop(PartInfo part, Rigidbody partBody, Vector3 bin, float duration)
        {
            Vector3 previousPosition = part.transform.position;
            float elapsed = 0f;
            float maxSpeed = 0f;
            float maxSpeedTime = 0f;
            float maxDistance = 0f;
            float farthestTime = 0f;
            float heightAtMaxSpeed = 0f;
            float distanceAtMaxSpeed = 0f;
            float exitTime = -1f;
            while (elapsed < duration && part != null)
            {
                yield return new WaitForFixedUpdate();
                elapsed += Time.fixedDeltaTime;
                Vector3 position = part.transform.position;
                float speed = (position - previousPosition).magnitude / Time.fixedDeltaTime;
                previousPosition = position;
                float distance = FlatDelta(position, bin).magnitude;
                if (speed > maxSpeed)
                {
                    maxSpeed = speed;
                    maxSpeedTime = elapsed;
                    heightAtMaxSpeed = (position.y - bin.y) * 1000f;
                    distanceAtMaxSpeed = distance * 1000f;
                }
                if (distance > maxDistance)
                {
                    maxDistance = distance;
                    farthestTime = elapsed;
                }
                if (exitTime < 0f && distance > rejectBinRadius)
                {
                    exitTime = elapsed;
                }
            }
            if (part == null)
            {
                yield break;
            }
            Vector3 endPosition = part.transform.position;
            LogRejectDiagnostic($"놓은 뒤 {duration:F1}초 추적 | 최대 속도 {maxSpeed:F2}m/s @ {maxSpeedTime:F2}초 (그때 높이 {heightAtMaxSpeed:F0}mm, 중심에서 {distanceAtMaxSpeed:F0}mm) | " +
                  $"가장 멀어짐 {maxDistance * 1000f:F0}mm @ {farthestTime:F2}초 | " +
                  $"{(exitTime >= 0f ? $"폐기함 범위 벗어난 시각 {exitTime:F2}초 | " : "")}" +
                  $"마지막 위치 중심에서 {FlatDelta(endPosition, bin).magnitude * 1000f:F0}mm, 높이 {(endPosition.y - bin.y) * 1000f:F0}mm, kinematic {partBody.isKinematic}");
        }
        #endregion
    #endregion

    #region [Part 1] 인식 결과로 조립 (13)
        #region 13. 인식 결과로 조립
        public void SetBase(BaseInfo baseInfo) => currentBase = baseInfo;
        public void SetPart(PartInfo part)
        {
            currentPartInfo = part;
            currentPart = part != null ? part.GetComponent<Rigidbody>() : null;
        }

        public IEnumerator AssembleAt(float graspYaw, float holeYaw)
        {
            yield break;
        }

        [ContextMenu("정지 위치의 물건 등록")]
        void MenuRegister()
        {
            if (!Application.isPlaying)
            {
                return;
            }
            BaseInfo nearestBase = null;
            PartInfo nearestPart = null;
            float nearestBaseDistance = 0.01f;
            float nearestPartDistance = 0.05f;
            foreach (BaseInfo sceneBase in FindObjectsByType<BaseInfo>())
            {
                float distance = FlatDelta(sceneBase.transform.position, basePoint.position).magnitude;
                if (distance < nearestBaseDistance)
                {
                    nearestBaseDistance = distance;
                    nearestBase = sceneBase;
                }
            }
            foreach (PartInfo scenePart in FindObjectsByType<PartInfo>())
            {
                float distance = FlatDelta(scenePart.transform.position, partStopPoint.position).magnitude;
                if (distance < nearestPartDistance)
                {
                    nearestPartDistance = distance;
                    nearestPart = scenePart;
                }
            }
            SetBase(nearestBase);
            SetPart(nearestPart);
            Debug.Log($"[Assembly] 등록 | 베이스 {(nearestBase != null ? nearestBase.name : "없음")} | 부품 {(nearestPart != null ? nearestPart.name : "없음")}");
        }

        [ContextMenu("인식 결과로 조립")]
        void MenuAssembleAt()
        {
            if (!Application.isPlaying)
            {
                return;
            }
            BaseInspectAgent baseAgent = FindFirstObjectByType<BaseInspectAgent>();
            AssemblyGraspAgent graspAgent = FindFirstObjectByType<AssemblyGraspAgent>();
            if (baseAgent == null || graspAgent == null)
            {
                Debug.LogWarning("[Assembly] BaseInspectAgent / AssemblyGraspAgent를 찾지 못했습니다.");
                return;
            }
            StartCoroutine(AssembleAt(graspAgent.LastYaw, baseAgent.LastHoleYaw));
        }
        #endregion
    #endregion
}
